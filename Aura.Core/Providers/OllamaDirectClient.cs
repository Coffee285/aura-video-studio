using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aura.Core.Providers;

/// <summary>
/// Settings for Ollama direct client configuration.
/// </summary>
public class OllamaSettings
{
    /// <summary>Base URL for Ollama API (default: http://127.0.0.1:11434)</summary>
    public string BaseUrl { get; set; } = "http://127.0.0.1:11434";

    /// <summary>Default model to use if not specified</summary>
    public string? DefaultModel { get; set; }

    /// <summary>
    /// Timeout for requests (default: 15 minutes to match OllamaScriptProvider).
    /// This allows for slow local models, large models, and model loading.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Maximum retry attempts (default: 3)</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Enable GPU acceleration (default: true)</summary>
    public bool GpuEnabled { get; set; } = true;

    /// <summary>Number of GPUs to use (-1 = all, 0 = CPU only)</summary>
    public int NumGpu { get; set; } = -1;

    /// <summary>Context window size (default: 4096)</summary>
    public int NumCtx { get; set; } = 4096;

    /// <summary>
    /// Interval for heartbeat logging during long operations (default: 30 seconds).
    /// Helps monitor progress during 15-minute timeout windows.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Duration to cache availability check results (default: 30 seconds).
    /// Avoids repeated slow availability checks during active operations.
    /// </summary>
    public TimeSpan AvailabilityCacheDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Buffer time added to operation timeout when configuring HttpClient timeout (default: 5 minutes).
    /// This ensures HttpClient doesn't timeout before the operation timeout,
    /// allowing proper error handling and retry logic.
    /// </summary>
    public TimeSpan HttpClientTimeoutBuffer { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// Direct HTTP client for Ollama API with proper dependency injection.
///
/// ARCHITECTURAL FIX: This replaces reflection-based access to OllamaLlmProvider.
/// Uses IHttpClientFactory for proper lifetime management and configuration.
/// Implements retry logic with exponential backoff.
/// 
/// CRITICAL FIX (from PR analysis): Uses patterns from OllamaScriptProvider for reliability:
/// - 15-minute timeout (900s) for slow local models
/// - Independent CancellationTokenSource (not linked to parent)
/// - Heartbeat logging every 30 seconds
/// - Availability caching for 30 seconds
/// - Robust retry with exponential backoff
/// </summary>
public class OllamaDirectClient : IOllamaDirectClient
{
    private readonly ILogger<OllamaDirectClient> _logger;
    private readonly HttpClient _httpClient;
    private readonly OllamaSettings _settings;
    
    // Availability caching to avoid repeated slow checks
    private DateTime _lastAvailabilityCheck = DateTime.MinValue;
    private bool _lastAvailabilityResult = false;
    private readonly object _availabilityCacheLock = new object();

    public OllamaDirectClient(
        ILogger<OllamaDirectClient> logger,
        HttpClient httpClient,
        IOptions<OllamaSettings> settings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));

        // Configure HttpClient from settings
        _httpClient.BaseAddress = new Uri(_settings.BaseUrl);
        
        // CRITICAL FIX: Ensure HttpClient timeout is properly configured for Ollama's long-running requests
        // Add buffer to prevent HttpClient from timing out before our operation timeout
        // NOTE: Cannot use OllamaHttpClientHelper.EnsureProperTimeout() because it's in Aura.Providers
        // and Aura.Core doesn't reference Aura.Providers (would create circular dependency).
        // This inline implementation provides the same functionality.
        var requiredTimeout = _settings.Timeout.Add(_settings.HttpClientTimeoutBuffer);
        
        // If HttpClient timeout is insufficient, update it
        if (_httpClient.Timeout != Timeout.InfiniteTimeSpan && _httpClient.Timeout < requiredTimeout)
        {
            _logger.LogWarning(
                "HttpClient timeout ({HttpClientTimeout}s) is shorter than required ({RequiredTimeout}s). " +
                "Adjusting timeout to prevent premature cancellation.",
                _httpClient.Timeout.TotalSeconds, requiredTimeout.TotalSeconds);
            
            try
            {
                _httpClient.Timeout = requiredTimeout;
                _logger.LogInformation(
                    "Configured HttpClient timeout to {Timeout}s for Ollama direct client",
                    requiredTimeout.TotalSeconds);
            }
            catch (InvalidOperationException)
            {
                // HttpClient is already in use - log warning but continue
                // The timeout will be managed by our independent CancellationTokenSource
                _logger.LogWarning(
                    "HttpClient already in use, cannot adjust timeout. " +
                    "Using independent CancellationTokenSource for timeout management.");
            }
        }
    }

    /// <inheritdoc />
    public async Task<string> GenerateAsync(
        string model,
        string prompt,
        string? systemPrompt = null,
        OllamaGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(prompt);

        var requestBody = new OllamaGenerateRequest
        {
            Model = model,
            Prompt = prompt,
            System = systemPrompt,
            Stream = false, // Non-streaming for simplicity
            Options = options != null ? new OllamaRequestOptions
            {
                Temperature = options.Temperature,
                TopP = options.TopP,
                TopK = options.TopK,
                NumPredict = options.MaxTokens,
                RepeatPenalty = options.RepeatPenalty,
                Stop = options.Stop,
                NumGpu = options.NumGpu ?? _settings.NumGpu,
                NumCtx = options.NumCtx ?? _settings.NumCtx
            } : null
        };

        _logger.LogInformation(
            "Calling Ollama API: model={Model}, promptLength={PromptLength}, timeout={Timeout:F1} minutes",
            model, prompt.Length, _settings.Timeout.TotalMinutes);

        // CRITICAL FIX: Use independent timeout - don't link to parent token for timeout management
        // This prevents upstream components (frontend, API middleware) from cancelling our long-running operation
        // if they have shorter timeouts. The linked token approach would cancel if ANY upstream has a short timeout.
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(_settings.Timeout); // 15 minutes - allows for slow local models, large models, and model loading

        // Still respect explicit user cancellation by checking the parent token
        if (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("Operation was cancelled by user", cancellationToken);
        }

        // Retry with exponential backoff
        var attempt = 0;
        var maxAttempts = _settings.MaxRetries;
        Exception? lastException = null;
        
        // Track start time for the entire request (not just individual attempts)
        var requestStartTime = DateTime.UtcNow;

        while (attempt < maxAttempts)
        {
            attempt++;

            try
            {
                if (attempt > 1)
                {
                    // Match OllamaScriptProvider pattern: Math.Pow(2, attempt - 1) for delays of 2s, 4s, 8s
                    // attempt=2 -> 2^1=2s, attempt=3 -> 2^2=4s, attempt=4 -> 2^3=8s
                    var backoffDelay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                    _logger.LogInformation(
                        "Retrying Ollama request (attempt {Attempt}/{MaxAttempts}) after {Delay}s delay",
                        attempt, maxAttempts, backoffDelay.TotalSeconds);
                    // Use cts.Token to ensure retries complete within the 15-minute window
                    // Using parent cancellationToken would allow upstream timeouts to cancel retries
                    await Task.Delay(backoffDelay, cts.Token).ConfigureAwait(false);
                }

                // Track start time for this individual attempt (for duration logging)
                var attemptStartTime = DateTime.UtcNow;

                _logger.LogInformation(
                    "Sending request to Ollama (attempt {Attempt}/{MaxAttempts}, timeout: {Timeout:F1} minutes)",
                    attempt, maxAttempts, _settings.Timeout.TotalMinutes);

                // CRITICAL FIX: Start periodic heartbeat logging to show the system is still working
                // During a 15-minute wait, there's no visibility that the system is working without this
                using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                var heartbeatTask = Task.Run(async () =>
                {
                    try
                    {
                        while (!heartbeatCts.Token.IsCancellationRequested)
                        {
                            await Task.Delay(_settings.HeartbeatInterval, heartbeatCts.Token).ConfigureAwait(false);
                            // Calculate elapsed time from start of entire request (not just current attempt)
                            var totalElapsed = DateTime.UtcNow - requestStartTime;
                            var remaining = _settings.Timeout.TotalSeconds - totalElapsed.TotalSeconds;
                            if (remaining > 0)
                            {
                                _logger.LogInformation(
                                    "Still awaiting Ollama response... ({Elapsed:F0}s elapsed, {Remaining:F0}s remaining before timeout)",
                                    totalElapsed.TotalSeconds,
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
                    response = await _httpClient.PostAsJsonAsync(
                        "/api/generate",
                        requestBody,
                        cts.Token).ConfigureAwait(false);
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
                    throw new OperationCanceledException("Operation was cancelled by user", cancellationToken);
                }

                response.EnsureSuccessStatusCode();

                // Use cts.Token for JSON deserialization to maintain independent timeout strategy
                // Using parent cancellationToken would allow upstream components to cancel during parsing
                var result = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(
                    cancellationToken: cts.Token).ConfigureAwait(false);

                if (result == null || string.IsNullOrEmpty(result.Response))
                {
                    throw new InvalidOperationException("Ollama returned empty response");
                }

                var attemptDuration = DateTime.UtcNow - attemptStartTime;
                _logger.LogInformation(
                    "Ollama generation completed: model={Model}, duration={Duration}s, responseLength={Length} (attempt {Attempt})",
                    model, attemptDuration.TotalSeconds, result.Response.Length, attempt);

                return result.Response;
            }
            catch (TaskCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
            {
                lastException = ex;
                // Calculate total elapsed time from the start of all attempts (not just this attempt)
                var totalElapsed = DateTime.UtcNow - requestStartTime;
                _logger.LogWarning(ex,
                    "Ollama request timed out after {Elapsed:F1}s total (attempt {Attempt}/{MaxAttempts}, configured timeout: {Timeout:F1}s). " +
                    "This may be normal for slow models or when Ollama is loading the model. Will retry if attempts remain.",
                    totalElapsed.TotalSeconds, attempt, maxAttempts, _settings.Timeout.TotalSeconds);

                if (attempt >= maxAttempts)
                {
                    throw new InvalidOperationException(
                        $"Ollama request timed out after {_settings.Timeout.TotalSeconds}s ({_settings.Timeout.TotalMinutes:F1} minutes). " +
                        $"This can happen with large models or slow systems. The model '{model}' may be:\n" +
                        $"  - Still loading into memory (first request after Ollama start can take 2-5 minutes)\n" +
                        $"  - Generating on a slow CPU (some systems need 10-15 minutes)\n" +
                        $"  - A very large model (70B+ models can be extremely slow)\n" +
                        $"Suggestions:\n" +
                        $"  - Wait for Ollama to fully load the model (check 'ollama ps' in terminal)\n" +
                        $"  - Use a smaller/faster model (e.g., llama3.2:3b instead of llama3.1:8b)\n" +
                        $"  - Ensure Ollama has sufficient RAM (model size + 2GB minimum)\n" +
                        $"  - Check Ollama logs for errors: 'ollama logs'", ex);
                }
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
                _logger.LogWarning(ex,
                    "Failed to connect to Ollama at {BaseUrl} (attempt {Attempt}/{MaxAttempts})",
                    _settings.BaseUrl, attempt, maxAttempts);

                if (attempt >= maxAttempts)
                {
                    throw new InvalidOperationException(
                        $"Cannot connect to Ollama at {_settings.BaseUrl}. Please ensure Ollama is running: 'ollama serve'", ex);
                }
            }
            catch (Exception ex) when (attempt < maxAttempts && !cancellationToken.IsCancellationRequested)
            {
                lastException = ex;
                _logger.LogWarning(ex,
                    "Error calling Ollama (attempt {Attempt}/{MaxAttempts}): {Message}",
                    attempt, maxAttempts, ex.Message);
            }
        }

        throw new InvalidOperationException(
            $"Ollama request failed after {maxAttempts} attempts. Last error: {lastException?.Message ?? "Unknown error"}. " +
            $"Please verify Ollama is running and model '{model}' is available.", lastException);
    }

    /// <inheritdoc />
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        // CRITICAL FIX: Cache availability check results for 30 seconds
        // Avoids repeated slow checks during active operations (like script generation)
        lock (_availabilityCacheLock)
        {
            var cacheAge = DateTime.UtcNow - _lastAvailabilityCheck;
            if (cacheAge < _settings.AvailabilityCacheDuration)
            {
                _logger.LogDebug(
                    "Returning cached Ollama availability result: {Available} (cache age: {Age}s)",
                    _lastAvailabilityResult, cacheAge.TotalSeconds);
                return _lastAvailabilityResult;
            }
        }

        try
        {
            _logger.LogDebug("Performing fresh Ollama availability check");
            var response = await _httpClient.GetAsync("/api/version", cancellationToken).ConfigureAwait(false);
            var isAvailable = response.IsSuccessStatusCode;

            lock (_availabilityCacheLock)
            {
                _lastAvailabilityCheck = DateTime.UtcNow;
                _lastAvailabilityResult = isAvailable;
            }

            _logger.LogInformation("Ollama availability check result: {Available}", isAvailable);
            return isAvailable;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Ollama availability check failed");
            
            lock (_availabilityCacheLock)
            {
                _lastAvailabilityCheck = DateTime.UtcNow;
                _lastAvailabilityResult = false;
            }
            
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/tags", cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OllamaModelsResponse>(
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return result?.Models?.Select(m => m.Name).ToList() ?? new List<string>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list Ollama models");
            return new List<string>();
        }
    }

    #region DTOs for Ollama API

    private class OllamaGenerateRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = string.Empty;

        [JsonPropertyName("system")]
        public string? System { get; set; }

        [JsonPropertyName("stream")]
        public bool Stream { get; set; } = false;

        [JsonPropertyName("options")]
        public OllamaRequestOptions? Options { get; set; }
    }

    private class OllamaRequestOptions
    {
        [JsonPropertyName("temperature")]
        public double? Temperature { get; set; }

        [JsonPropertyName("top_p")]
        public double? TopP { get; set; }

        [JsonPropertyName("top_k")]
        public int? TopK { get; set; }

        [JsonPropertyName("num_predict")]
        public int? NumPredict { get; set; }

        [JsonPropertyName("repeat_penalty")]
        public int? RepeatPenalty { get; set; }

        [JsonPropertyName("stop")]
        public List<string>? Stop { get; set; }

        [JsonPropertyName("num_gpu")]
        public int? NumGpu { get; set; }

        [JsonPropertyName("num_ctx")]
        public int? NumCtx { get; set; }
    }

    private class OllamaGenerateResponse
    {
        [JsonPropertyName("response")]
        public string Response { get; set; } = string.Empty;

        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("done")]
        public bool Done { get; set; }
    }

    private class OllamaModelsResponse
    {
        [JsonPropertyName("models")]
        public List<OllamaModelInfo>? Models { get; set; }
    }

    private class OllamaModelInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    #endregion
}
