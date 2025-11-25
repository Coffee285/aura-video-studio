using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aura.Core.Interfaces;
using Aura.Core.Models;
using Aura.Core.Models.Generation;
using Aura.Core.Models.Ollama;
using Aura.Core.Models.RAG;
using Aura.Core.Services.RAG;
using Microsoft.Extensions.Logging;

namespace Aura.Providers.Llm;

/// <summary>
/// Ollama-based script provider with streaming support
/// Extends BaseLlmScriptProvider to integrate with script generation workflows
/// </summary>
public class OllamaScriptProvider : BaseLlmScriptProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly TimeSpan _timeout;
    private readonly RagContextBuilder? _ragContextBuilder;

    // Cache for availability check to avoid repeated calls
    private DateTime _lastAvailabilityCheck = DateTime.MinValue;
    private bool _lastAvailabilityResult = false;
    private readonly TimeSpan _availabilityCacheDuration = TimeSpan.FromSeconds(30);
    private readonly object _availabilityCacheLock = new object();

    public OllamaScriptProvider(
        ILogger<OllamaScriptProvider> logger,
        HttpClient httpClient,
        RagContextBuilder? ragContextBuilder = null,
        string baseUrl = "http://127.0.0.1:11434",
        string model = "llama3.1:8b-q4_k_m",
        int maxRetries = 3,
        int timeoutSeconds = 900) // 15 minutes - very lenient for slow systems and large models
        : base(logger, maxRetries, baseRetryDelayMs: 1000)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ragContextBuilder = ragContextBuilder;
        _baseUrl = ValidateBaseUrl(baseUrl);
        _model = model;
        _timeout = TimeSpan.FromSeconds(timeoutSeconds);

        // CRITICAL: Ensure HttpClient timeout is longer than provider timeout
        // HttpClient has a default 100-second timeout that would kill connections
        // before our 15-minute provider timeout is reached
        if (_httpClient.Timeout < TimeSpan.FromSeconds(timeoutSeconds + 60))
        {
            _logger.LogWarning(
                "HttpClient timeout ({HttpClientTimeout}s) is shorter than provider timeout ({ProviderTimeout}s). " +
                "Increasing HttpClient timeout to prevent premature cancellation.",
                _httpClient.Timeout.TotalSeconds, timeoutSeconds);
            _httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds + 300); // Add 5-minute buffer
        }

        _logger.LogInformation("OllamaScriptProvider initialized with baseUrl={BaseUrl}, model={Model}, timeout={Timeout}s, httpClientTimeout={HttpClientTimeout}s, RAG={RagEnabled}",
            _baseUrl, _model, timeoutSeconds, _httpClient.Timeout.TotalSeconds, _ragContextBuilder != null ? "enabled" : "disabled");
    }

    /// <summary>
    /// Validate and normalize the base URL
    /// </summary>
    private static string ValidateBaseUrl(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = "http://127.0.0.1:11434";
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException($"Invalid base URL: {baseUrl}", nameof(baseUrl));
        }

        return baseUrl.TrimEnd('/');
    }

    /// <summary>
    /// Core script generation logic using Ollama
    /// </summary>
    protected override async Task<Script> GenerateScriptCoreAsync(
        ScriptGenerationRequest request,
        CancellationToken cancellationToken)
    {
        var modelToUse = request.ModelOverride ?? _model;
        _logger.LogInformation("Generating script with Ollama for topic: {Topic}. ModelOverride: {ModelOverride}, DefaultModel: {DefaultModel}, UsingModel: {UsingModel}", 
            request.Brief.Topic, request.ModelOverride ?? "null", _model, modelToUse);

        var isAvailable = await IsServiceAvailableAsync(cancellationToken).ConfigureAwait(false);
        if (!isAvailable)
        {
            var errorMessage = $"Cannot connect to Ollama at {_baseUrl}. Please ensure Ollama is running: 'ollama serve'";
            _logger.LogError("Ollama availability check failed. {ErrorMessage}", errorMessage);
            throw new InvalidOperationException(errorMessage);
        }

        // RAG Context Retrieval - retrieve relevant context from ingested documents
        string ragContext = await RetrieveRagContextAsync(request, cancellationToken).ConfigureAwait(false);

        var startTime = DateTime.UtcNow;
        var prompt = BuildPrompt(request, ragContext);
        var model = request.ModelOverride ?? _model;

        // Retry logic for handling variable Ollama response times
        Exception? lastException = null;
        for (int attempt = 0; attempt <= _maxRetries; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    var backoffDelay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    _logger.LogInformation("Retrying Ollama script generation (attempt {Attempt}/{MaxRetries}) after {Delay}s delay",
                        attempt + 1, _maxRetries + 1, backoffDelay.TotalSeconds);
                    await Task.Delay(backoffDelay, cancellationToken).ConfigureAwait(false);
                }

                // Get LLM parameters from brief, with defaults
                var llmParams = request.Brief.LlmParameters;
                var temperature = request.TemperatureOverride ?? llmParams?.Temperature ?? 0.7;
                var maxTokens = llmParams?.MaxTokens ?? 2048;
                var topP = llmParams?.TopP ?? 0.9;
                var topK = llmParams?.TopK;

                // Ollama uses num_predict (not max_tokens) and supports top_k
                var options = new Dictionary<string, object>
                {
                    { "temperature", temperature },
                    { "top_p", topP },
                    { "num_predict", maxTokens }
                };

                if (topK.HasValue)
                {
                    options["top_k"] = topK.Value;
                }

                var requestBody = new
                {
                    model = model,
                    prompt = prompt,
                    stream = false,
                    options = options
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // CRITICAL FIX: Use independent timeout - don't link to parent token for timeout management
                // This prevents upstream components (frontend, API middleware) from cancelling our long-running operation
                // if they have shorter timeouts. The linked token approach would cancel if ANY upstream has a short timeout.
                using var cts = new CancellationTokenSource();
                cts.CancelAfter(_timeout); // 15 minutes - allows for slow local models, large models, and model loading

                // Still respect explicit user cancellation by checking the parent token
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException("Script generation was cancelled by user", cancellationToken);
                }

                _logger.LogInformation("Sending request to Ollama (attempt {Attempt}/{MaxRetries}, timeout: {Timeout:F1} minutes)",
                    attempt + 1, _maxRetries + 1, _timeout.TotalMinutes);

                _logger.LogInformation("Request sent to Ollama, awaiting response (timeout: {Timeout:F1} minutes, this may take a while for large models)...",
                    _timeout.TotalMinutes);

                // CRITICAL FIX: Start periodic heartbeat logging to show the system is still working
                // During a 15-minute wait, there's no visibility that the system is working without this
                var requestStartTime = DateTime.UtcNow;
                using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                var heartbeatTask = Task.Run(async () =>
                {
                    try
                    {
                        while (!heartbeatCts.Token.IsCancellationRequested)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(30), heartbeatCts.Token).ConfigureAwait(false);
                            var elapsed = DateTime.UtcNow - requestStartTime;
                            var remaining = _timeout.TotalSeconds - elapsed.TotalSeconds;
                            if (remaining > 0)
                            {
                                _logger.LogInformation(
                                    "Still awaiting Ollama response... ({Elapsed:F0}s elapsed, {Remaining:F0}s remaining before timeout)",
                                    elapsed.TotalSeconds,
                                    remaining);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when generation completes or fails - ignore
                    }
                }, heartbeatCts.Token);

                HttpResponseMessage response;
                try
                {
                    response = await _httpClient.PostAsync($"{_baseUrl}/api/generate", content, cts.Token)
                        .ConfigureAwait(false);
                }
                finally
                {
                    // Stop heartbeat logging regardless of success/failure
                    heartbeatCts.Cancel();
                    try
                    {
                        await heartbeatTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected
                    }
                }

                // Check for user cancellation after long operation
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException("Script generation was cancelled by user", cancellationToken);
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(CancellationToken.None).ConfigureAwait(false);
                    if (errorContent.Contains("model") && errorContent.Contains("not found"))
                    {
                        throw new InvalidOperationException(
                            $"Model '{model}' not found. Please pull the model first using: ollama pull {model}");
                    }
                    response.EnsureSuccessStatusCode();
                }

                var responseJson = await response.Content.ReadAsStringAsync(CancellationToken.None).ConfigureAwait(false);
                var responseDoc = JsonDocument.Parse(responseJson);

                if (!responseDoc.RootElement.TryGetProperty("response", out var responseText))
                {
                    throw new InvalidOperationException("Invalid response from Ollama: missing 'response' field");
                }

                var scriptText = responseText.GetString() ?? string.Empty;
                var duration = DateTime.UtcNow - startTime;

                _logger.LogInformation("Script generated successfully ({Length} characters) in {Duration:F1}s (attempt {Attempt})",
                    scriptText.Length, duration.TotalSeconds, attempt + 1);

                var scenes = ParseScriptIntoScenes(scriptText, request.PlanSpec);

                return new Script
                {
                    Title = request.Brief.Topic,
                    Scenes = scenes,
                    TotalDuration = scenes.Count > 0
                        ? TimeSpan.FromSeconds(scenes.Sum(s => s.Duration.TotalSeconds))
                        : request.PlanSpec.TargetDuration,
                    Metadata = new ScriptMetadata
                    {
                        ProviderName = "Ollama",
                        ModelUsed = model,
                        GenerationTime = duration,
                        TokensUsed = EstimateTokens(scriptText),
                        Tier = ProviderTier.Free
                    }
                };
            }
            catch (TaskCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
            {
                lastException = ex;
                var elapsed = DateTime.UtcNow - startTime;
                _logger.LogWarning(ex,
                    "Ollama request timed out after {Elapsed:F1}s (attempt {Attempt}/{MaxRetries}, timeout: {Timeout}s). " +
                    "This may be normal for slow models or when Ollama is loading the model. Will retry if attempts remain.",
                    elapsed.TotalSeconds, attempt + 1, _maxRetries + 1, _timeout.TotalSeconds);

                if (attempt >= _maxRetries)
                {
                    throw new InvalidOperationException(
                        $"Ollama request timed out after {_timeout.TotalSeconds}s ({_timeout.TotalMinutes:F1} minutes). " +
                        $"This can happen with large models or slow systems. The model '{model}' may be:\n" +
                        $"  - Still loading into memory (first request after Ollama start can take 2-5 minutes)\n" +
                        $"  - Generating on a slow CPU (some systems need 10-15 minutes for script generation)\n" +
                        $"  - A very large model (70B+ models can be extremely slow)\n" +
                        $"Suggestions:\n" +
                        $"  - Wait for Ollama to fully load the model (check 'ollama ps' in terminal)\n" +
                        $"  - Use a smaller/faster model (e.g., llama3.2:3b instead of llama3.1:8b)\n" +
                        $"  - Ensure Ollama has sufficient RAM (model size + 2GB minimum)\n" +
                        $"  - Check Ollama logs for errors: 'ollama logs'", ex);
                }
            }
            catch (InvalidOperationException ex)
            {
                // Non-retryable errors: model not found, invalid response format, etc.
                // These should fail immediately without retrying
                var isNonRetryable = ex.Message.Contains("not found") ||
                                     ex.Message.Contains("missing 'response' field") ||
                                     ex.Message.Contains("Invalid response");

                if (isNonRetryable)
                {
                    _logger.LogError(ex,
                        "Non-retryable error from Ollama (attempt {Attempt}): {Message}",
                        attempt + 1, ex.Message);
                    throw; // Fail immediately, don't retry
                }

                // For other InvalidOperationException, treat as retryable
                lastException = ex;
                _logger.LogWarning(ex,
                    "InvalidOperationException from Ollama (attempt {Attempt}/{MaxRetries}): {Message}",
                    attempt + 1, _maxRetries + 1, ex.Message);

                if (attempt >= _maxRetries)
                {
                    throw;
                }
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
                _logger.LogWarning(ex,
                    "Failed to connect to Ollama at {BaseUrl} (attempt {Attempt}/{MaxRetries})",
                    _baseUrl, attempt + 1, _maxRetries + 1);

                if (attempt >= _maxRetries)
                {
                    throw new InvalidOperationException(
                        $"Cannot connect to Ollama at {_baseUrl}. Please ensure Ollama is running: 'ollama serve'", ex);
                }
            }
            catch (Exception ex) when (attempt < _maxRetries)
            {
                lastException = ex;
                _logger.LogWarning(ex,
                    "Error generating script with Ollama (attempt {Attempt}/{MaxRetries}): {Message}",
                    attempt + 1, _maxRetries + 1, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating script with Ollama after all retries");
                throw;
            }
        }

        // Should not reach here, but handle it gracefully
        var finalDuration = DateTime.UtcNow - startTime;
        throw new InvalidOperationException(
            $"Failed to generate script with Ollama after {_maxRetries + 1} attempts in {finalDuration.TotalSeconds:F1}s. " +
            $"Last error: {lastException?.Message ?? "Unknown error"}. " +
            $"Please verify Ollama is running and model '{model}' is available.", lastException);
    }

    /// <summary>
    /// Stream-based script generation with real-time updates
    /// </summary>
    public override async IAsyncEnumerable<ScriptGenerationProgress> StreamGenerateAsync(
        ScriptGenerationRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting streaming generation with Ollama for topic: {Topic}",
            request.Brief.Topic);

        var isAvailable = await IsServiceAvailableAsync(cancellationToken).ConfigureAwait(false);
        if (!isAvailable)
        {
            var errorMessage = $"Cannot connect to Ollama at {_baseUrl}. Please ensure Ollama is running: 'ollama serve'";
            _logger.LogError("Ollama availability check failed. {ErrorMessage}", errorMessage);
            throw new InvalidOperationException(errorMessage);
        }

        // RAG Context Retrieval for streaming
        string ragContext = await RetrieveRagContextAsync(request, cancellationToken).ConfigureAwait(false);

        var prompt = BuildPrompt(request, ragContext);
        var model = request.ModelOverride ?? _model;

        var requestBody = new
        {
            model = model,
            prompt = prompt,
            stream = true,
            options = new
            {
                temperature = request.TemperatureOverride ?? 0.7,
                top_p = 0.9,
                num_predict = 2048
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/generate")
        {
            Content = content
        };

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to connect to Ollama at {BaseUrl}", _baseUrl);
            throw new InvalidOperationException(
                $"Cannot connect to Ollama at {_baseUrl}. Please ensure Ollama is running: 'ollama serve'", ex);
        }

        using (response)
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream);

            var buffer = new StringBuilder();
            var tokenCount = 0;

            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                OllamaStreamResponse? chunk;
                try
                {
                    chunk = JsonSerializer.Deserialize<OllamaStreamResponse>(line);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse streaming JSON line: {Line}", line);
                    continue;
                }

                if (chunk == null)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(chunk.Response))
                {
                    buffer.Append(chunk.Response);
                    tokenCount++;

                    const int expectedMaxTokens = 2048;
                    var percentComplete = chunk.Done ? 100 : Math.Min(95, (tokenCount * 100) / expectedMaxTokens);

                    yield return new ScriptGenerationProgress
                    {
                        Stage = "Generating",
                        PercentComplete = percentComplete,
                        PartialScript = buffer.ToString(),
                        Message = chunk.Done
                            ? $"Generation complete ({tokenCount} tokens)"
                            : $"Generating... ({tokenCount} tokens)"
                    };
                }

                if (chunk.Done)
                {
                    _logger.LogInformation("Streaming complete. Total tokens: {Tokens}",
                        chunk.EvalCount ?? tokenCount);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Get available models from Ollama
    /// </summary>
    public override async Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Fetching available Ollama models from {BaseUrl}/api/tags", _baseUrl);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            var response = await _httpClient.GetAsync($"{_baseUrl}/api/tags", cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var tagsDoc = JsonDocument.Parse(content);

            var models = new List<string>();

            if (tagsDoc.RootElement.TryGetProperty("models", out var modelsArray) &&
                modelsArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var modelElement in modelsArray.EnumerateArray())
                {
                    if (modelElement.TryGetProperty("name", out var nameProp))
                    {
                        var name = nameProp.GetString();
                        if (!string.IsNullOrEmpty(name))
                        {
                            models.Add(name);
                        }
                    }
                }
            }

            _logger.LogInformation("Found {Count} Ollama models", models.Count);
            return models;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Ollama models from {BaseUrl}", _baseUrl);
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Validate provider configuration
    /// </summary>
    public override async Task<ProviderValidationResult> ValidateConfigurationAsync(CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        try
        {
            var isAvailable = await IsServiceAvailableAsync(cancellationToken).ConfigureAwait(false);
            if (!isAvailable)
            {
                errors.Add($"Cannot connect to Ollama at {_baseUrl}");
                errors.Add("Please ensure Ollama is running: 'ollama serve'");
                return new ProviderValidationResult
                {
                    IsValid = false,
                    Errors = errors,
                    Warnings = warnings
                };
            }

            var models = await GetAvailableModelsAsync(cancellationToken).ConfigureAwait(false);
            if (models.Count == 0)
            {
                warnings.Add("No models found. Please pull a model using: ollama pull llama3.1");
            }
            else if (!models.Contains(_model))
            {
                warnings.Add($"Configured model '{_model}' not found. Available models: {string.Join(", ", models)}");
            }

            return new ProviderValidationResult
            {
                IsValid = true,
                Errors = errors,
                Warnings = warnings
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating Ollama configuration");
            errors.Add($"Validation failed: {ex.Message}");
            return new ProviderValidationResult
            {
                IsValid = false,
                Errors = errors,
                Warnings = warnings
            };
        }
    }

    /// <summary>
    /// Get provider metadata
    /// </summary>
    public override ProviderMetadata GetProviderMetadata()
    {
        return new ProviderMetadata
        {
            Name = "Ollama",
            Tier = ProviderTier.Free,
            RequiresInternet = false,
            RequiresApiKey = false,
            Capabilities = new List<string> { "streaming", "local-execution", "offline" },
            DefaultModel = _model,
            EstimatedCostPer1KTokens = 0m
        };
    }

    /// <summary>
    /// Check if Ollama service is available
    /// </summary>
    public override async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        return await IsServiceAvailableAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Check if Ollama service is available at the configured base URL
    /// Results are cached for 30 seconds to avoid repeated checks
    /// </summary>
    private async Task<bool> IsServiceAvailableAsync(CancellationToken cancellationToken)
    {
        // Check if parent token is already cancelled before proceeding
        if (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Availability check cancelled before starting (parent token already cancelled)");
            return false;
        }

        // Check cache first to avoid repeated availability checks
        lock (_availabilityCacheLock)
        {
            var cacheAge = DateTime.UtcNow - _lastAvailabilityCheck;
            if (cacheAge < _availabilityCacheDuration)
            {
                _logger.LogDebug("Using cached Ollama availability result: {Result} (age: {Age}s)", 
                    _lastAvailabilityResult, cacheAge.TotalSeconds);
                return _lastAvailabilityResult;
            }
        }

        try
        {
            _logger.LogInformation("Checking Ollama service availability at {BaseUrl}", _baseUrl);

            // Create a new CancellationTokenSource that is NOT linked to the parent cancellationToken
            // This prevents premature cancellation if the parent token has a short timeout
            // The availability check needs its own independent timeout
            // However, we still monitor the parent token and exit early if it's cancelled
            using var cts = new CancellationTokenSource();
            // Increased timeout to 60 seconds to accommodate slow systems, model loading, and network latency
            // This is very lenient to ensure we don't prematurely fail on systems that are actually working
            cts.CancelAfter(TimeSpan.FromSeconds(60));

            // Try /api/version first (lightweight endpoint)
            try
            {
                // Check parent cancellation before making request
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogDebug("Availability check cancelled by parent token during /api/version attempt");
                    return false;
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/api/version");
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
                
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Ollama service detected at {BaseUrl} via /api/version", _baseUrl);
                    lock (_availabilityCacheLock)
                    {
                        _lastAvailabilityCheck = DateTime.UtcNow;
                        _lastAvailabilityResult = true;
                    }
                    return true;
                }
                _logger.LogWarning("Ollama /api/version endpoint returned status code {StatusCode}, trying /api/tags as fallback", response.StatusCode);
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogInformation("Ollama /api/version endpoint timed out after 60s, trying /api/tags as fallback. This is normal for slow systems. Inner exception: {InnerException}", ex.InnerException?.Message);
            }
            catch (HttpRequestException ex)
            {
                var innerException = ex.InnerException;
                var errorDetails = $"HttpRequestException: {ex.Message}";
                if (innerException != null)
                {
                    errorDetails += $", InnerException: {innerException.GetType().Name} - {innerException.Message}";
                }
                _logger.LogInformation("Ollama /api/version endpoint failed: {ErrorDetails}, trying /api/tags as fallback", errorDetails);
            }
            catch (Exception ex)
            {
                _logger.LogInformation("Ollama /api/version endpoint error: {ExceptionType} - {Message}, trying /api/tags as fallback", ex.GetType().Name, ex.Message);
            }

            // Fallback to /api/tags if version endpoint failed for any reason (timeout, error status, or exception)
            try
            {
                // Check parent cancellation before fallback attempt
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogDebug("Availability check cancelled by parent token before /api/tags fallback");
                    return false;
                }

                using var fallbackCts = new CancellationTokenSource();
                fallbackCts.CancelAfter(TimeSpan.FromSeconds(45)); // Lenient fallback
                using var fallbackRequest = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/api/tags");
                using var tagsResponse = await _httpClient.SendAsync(fallbackRequest, HttpCompletionOption.ResponseHeadersRead, fallbackCts.Token).ConfigureAwait(false);
                
                if (tagsResponse.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Ollama service detected at {BaseUrl} via /api/tags fallback", _baseUrl);
                    lock (_availabilityCacheLock)
                    {
                        _lastAvailabilityCheck = DateTime.UtcNow;
                        _lastAvailabilityResult = true;
                    }
                    return true;
                }
                _logger.LogWarning("Ollama /api/tags fallback endpoint returned status code {StatusCode}", tagsResponse.StatusCode);
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning("Ollama /api/tags fallback endpoint timed out after 45s. System may be very slow or Ollama may not be running. Inner exception: {InnerException}", ex.InnerException?.Message);
            }
            catch (HttpRequestException ex)
            {
                var innerException = ex.InnerException;
                var errorDetails = $"HttpRequestException: {ex.Message}";
                if (innerException != null)
                {
                    errorDetails += $", InnerException: {innerException.GetType().Name} - {innerException.Message}";
                    if (innerException.Message.Contains("No connection could be made") || 
                        innerException.Message.Contains("Connection refused") ||
                        innerException.Message.Contains("actively refused"))
                    {
                        _logger.LogWarning("Ollama connection refused at {BaseUrl}. Ensure Ollama is running: 'ollama serve'", _baseUrl);
                    }
                }
                _logger.LogWarning("Ollama /api/tags fallback endpoint failed: {ErrorDetails}", errorDetails);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ollama /api/tags fallback endpoint error: {ExceptionType} - {Message}", ex.GetType().Name, ex.Message);
            }

            _logger.LogWarning("Ollama service not available at {BaseUrl} after checking both /api/version and /api/tags", _baseUrl);
            lock (_availabilityCacheLock)
            {
                _lastAvailabilityCheck = DateTime.UtcNow;
                _lastAvailabilityResult = false;
            }
            return false;
        }
        catch (HttpRequestException ex)
        {
            var innerException = ex.InnerException;
            var errorDetails = $"HttpRequestException: {ex.Message}";
            if (innerException != null)
            {
                errorDetails += $", InnerException: {innerException.GetType().Name} - {innerException.Message}";
            }
            _logger.LogWarning("Ollama service not available at {BaseUrl}: {ErrorDetails}", _baseUrl, errorDetails);
            lock (_availabilityCacheLock)
            {
                _lastAvailabilityCheck = DateTime.UtcNow;
                _lastAvailabilityResult = false;
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking Ollama service availability at {BaseUrl}: {ExceptionType} - {Message}", _baseUrl, ex.GetType().Name, ex.Message);
            lock (_availabilityCacheLock)
            {
                _lastAvailabilityCheck = DateTime.UtcNow;
                _lastAvailabilityResult = false;
            }
            return false;
        }
    }

    /// <summary>
    /// Build prompt from request with optional RAG context
    /// </summary>
    private string BuildPrompt(ScriptGenerationRequest request, string ragContext = "")
    {
        var brief = request.Brief;
        var spec = request.PlanSpec;
        var promptBuilder = new StringBuilder();

        // Add RAG context first if available
        if (!string.IsNullOrEmpty(ragContext))
        {
            promptBuilder.AppendLine("# Reference Context");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine(ragContext);
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("---");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("Use the above reference context to inform your script generation.");
            if (brief.RagConfiguration?.IncludeCitations == true)
            {
                promptBuilder.AppendLine("Cite sources using [Citation X] format where appropriate.");
            }
            promptBuilder.AppendLine();
        }

        var systemPrompt = @"You are a professional scriptwriter creating engaging video scripts.
Follow these guidelines:
- Create clear, concise narration suitable for video
- Structure content into logical scenes
- Match the requested tone and style
- Consider the target audience
- Keep scenes focused and digestible";

        if (!string.IsNullOrEmpty(ragContext))
        {
            systemPrompt += @"
- Use information from the reference context when relevant
- Cite sources appropriately using [Citation X] format";
        }

        promptBuilder.AppendLine(systemPrompt);
        promptBuilder.AppendLine();

        var userPrompt = $@"Create a video script for the following:

Topic: {brief.Topic}
Audience: {brief.Audience}
Goal: {brief.Goal}
Tone: {brief.Tone}
Duration: {spec.TargetDuration.TotalSeconds} seconds
Style: {spec.Style}

Please provide a well-structured script with clear narration for each scene.";

        promptBuilder.AppendLine(userPrompt);

        return promptBuilder.ToString();
    }

    /// <summary>
    /// Retrieve RAG context for the given topic if RAG is enabled
    /// </summary>
    private async Task<string> RetrieveRagContextAsync(
        ScriptGenerationRequest request,
        CancellationToken cancellationToken)
    {
        if (_ragContextBuilder == null || request.Brief.RagConfiguration?.Enabled != true)
        {
            return string.Empty;
        }

        try
        {
            var ragConfig = new RagConfig
            {
                Enabled = true,
                TopK = request.Brief.RagConfiguration.TopK,
                MinimumScore = request.Brief.RagConfiguration.MinimumScore,
                MaxContextTokens = request.Brief.RagConfiguration.MaxContextTokens,
                IncludeCitations = request.Brief.RagConfiguration.IncludeCitations
            };

            var ragResult = await _ragContextBuilder.BuildContextAsync(
                request.Brief.Topic,
                ragConfig,
                cancellationToken).ConfigureAwait(false);

            if (ragResult.Chunks.Count > 0)
            {
                _logger.LogInformation("RAG context retrieved: {ChunkCount} chunks, {TokenCount} tokens",
                    ragResult.Chunks.Count, ragResult.TotalTokens);
                return ragResult.FormattedContext;
            }

            _logger.LogInformation("No relevant RAG context found for topic: {Topic}", request.Brief.Topic);
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve RAG context, continuing without it");
            return string.Empty;
        }
    }
}
