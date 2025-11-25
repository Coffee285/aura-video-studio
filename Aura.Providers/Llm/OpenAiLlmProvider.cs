using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aura.Core.AI;
using Aura.Core.AI.SchemaBuilders;
using Aura.Core.Models;
using Aura.Core.Models.Narrative;
using Aura.Core.Models.OpenAI;
using Aura.Core.Models.Streaming;
using Aura.Core.Models.Visual;
using Aura.Core.Providers;
using Aura.Core.Services.AI;
using Microsoft.Extensions.Logging;

namespace Aura.Providers.Llm;

/// <summary>
/// LLM provider that uses OpenAI API for script generation (Pro feature).
/// Supports optional ML-driven enhancements via callbacks and prompt customization.
/// </summary>
public class OpenAiLlmProvider : ILlmProvider
{
    private readonly ILogger<OpenAiLlmProvider> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly int _maxRetries;
    private readonly TimeSpan _timeout;
    private readonly PromptCustomizationService _promptCustomizationService;

    /// <summary>
    /// Optional callback to enhance prompts before generation
    /// </summary>
    public Func<string, Brief, PlanSpec, Task<string>>? PromptEnhancementCallback { get; set; }

    /// <summary>
    /// Optional callback to track generation performance
    /// </summary>
    public Action<double, TimeSpan, bool>? PerformanceTrackingCallback { get; set; }

    public OpenAiLlmProvider(
        ILogger<OpenAiLlmProvider> logger,
        HttpClient httpClient,
        string apiKey,
        string model = "gpt-4o-mini",
        int maxRetries = 2,
        int timeoutSeconds = 120,
        PromptCustomizationService? promptCustomizationService = null)
    {
        _logger = logger;
        _httpClient = httpClient;
        _apiKey = apiKey;
        _model = model;
        _maxRetries = maxRetries;
        _timeout = TimeSpan.FromSeconds(timeoutSeconds);
        
        // Create PromptCustomizationService if not provided (using logger factory pattern)
        if (promptCustomizationService == null)
        {
            var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
            var customizationLogger = loggerFactory.CreateLogger<PromptCustomizationService>();
            _promptCustomizationService = new PromptCustomizationService(customizationLogger);
        }
        else
        {
            _promptCustomizationService = promptCustomizationService;
        }

        ValidateApiKey();
    }

    /// <summary>
    /// Validate the API key format and presence
    /// </summary>
    private void ValidateApiKey()
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new ArgumentException(
                "OpenAI API key is not configured. Please add your API key in Settings → Providers → OpenAI",
                nameof(_apiKey));
        }

        // Allow test/mock keys for testing
        if (_apiKey.StartsWith("test-", StringComparison.OrdinalIgnoreCase) ||
            _apiKey.StartsWith("mock-", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // OpenAI API keys should start with "sk-" and be reasonably long
        if (!_apiKey.StartsWith("sk-", StringComparison.Ordinal) || _apiKey.Length < 40)
        {
            throw new ArgumentException(
                "OpenAI API key format appears invalid. Please check your API key in Settings → Providers → OpenAI",
                nameof(_apiKey));
        }
    }

    public async Task<string> DraftScriptAsync(Brief brief, PlanSpec spec, CancellationToken ct)
    {
        // Use model override from LlmParameters if provided, otherwise use default model
        var modelToUse = !string.IsNullOrWhiteSpace(brief.LlmParameters?.ModelOverride) 
            ? brief.LlmParameters.ModelOverride 
            : _model;
        _logger.LogInformation("Generating high-quality script with OpenAI (model: {Model}) for topic: {Topic}", modelToUse, brief.Topic);

        var startTime = DateTime.UtcNow;
        Exception? lastException = null;
        
        for (int attempt = 0; attempt <= _maxRetries; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    var backoffDelay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    _logger.LogInformation("Retry attempt {Attempt}/{MaxRetries} after {Delay}s delay", 
                        attempt, _maxRetries, backoffDelay.TotalSeconds);
                    await Task.Delay(backoffDelay, ct).ConfigureAwait(false);
                }

                // Build enhanced prompts for quality content with user customizations
                string systemPrompt = EnhancedPromptTemplates.GetSystemPromptForScriptGeneration();
                string userPrompt = await _promptCustomizationService.BuildCustomizedPromptAsync(brief, spec, brief.PromptModifiers, ct).ConfigureAwait(false);

                // Apply enhancement callback if configured
                if (PromptEnhancementCallback != null)
                {
                    userPrompt = await PromptEnhancementCallback(userPrompt, brief, spec).ConfigureAwait(false);
                }

                // Get LLM parameters from brief, with defaults
                var llmParams = brief.LlmParameters;
                var temperature = llmParams?.Temperature ?? 0.7;
                var maxTokens = llmParams?.MaxTokens ?? 2048;
                var topP = llmParams?.TopP;
                var frequencyPenalty = llmParams?.FrequencyPenalty;
                var presencePenalty = llmParams?.PresencePenalty;
                var stopSequences = llmParams?.StopSequences;

                // Call OpenAI API with proper format
                var requestBody = new Dictionary<string, object>
                {
                    { "model", modelToUse },
                    { "messages", new[]
                        {
                            new { role = "system", content = systemPrompt },
                            new { role = "user", content = userPrompt }
                        }
                    },
                    { "temperature", temperature },
                    { "max_tokens", maxTokens }
                };

                // Add optional parameters only if provided
                if (topP.HasValue)
                {
                    requestBody["top_p"] = topP.Value;
                }
                if (frequencyPenalty.HasValue)
                {
                    requestBody["frequency_penalty"] = frequencyPenalty.Value;
                }
                if (presencePenalty.HasValue)
                {
                    requestBody["presence_penalty"] = presencePenalty.Value;
                }
                if (stopSequences != null && stopSequences.Count > 0)
                {
                    requestBody["stop"] = stopSequences;
                }

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(_timeout);

                var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content, cts.Token).ConfigureAwait(false);
                
                // Handle specific HTTP error codes
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        throw new InvalidOperationException(
                            "OpenAI API key is invalid or has been revoked. Please check your API key in Settings → Providers → OpenAI");
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        _logger.LogWarning("OpenAI rate limit exceeded (attempt {Attempt}/{MaxRetries})", attempt + 1, _maxRetries + 1);
                        if (attempt >= _maxRetries)
                        {
                            throw new InvalidOperationException(
                                "OpenAI rate limit exceeded. Please wait a moment and try again, or upgrade your OpenAI plan.");
                        }
                        lastException = new Exception($"Rate limit exceeded: {errorContent}");
                        continue; // Retry
                    }
                    else if ((int)response.StatusCode >= 500)
                    {
                        _logger.LogWarning("OpenAI server error (attempt {Attempt}/{MaxRetries}): {StatusCode}", 
                            attempt + 1, _maxRetries + 1, response.StatusCode);
                        if (attempt >= _maxRetries)
                        {
                            throw new InvalidOperationException(
                                $"OpenAI service is experiencing issues (HTTP {response.StatusCode}). Please try again later.");
                        }
                        lastException = new Exception($"Server error: {errorContent}");
                        continue; // Retry
                    }
                    
                    response.EnsureSuccessStatusCode();
                }

                var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                
                // Parse and validate response structure
                JsonDocument? responseDoc = null;
                try
                {
                    responseDoc = JsonDocument.Parse(responseJson);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Failed to parse OpenAI JSON response: {Response}", responseJson.Substring(0, Math.Min(500, responseJson.Length)));
                    throw new InvalidOperationException("OpenAI returned invalid JSON response", ex);
                }

                // Check for API errors in response
                if (responseDoc.RootElement.TryGetProperty("error", out var errorElement))
                {
                    var errorMessage = errorElement.TryGetProperty("message", out var msg) 
                        ? msg.GetString() ?? "Unknown error" 
                        : "API error";
                    var errorType = errorElement.TryGetProperty("type", out var type) 
                        ? type.GetString() ?? "unknown" 
                        : "unknown";
                    _logger.LogError("OpenAI API error: {Type} - {Message}", errorType, errorMessage);
                    throw new InvalidOperationException($"OpenAI API error: {errorMessage}");
                }

                if (responseDoc.RootElement.TryGetProperty("choices", out var choices) &&
                    choices.GetArrayLength() > 0)
                {
                    var firstChoice = choices[0];
                    if (firstChoice.TryGetProperty("message", out var message) &&
                        message.TryGetProperty("content", out var contentProp))
                    {
                        string script = contentProp.GetString() ?? string.Empty;
                        
                        if (string.IsNullOrWhiteSpace(script))
                        {
                            throw new InvalidOperationException("OpenAI returned an empty response");
                        }
                        
                        var duration = DateTime.UtcNow - startTime;
                        
                        _logger.LogInformation("Script generated successfully ({Length} characters) in {Duration}s", 
                            script.Length, duration.TotalSeconds);
                        
                        // Track performance if callback configured
                        PerformanceTrackingCallback?.Invoke(80.0, duration, true);
                        
                        return script;
                    }
                }

                _logger.LogWarning("OpenAI response did not contain expected structure. Response: {Response}", 
                    responseJson.Substring(0, Math.Min(500, responseJson.Length)));
                var failureDuration = DateTime.UtcNow - startTime;
                PerformanceTrackingCallback?.Invoke(0, failureDuration, false);
                throw new InvalidOperationException($"Invalid response structure from OpenAI API. Expected 'choices[0].message.content' but got: {responseJson.Substring(0, Math.Min(200, responseJson.Length))}");
            }
            catch (TaskCanceledException ex)
            {
                lastException = ex;
                _logger.LogWarning(ex, "OpenAI request timed out (attempt {Attempt}/{MaxRetries})", attempt + 1, _maxRetries + 1);
                if (attempt >= _maxRetries)
                {
                    var duration = DateTime.UtcNow - startTime;
                    PerformanceTrackingCallback?.Invoke(0, duration, false);
                    throw new InvalidOperationException(
                        $"OpenAI request timed out after {_timeout.TotalSeconds}s. Please check your internet connection or try again later.", ex);
                }
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Failed to connect to OpenAI API (attempt {Attempt}/{MaxRetries})", attempt + 1, _maxRetries + 1);
                if (attempt >= _maxRetries)
                {
                    var duration = DateTime.UtcNow - startTime;
                    PerformanceTrackingCallback?.Invoke(0, duration, false);
                    throw new InvalidOperationException(
                        "Cannot connect to OpenAI API. Please check your internet connection and try again.", ex);
                }
            }
            catch (InvalidOperationException)
            {
                // Don't retry on validation errors or known issues
                var duration = DateTime.UtcNow - startTime;
                PerformanceTrackingCallback?.Invoke(0, duration, false);
                throw;
            }
            catch (Exception ex) when (attempt < _maxRetries)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Error generating script with OpenAI (attempt {Attempt}/{MaxRetries})", attempt + 1, _maxRetries + 1);
            }
            catch (Exception ex)
            {
                var duration = DateTime.UtcNow - startTime;
                PerformanceTrackingCallback?.Invoke(0, duration, false);
                _logger.LogError(ex, "Error generating script with OpenAI after all retries");
                throw;
            }
        }

        // Should not reach here, but just in case
        var finalDuration = DateTime.UtcNow - startTime;
        PerformanceTrackingCallback?.Invoke(0, finalDuration, false);
        throw new InvalidOperationException(
            $"Failed to generate script with OpenAI after {_maxRetries + 1} attempts. Please try again later.", lastException);
    }

    /// <summary>
    /// Generates a script using OpenAI structured outputs with JSON schema validation
    /// Ensures reliable JSON structure by enforcing schema at the API level
    /// </summary>
    public async Task<string> GenerateScriptWithSchemaAsync(Brief brief, PlanSpec spec, CancellationToken ct)
    {
        _logger.LogInformation("Generating high-quality script with OpenAI structured outputs (model: {Model}) for topic: {Topic}", _model, brief.Topic);

        var startTime = DateTime.UtcNow;
        Exception? lastException = null;
        
        for (int attempt = 0; attempt <= _maxRetries; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    var backoffDelay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    _logger.LogInformation("Retry attempt {Attempt}/{MaxRetries} after {Delay}s delay", 
                        attempt, _maxRetries, backoffDelay.TotalSeconds);
                    await Task.Delay(backoffDelay, ct).ConfigureAwait(false);
                }

                // Build enhanced prompts for quality content with user customizations
                string systemPrompt = EnhancedPromptTemplates.GetSystemPromptForScriptGeneration();
                string userPrompt = await _promptCustomizationService.BuildCustomizedPromptAsync(brief, spec, brief.PromptModifiers, ct).ConfigureAwait(false);

                // Apply enhancement callback if configured
                if (PromptEnhancementCallback != null)
                {
                    userPrompt = await PromptEnhancementCallback(userPrompt, brief, spec).ConfigureAwait(false);
                }

                // Get the script schema for structured output
                var responseFormat = ScriptSchemaBuilder.GetScriptSchema();

                // Call OpenAI API with structured output
                var requestBody = new
                {
                    model = _model,
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = userPrompt }
                    },
                    temperature = 0.7,
                    max_tokens = 2048,
                    response_format = responseFormat
                };

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

                var json = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions 
                { 
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                });
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(_timeout);

                var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content, cts.Token).ConfigureAwait(false);
                
                // Handle specific HTTP error codes
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        throw new InvalidOperationException(
                            "OpenAI API key is invalid or has been revoked. Please check your API key in Settings → Providers → OpenAI");
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        _logger.LogWarning("OpenAI rate limit exceeded (attempt {Attempt}/{MaxRetries})", attempt + 1, _maxRetries + 1);
                        if (attempt >= _maxRetries)
                        {
                            throw new InvalidOperationException(
                                "OpenAI rate limit exceeded. Please wait a moment and try again, or upgrade your OpenAI plan.");
                        }
                        lastException = new Exception($"Rate limit exceeded: {errorContent}");
                        continue; // Retry
                    }
                    else if ((int)response.StatusCode >= 500)
                    {
                        _logger.LogWarning("OpenAI server error (attempt {Attempt}/{MaxRetries}): {StatusCode}", 
                            attempt + 1, _maxRetries + 1, response.StatusCode);
                        if (attempt >= _maxRetries)
                        {
                            throw new InvalidOperationException(
                                $"OpenAI service is experiencing issues (HTTP {response.StatusCode}). Please try again later.");
                        }
                        lastException = new Exception($"Server error: {errorContent}");
                        continue; // Retry
                    }
                    
                    response.EnsureSuccessStatusCode();
                }

                var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var responseDoc = JsonDocument.Parse(responseJson);

                if (responseDoc.RootElement.TryGetProperty("choices", out var choices) &&
                    choices.GetArrayLength() > 0)
                {
                    var firstChoice = choices[0];
                    if (firstChoice.TryGetProperty("message", out var message) &&
                        message.TryGetProperty("content", out var contentProp))
                    {
                        string script = contentProp.GetString() ?? string.Empty;
                        
                        if (string.IsNullOrWhiteSpace(script))
                        {
                            throw new InvalidOperationException("OpenAI returned an empty response");
                        }
                        
                        var duration = DateTime.UtcNow - startTime;
                        
                        _logger.LogInformation("Script generated successfully with structured output ({Length} characters) in {Duration}s", 
                            script.Length, duration.TotalSeconds);
                        
                        // Track performance if callback configured
                        PerformanceTrackingCallback?.Invoke(80.0, duration, true);
                        
                        return script;
                    }
                }

                _logger.LogWarning("OpenAI response did not contain expected structure");
                var failureDuration = DateTime.UtcNow - startTime;
                PerformanceTrackingCallback?.Invoke(0, failureDuration, false);
                throw new InvalidOperationException("Invalid response structure from OpenAI API");
            }
            catch (TaskCanceledException ex)
            {
                lastException = ex;
                _logger.LogWarning(ex, "OpenAI request timed out (attempt {Attempt}/{MaxRetries})", attempt + 1, _maxRetries + 1);
                if (attempt >= _maxRetries)
                {
                    var duration = DateTime.UtcNow - startTime;
                    PerformanceTrackingCallback?.Invoke(0, duration, false);
                    throw new InvalidOperationException(
                        $"OpenAI request timed out after {_timeout.TotalSeconds}s. Please check your internet connection or try again later.", ex);
                }
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Failed to connect to OpenAI API (attempt {Attempt}/{MaxRetries})", attempt + 1, _maxRetries + 1);
                if (attempt >= _maxRetries)
                {
                    var duration = DateTime.UtcNow - startTime;
                    PerformanceTrackingCallback?.Invoke(0, duration, false);
                    throw new InvalidOperationException(
                        "Cannot connect to OpenAI API. Please check your internet connection and try again.", ex);
                }
            }
            catch (InvalidOperationException)
            {
                // Don't retry on validation errors or known issues
                var duration = DateTime.UtcNow - startTime;
                PerformanceTrackingCallback?.Invoke(0, duration, false);
                throw;
            }
            catch (Exception ex) when (attempt < _maxRetries)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Error generating script with OpenAI structured output (attempt {Attempt}/{MaxRetries})", attempt + 1, _maxRetries + 1);
            }
            catch (Exception ex)
            {
                var duration = DateTime.UtcNow - startTime;
                PerformanceTrackingCallback?.Invoke(0, duration, false);
                _logger.LogError(ex, "Error generating script with OpenAI structured output after all retries");
                throw;
            }
        }

        // Should not reach here, but just in case
        var finalDuration = DateTime.UtcNow - startTime;
        PerformanceTrackingCallback?.Invoke(0, finalDuration, false);
        throw new InvalidOperationException(
            $"Failed to generate script with OpenAI structured output after {_maxRetries + 1} attempts. Please try again later.", lastException);
    }

    public async Task<string> CompleteAsync(string prompt, CancellationToken ct)
    {
        _logger.LogInformation("Executing raw prompt completion with OpenAI (model: {Model})", _model);

        var startTime = DateTime.UtcNow;
        Exception? lastException = null;
        
        for (int attempt = 0; attempt <= _maxRetries; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    var backoffDelay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    _logger.LogInformation("Retry attempt {Attempt}/{MaxRetries} after {Delay}s delay", 
                        attempt, _maxRetries, backoffDelay.TotalSeconds);
                    await Task.Delay(backoffDelay, ct).ConfigureAwait(false);
                }

                // Use prompt as user message with a generic system prompt for structured output
                var systemPrompt = "You are a helpful assistant that generates structured JSON responses. Follow the instructions precisely and return only valid JSON.";
                
                var requestBody = new
                {
                    model = _model,
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = prompt }
                    },
                    temperature = 0.7,
                    max_tokens = 2048
                };

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(_timeout);

                var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content, cts.Token).ConfigureAwait(false);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        throw new InvalidOperationException(
                            "OpenAI API key is invalid or has been revoked. Please check your API key in Settings → Providers → OpenAI");
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        _logger.LogWarning("OpenAI rate limit exceeded (attempt {Attempt}/{MaxRetries})", attempt + 1, _maxRetries + 1);
                        if (attempt >= _maxRetries)
                        {
                            throw new InvalidOperationException(
                                "OpenAI rate limit exceeded. Please wait a moment and try again, or upgrade your OpenAI plan.");
                        }
                        lastException = new Exception($"Rate limit exceeded: {errorContent}");
                        continue;
                    }
                    else if ((int)response.StatusCode >= 500)
                    {
                        _logger.LogWarning("OpenAI server error (attempt {Attempt}/{MaxRetries}): {StatusCode}", 
                            attempt + 1, _maxRetries + 1, response.StatusCode);
                        if (attempt >= _maxRetries)
                        {
                            throw new InvalidOperationException(
                                $"OpenAI service is experiencing issues (HTTP {response.StatusCode}). Please try again later.");
                        }
                        lastException = new Exception($"Server error: {errorContent}");
                        continue;
                    }
                    
                    response.EnsureSuccessStatusCode();
                }

                var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var responseDoc = JsonDocument.Parse(responseJson);

                if (responseDoc.RootElement.TryGetProperty("choices", out var choices) &&
                    choices.GetArrayLength() > 0)
                {
                    var firstChoice = choices[0];
                    if (firstChoice.TryGetProperty("message", out var message) &&
                        message.TryGetProperty("content", out var contentProp))
                    {
                        string result = contentProp.GetString() ?? string.Empty;
                        
                        if (string.IsNullOrWhiteSpace(result))
                        {
                            throw new InvalidOperationException("OpenAI returned an empty response");
                        }
                        
                        var duration = DateTime.UtcNow - startTime;
                        _logger.LogInformation("Completion generated successfully ({Length} characters) in {Duration}s", 
                            result.Length, duration.TotalSeconds);
                        
                        return result;
                    }
                }

                _logger.LogWarning("OpenAI response did not contain expected structure");
                throw new InvalidOperationException("Invalid response structure from OpenAI API");
            }
            catch (TaskCanceledException ex)
            {
                lastException = ex;
                _logger.LogWarning(ex, "OpenAI request timed out (attempt {Attempt}/{MaxRetries})", attempt + 1, _maxRetries + 1);
                if (attempt >= _maxRetries)
                {
                    throw new InvalidOperationException(
                        $"OpenAI request timed out after {_timeout.TotalSeconds}s. Please check your internet connection or try again later.", ex);
                }
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Failed to connect to OpenAI API (attempt {Attempt}/{MaxRetries})", attempt + 1, _maxRetries + 1);
                if (attempt >= _maxRetries)
                {
                    throw new InvalidOperationException(
                        "Cannot connect to OpenAI API. Please check your internet connection and try again.", ex);
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < _maxRetries)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Error completing prompt with OpenAI (attempt {Attempt}/{MaxRetries})", attempt + 1, _maxRetries + 1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing prompt with OpenAI after all retries");
                throw;
            }
        }

        throw new InvalidOperationException(
            $"Failed to complete prompt with OpenAI after {_maxRetries + 1} attempts. Please try again later.", lastException);
    }

    /// <summary>
    /// Generate a chat completion response for ideation, brainstorming, and other conversational tasks.
    /// Uses OpenAI's chat API with system/user message pattern for reliable JSON output.
    /// </summary>
    public async Task<string> GenerateChatCompletionAsync(
        string systemPrompt,
        string userPrompt,
        LlmParameters? parameters = null,
        CancellationToken ct = default)
    {
        var modelToUse = !string.IsNullOrWhiteSpace(parameters?.ModelOverride)
            ? parameters.ModelOverride
            : _model;

        _logger.LogInformation(
            "Generating chat completion with OpenAI (model: {Model})",
            modelToUse);

        var startTime = DateTime.UtcNow;
        Exception? lastException = null;

        for (int attempt = 0; attempt <= _maxRetries; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    var backoffDelay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    _logger.LogInformation("Retrying chat completion (attempt {Attempt}/{MaxRetries}) after {Delay}s",
                        attempt + 1, _maxRetries + 1, backoffDelay.TotalSeconds);
                    await Task.Delay(backoffDelay, ct).ConfigureAwait(false);
                }

                // Build request with LLM parameters
                var temperature = parameters?.Temperature ?? 0.7;
                var maxTokens = parameters?.MaxTokens ?? 2048;
                var topP = parameters?.TopP;
                var frequencyPenalty = parameters?.FrequencyPenalty;
                var presencePenalty = parameters?.PresencePenalty;

                var requestBody = new Dictionary<string, object>
                {
                    { "model", modelToUse },
                    { "messages", new[]
                        {
                            new { role = "system", content = systemPrompt },
                            new { role = "user", content = userPrompt }
                        }
                    },
                    { "temperature", temperature },
                    { "max_tokens", maxTokens },
                    // Request JSON output format for reliable structured responses
                    { "response_format", new { type = "json_object" } }
                };

                // Add optional parameters only if provided
                if (topP.HasValue)
                {
                    requestBody["top_p"] = topP.Value;
                }
                if (frequencyPenalty.HasValue)
                {
                    requestBody["frequency_penalty"] = frequencyPenalty.Value;
                }
                if (presencePenalty.HasValue)
                {
                    requestBody["presence_penalty"] = presencePenalty.Value;
                }

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(_timeout);

                var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content, cts.Token).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        throw new InvalidOperationException(
                            "OpenAI API key is invalid or has been revoked. Please check your API key in Settings → Providers → OpenAI");
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        _logger.LogWarning("OpenAI rate limit exceeded (attempt {Attempt}/{MaxRetries})", attempt + 1, _maxRetries + 1);
                        if (attempt >= _maxRetries)
                        {
                            throw new InvalidOperationException(
                                "OpenAI rate limit exceeded. Please wait a moment and try again, or upgrade your OpenAI plan.");
                        }
                        lastException = new Exception($"Rate limit exceeded: {errorContent}");
                        continue;
                    }
                    else if ((int)response.StatusCode >= 500)
                    {
                        _logger.LogWarning("OpenAI server error (attempt {Attempt}/{MaxRetries}): {StatusCode}",
                            attempt + 1, _maxRetries + 1, response.StatusCode);
                        if (attempt >= _maxRetries)
                        {
                            throw new InvalidOperationException(
                                $"OpenAI service is experiencing issues (HTTP {response.StatusCode}). Please try again later.");
                        }
                        lastException = new Exception($"Server error: {errorContent}");
                        continue;
                    }

                    response.EnsureSuccessStatusCode();
                }

                var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var responseDoc = JsonDocument.Parse(responseJson);

                // Check for API errors in response
                if (responseDoc.RootElement.TryGetProperty("error", out var errorElement))
                {
                    var errorMessage = errorElement.TryGetProperty("message", out var msg)
                        ? msg.GetString() ?? "Unknown error"
                        : "API error";
                    throw new InvalidOperationException($"OpenAI API error: {errorMessage}");
                }

                if (responseDoc.RootElement.TryGetProperty("choices", out var choices) &&
                    choices.GetArrayLength() > 0)
                {
                    var firstChoice = choices[0];
                    if (firstChoice.TryGetProperty("message", out var message) &&
                        message.TryGetProperty("content", out var contentProp))
                    {
                        string result = contentProp.GetString() ?? string.Empty;

                        if (string.IsNullOrWhiteSpace(result))
                        {
                            throw new InvalidOperationException("OpenAI returned an empty response");
                        }

                        var duration = DateTime.UtcNow - startTime;
                        _logger.LogInformation("Chat completion succeeded ({Length} characters) in {Duration}s",
                            result.Length, duration.TotalSeconds);

                        return result;
                    }
                }

                throw new InvalidOperationException("Invalid response structure from OpenAI API");
            }
            catch (TaskCanceledException ex)
            {
                lastException = ex;
                _logger.LogWarning(ex, "OpenAI chat completion timed out (attempt {Attempt}/{MaxRetries})",
                    attempt + 1, _maxRetries + 1);
                if (attempt >= _maxRetries)
                {
                    throw new InvalidOperationException(
                        $"OpenAI request timed out after {_timeout.TotalSeconds}s. Please check your internet connection or try again later.", ex);
                }
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Failed to connect to OpenAI API (attempt {Attempt}/{MaxRetries})",
                    attempt + 1, _maxRetries + 1);
                if (attempt >= _maxRetries)
                {
                    throw new InvalidOperationException(
                        "Cannot connect to OpenAI API. Please check your internet connection and try again.", ex);
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < _maxRetries)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Error during chat completion with OpenAI (attempt {Attempt}/{MaxRetries})",
                    attempt + 1, _maxRetries + 1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during chat completion with OpenAI after all retries");
                throw;
            }
        }

        throw new InvalidOperationException(
            $"Failed to generate chat completion with OpenAI after {_maxRetries + 1} attempts. Please try again later.",
            lastException);
    }

    public async Task<SceneAnalysisResult?> AnalyzeSceneImportanceAsync(
        string sceneText,
        string? previousSceneText,
        string videoGoal,
        CancellationToken ct)
    {
        _logger.LogInformation("Analyzing scene importance with OpenAI");

        try
        {
            var systemPrompt = "You are a video pacing expert. Analyze scenes for optimal timing. " +
                              "Return your response ONLY as valid JSON with no additional text.";
            
            var userPrompt = $@"Analyze this scene and return JSON with:
- importance (0-100): How critical is this scene to the video's message
- complexity (0-100): How complex is the information presented
- emotionalIntensity (0-100): Emotional impact level
- informationDensity (""low""|""medium""|""high""): Amount of information
- optimalDurationSeconds (number): Recommended duration in seconds
- transitionType (""cut""|""fade""|""dissolve""): Recommended transition
- reasoning (string): Brief explanation

Scene: {sceneText}
{(previousSceneText != null ? $"Previous scene: {previousSceneText}" : "")}
Video goal: {videoGoal}

Respond with ONLY the JSON object, no other text:";

            var requestBody = new
            {
                model = _model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature = 0.3,
                max_tokens = 512,
                response_format = new { type = "json_object" }
            };

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30)); // Shorter timeout for analysis

            var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content, cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var responseDoc = JsonDocument.Parse(responseJson);

            if (responseDoc.RootElement.TryGetProperty("choices", out var choices) &&
                choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var contentProp))
                {
                    var analysisText = contentProp.GetString() ?? string.Empty;
                    
                    try
                    {
                        var analysisDoc = JsonDocument.Parse(analysisText);
                        var root = analysisDoc.RootElement;

                        var result = new SceneAnalysisResult(
                            Importance: root.TryGetProperty("importance", out var imp) ? imp.GetDouble() : 50.0,
                            Complexity: root.TryGetProperty("complexity", out var comp) ? comp.GetDouble() : 50.0,
                            EmotionalIntensity: root.TryGetProperty("emotionalIntensity", out var emo) ? emo.GetDouble() : 50.0,
                            InformationDensity: root.TryGetProperty("informationDensity", out var info) ? info.GetString() ?? "medium" : "medium",
                            OptimalDurationSeconds: root.TryGetProperty("optimalDurationSeconds", out var dur) ? dur.GetDouble() : 10.0,
                            TransitionType: root.TryGetProperty("transitionType", out var trans) ? trans.GetString() ?? "cut" : "cut",
                            Reasoning: root.TryGetProperty("reasoning", out var reas) ? reas.GetString() ?? "" : ""
                        );

                        _logger.LogInformation("Scene analysis complete. Importance: {Importance}, Complexity: {Complexity}", 
                            result.Importance, result.Complexity);
                        
                        return result;
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse scene analysis JSON from OpenAI: {Response}", analysisText);
                        return null;
                    }
                }
            }

            _logger.LogWarning("OpenAI scene analysis response did not contain expected structure");
            return null;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "OpenAI scene analysis request timed out");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to connect to OpenAI API for scene analysis");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing scene with OpenAI");
            return null;
        }
    }

    public async Task<VisualPromptResult?> GenerateVisualPromptAsync(
        string sceneText,
        string? previousSceneText,
        string videoTone,
        VisualStyle targetStyle,
        CancellationToken ct)
    {
        _logger.LogInformation("Generating visual prompt with OpenAI");

        try
        {
            var systemPrompt = "You are a professional cinematographer and visual director. " +
                              "Create detailed visual prompts for image generation. " +
                              "Return your response ONLY as valid JSON with no additional text.";
            
            var userPrompt = $@"Create a detailed visual prompt for this scene and return JSON with:
- detailedDescription (string): Detailed visual description (100-200 tokens) of what should be shown
- compositionGuidelines (string): Composition rules (e.g., ""rule of thirds, leading lines"")
- lightingMood (string): Lighting mood (e.g., ""dramatic"", ""soft"", ""golden hour"")
- lightingDirection (string): Light direction (e.g., ""front"", ""side"", ""back"")
- lightingQuality (string): Light quality (e.g., ""soft"", ""hard"", ""diffused"")
- timeOfDay (string): Time of day (e.g., ""day"", ""golden hour"", ""evening"", ""night"")
- colorPalette (array of strings): 3-5 specific color hex codes
- shotType (string): Shot type (e.g., ""wide shot"", ""medium shot"", ""close-up"")
- cameraAngle (string): Camera angle (e.g., ""eye level"", ""high angle"", ""low angle"")
- depthOfField (string): Depth of field (e.g., ""shallow"", ""medium"", ""deep"")
- styleKeywords (array of strings): 5-7 keywords for the visual style
- negativeElements (array of strings): Elements to avoid in the image
- continuityElements (array of strings): Elements that should remain consistent with previous scenes
- reasoning (string): Brief explanation of choices

Scene text: {sceneText}
{(previousSceneText != null ? $"Previous scene: {previousSceneText}" : "")}
Video tone: {videoTone}
Target style: {targetStyle}

Respond with ONLY the JSON object, no other text:";

            var requestBody = new
            {
                model = _model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature = 0.7,
                max_tokens = 1024,
                response_format = new { type = "json_object" }
            };

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content, cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var responseDoc = JsonDocument.Parse(responseJson);

            if (responseDoc.RootElement.TryGetProperty("choices", out var choices) &&
                choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var contentProp))
                {
                    var promptText = contentProp.GetString() ?? string.Empty;
                    
                    try
                    {
                        var promptDoc = JsonDocument.Parse(promptText);
                        var root = promptDoc.RootElement;

                        var result = new VisualPromptResult(
                            DetailedDescription: root.TryGetProperty("detailedDescription", out var desc) ? desc.GetString() ?? "" : "",
                            CompositionGuidelines: root.TryGetProperty("compositionGuidelines", out var comp) ? comp.GetString() ?? "" : "",
                            LightingMood: root.TryGetProperty("lightingMood", out var mood) ? mood.GetString() ?? "neutral" : "neutral",
                            LightingDirection: root.TryGetProperty("lightingDirection", out var dir) ? dir.GetString() ?? "front" : "front",
                            LightingQuality: root.TryGetProperty("lightingQuality", out var qual) ? qual.GetString() ?? "soft" : "soft",
                            TimeOfDay: root.TryGetProperty("timeOfDay", out var time) ? time.GetString() ?? "day" : "day",
                            ColorPalette: ParseStringArray(root, "colorPalette"),
                            ShotType: root.TryGetProperty("shotType", out var shot) ? shot.GetString() ?? "medium shot" : "medium shot",
                            CameraAngle: root.TryGetProperty("cameraAngle", out var angle) ? angle.GetString() ?? "eye level" : "eye level",
                            DepthOfField: root.TryGetProperty("depthOfField", out var dof) ? dof.GetString() ?? "medium" : "medium",
                            StyleKeywords: ParseStringArray(root, "styleKeywords"),
                            NegativeElements: ParseStringArray(root, "negativeElements"),
                            ContinuityElements: ParseStringArray(root, "continuityElements"),
                            Reasoning: root.TryGetProperty("reasoning", out var reas) ? reas.GetString() ?? "" : ""
                        );

                        _logger.LogInformation("Visual prompt generated successfully");
                        return result;
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse visual prompt JSON from OpenAI: {Response}", promptText);
                        return null;
                    }
                }
            }

            _logger.LogWarning("OpenAI visual prompt response did not contain expected structure");
            return null;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "OpenAI visual prompt request timed out");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to connect to OpenAI API for visual prompt");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating visual prompt with OpenAI");
            return null;
        }
    }

    private static string[] ParseStringArray(JsonElement root, string propertyName)
    {
        if (root.TryGetProperty(propertyName, out var arrayProp) && arrayProp.ValueKind == JsonValueKind.Array)
        {
            var items = new System.Collections.Generic.List<string>();
            foreach (var item in arrayProp.EnumerateArray())
            {
                var str = item.GetString();
                if (!string.IsNullOrEmpty(str))
                {
                    items.Add(str);
                }
            }
            return items.ToArray();
        }
        return Array.Empty<string>();
    }

    public async Task<ContentComplexityAnalysisResult?> AnalyzeContentComplexityAsync(
        string sceneText,
        string? previousSceneText,
        string videoGoal,
        CancellationToken ct)
    {
        _logger.LogInformation("Analyzing content complexity with OpenAI for video goal: {Goal}", videoGoal);

        var prompt = BuildComplexityAnalysisPrompt(sceneText, previousSceneText, videoGoal);

        try
        {
            var requestBody = new
            {
                model = "gpt-4o-mini",
                messages = new[]
                {
                    new { role = "system", content = "You are an expert in cognitive science and educational content analysis. Analyze the complexity of video content to optimize pacing for viewer comprehension." },
                    new { role = "user", content = prompt }
                },
                temperature = 0.3,
                max_tokens = 500,
                response_format = new { type = "json_object" }
            };

            var json = System.Text.Json.JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            var messageContent = root
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrEmpty(messageContent))
            {
                _logger.LogWarning("OpenAI returned empty complexity analysis");
                return null;
            }

            using var analysisDoc = JsonDocument.Parse(messageContent);
            var analysisRoot = analysisDoc.RootElement;

            var result = new ContentComplexityAnalysisResult(
                OverallComplexityScore: GetDoubleProperty(analysisRoot, "overall_complexity_score", 50.0),
                ConceptDifficulty: GetDoubleProperty(analysisRoot, "concept_difficulty", 50.0),
                TerminologyDensity: GetDoubleProperty(analysisRoot, "terminology_density", 50.0),
                PrerequisiteKnowledgeLevel: GetDoubleProperty(analysisRoot, "prerequisite_knowledge_level", 50.0),
                MultiStepReasoningRequired: GetDoubleProperty(analysisRoot, "multi_step_reasoning_required", 50.0),
                NewConceptsIntroduced: GetIntProperty(analysisRoot, "new_concepts_introduced", 3),
                CognitiveProcessingTimeSeconds: GetDoubleProperty(analysisRoot, "cognitive_processing_time_seconds", 10.0),
                OptimalAttentionWindowSeconds: GetDoubleProperty(analysisRoot, "optimal_attention_window_seconds", 10.0),
                DetailedBreakdown: GetStringProperty(analysisRoot, "detailed_breakdown", "No breakdown provided")
            );

            _logger.LogInformation("Content complexity analyzed: Overall={Score:F0}, NewConcepts={Concepts}",
                result.OverallComplexityScore, result.NewConceptsIntroduced);

            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to connect to OpenAI API for complexity analysis");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing content complexity with OpenAI");
            return null;
        }
    }

    private static string BuildComplexityAnalysisPrompt(string sceneText, string? previousSceneText, string videoGoal)
    {
        var prompt = new System.Text.StringBuilder();
        prompt.AppendLine("Analyze the cognitive complexity of this video scene content to optimize viewer comprehension and pacing.");
        prompt.AppendLine();
        prompt.AppendLine($"VIDEO GOAL: {videoGoal}");
        prompt.AppendLine();
        prompt.AppendLine("SCENE CONTENT:");
        prompt.AppendLine(sceneText);
        
        if (!string.IsNullOrEmpty(previousSceneText))
        {
            prompt.AppendLine();
            prompt.AppendLine("PREVIOUS SCENE (for context):");
            prompt.AppendLine(previousSceneText);
        }

        prompt.AppendLine();
        prompt.AppendLine("Provide a JSON response with the following fields (all scores 0-100):");
        prompt.AppendLine("{");
        prompt.AppendLine("  \"overall_complexity_score\": <0-100>,");
        prompt.AppendLine("  \"concept_difficulty\": <0-100, how difficult are the concepts?>,");
        prompt.AppendLine("  \"terminology_density\": <0-100, how many specialized terms?>,");
        prompt.AppendLine("  \"prerequisite_knowledge_level\": <0-100, how much prior knowledge assumed?>,");
        prompt.AppendLine("  \"multi_step_reasoning_required\": <0-100, requires following multiple logical steps?>,");
        prompt.AppendLine("  \"new_concepts_introduced\": <integer count of new concepts>,");
        prompt.AppendLine("  \"cognitive_processing_time_seconds\": <estimated time to process>,");
        prompt.AppendLine("  \"optimal_attention_window_seconds\": <5-15, how long to show this content?>,");
        prompt.AppendLine("  \"detailed_breakdown\": \"<2-3 sentence explanation of complexity factors>\"");
        prompt.AppendLine("}");

        return prompt.ToString();
    }

    private static double GetDoubleProperty(JsonElement root, string propertyName, double defaultValue)
    {
        if (root.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number)
                return prop.GetDouble();
            if (prop.ValueKind == JsonValueKind.String && double.TryParse(prop.GetString(), out var parsed))
                return parsed;
        }
        return defaultValue;
    }

    private static int GetIntProperty(JsonElement root, string propertyName, int defaultValue)
    {
        if (root.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number)
                return prop.GetInt32();
            if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out var parsed))
                return parsed;
        }
        return defaultValue;
    }

    private static string GetStringProperty(JsonElement root, string propertyName, string defaultValue)
    {
        if (root.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            var value = prop.GetString();
            return value ?? defaultValue;
        }
        return defaultValue;
    }

    // Removed legacy prompt building methods - now using EnhancedPromptTemplates

    public async Task<SceneCoherenceResult?> AnalyzeSceneCoherenceAsync(
        string fromSceneText,
        string toSceneText,
        string videoGoal,
        CancellationToken ct)
    {
        _logger.LogInformation("Analyzing scene coherence with OpenAI");

        try
        {
            var systemPrompt = "You are a narrative flow expert analyzing video scene transitions. " +
                              "Return your response ONLY as valid JSON with no additional text.";
            
            var userPrompt = $@"Analyze the narrative coherence between these two consecutive scenes and return JSON with:
- coherenceScore (0-100): How well scene B flows from scene A (0=no connection, 100=perfect flow)
- connectionTypes (array of strings): Types of connections (choose from: ""causal"", ""thematic"", ""prerequisite"", ""callback"", ""sequential"", ""contrast"")
- confidenceScore (0-1): Your confidence in this analysis
- reasoning (string): Brief explanation of the coherence assessment

Scene A: {fromSceneText}

Scene B: {toSceneText}

Video goal: {videoGoal}

Respond with ONLY the JSON object, no other text:";

            var requestBody = new
            {
                model = _model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature = 0.3,
                max_tokens = 512,
                response_format = new { type = "json_object" }
            };

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content, cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var responseDoc = JsonDocument.Parse(responseJson);

            if (responseDoc.RootElement.TryGetProperty("choices", out var choices) &&
                choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var contentProp))
                {
                    var analysisText = contentProp.GetString() ?? string.Empty;
                    
                    try
                    {
                        var analysisDoc = JsonDocument.Parse(analysisText);
                        var root = analysisDoc.RootElement;

                        var connectionTypes = new List<string>();
                        if (root.TryGetProperty("connectionTypes", out var connTypes) && 
                            connTypes.ValueKind == JsonValueKind.Array)
                        {
                            connectionTypes = connTypes.EnumerateArray()
                                .Where(e => e.ValueKind == JsonValueKind.String)
                                .Select(e => e.GetString() ?? "sequential")
                                .ToList();
                        }

                        var result = new SceneCoherenceResult(
                            CoherenceScore: root.TryGetProperty("coherenceScore", out var score) ? score.GetDouble() : 50.0,
                            ConnectionTypes: connectionTypes.ToArray(),
                            ConfidenceScore: root.TryGetProperty("confidenceScore", out var conf) ? conf.GetDouble() : 0.5,
                            Reasoning: root.TryGetProperty("reasoning", out var reas) ? reas.GetString() ?? "" : ""
                        );

                        _logger.LogInformation("Scene coherence analysis complete. Score: {Score}", result.CoherenceScore);
                        
                        return result;
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse coherence analysis JSON");
                        return null;
                    }
                }
            }

            _logger.LogWarning("No valid response from OpenAI for scene coherence analysis");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing scene coherence with OpenAI");
            return null;
        }
    }

    public async Task<NarrativeArcResult?> ValidateNarrativeArcAsync(
        IReadOnlyList<string> sceneTexts,
        string videoGoal,
        string videoType,
        CancellationToken ct)
    {
        _logger.LogInformation("Validating narrative arc with OpenAI for {VideoType} video", videoType);

        try
        {
            var systemPrompt = "You are a narrative structure expert analyzing video story arcs. " +
                              "Return your response ONLY as valid JSON with no additional text.";
            
            var scenesText = string.Join("\n\n", sceneTexts.Select((s, i) => $"Scene {i + 1}: {s}"));
            
            var expectedStructures = new Dictionary<string, string>
            {
                { "educational", "problem → explanation → solution" },
                { "entertainment", "setup → conflict → resolution" },
                { "documentary", "introduction → evidence → conclusion" },
                { "tutorial", "overview → steps → summary" },
                { "general", "introduction → body → conclusion" }
            };

            var expectedStructure = expectedStructures.GetValueOrDefault(
                videoType.ToLowerInvariant(), 
                expectedStructures["general"]);

            var userPrompt = $@"Analyze the narrative arc of this {videoType} video and return JSON with:
- isValid (boolean): Whether the narrative follows a coherent arc
- detectedStructure (string): The structure you detect (e.g., ""setup → conflict → resolution"")
- expectedStructure (string): ""{expectedStructure}""
- structuralIssues (array of strings): Any problems with the narrative structure
- recommendations (array of strings): Suggestions to improve the narrative arc
- reasoning (string): Brief explanation of your assessment

{scenesText}

Video goal: {videoGoal}

Respond with ONLY the JSON object, no other text:";

            var requestBody = new
            {
                model = _model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature = 0.3,
                max_tokens = 1024,
                response_format = new { type = "json_object" }
            };

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(45));

            var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content, cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var responseDoc = JsonDocument.Parse(responseJson);

            if (responseDoc.RootElement.TryGetProperty("choices", out var choices) &&
                choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var contentProp))
                {
                    var analysisText = contentProp.GetString() ?? string.Empty;
                    
                    try
                    {
                        var analysisDoc = JsonDocument.Parse(analysisText);
                        var root = analysisDoc.RootElement;

                        var structuralIssues = new List<string>();
                        if (root.TryGetProperty("structuralIssues", out var issues) && 
                            issues.ValueKind == JsonValueKind.Array)
                        {
                            structuralIssues = issues.EnumerateArray()
                                .Where(e => e.ValueKind == JsonValueKind.String)
                                .Select(e => e.GetString() ?? "")
                                .Where(s => !string.IsNullOrEmpty(s))
                                .ToList();
                        }

                        var recommendations = new List<string>();
                        if (root.TryGetProperty("recommendations", out var recs) && 
                            recs.ValueKind == JsonValueKind.Array)
                        {
                            recommendations = recs.EnumerateArray()
                                .Where(e => e.ValueKind == JsonValueKind.String)
                                .Select(e => e.GetString() ?? "")
                                .Where(s => !string.IsNullOrEmpty(s))
                                .ToList();
                        }

                        var result = new NarrativeArcResult(
                            IsValid: root.TryGetProperty("isValid", out var valid) && valid.GetBoolean(),
                            DetectedStructure: root.TryGetProperty("detectedStructure", out var detected) ? detected.GetString() ?? "" : "",
                            ExpectedStructure: expectedStructure,
                            StructuralIssues: structuralIssues.ToArray(),
                            Recommendations: recommendations.ToArray(),
                            Reasoning: root.TryGetProperty("reasoning", out var reas) ? reas.GetString() ?? "" : ""
                        );

                        _logger.LogInformation("Narrative arc validation complete. Valid: {IsValid}", result.IsValid);
                        
                        return result;
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse narrative arc JSON");
                        return null;
                    }
                }
            }

            _logger.LogWarning("No valid response from OpenAI for narrative arc validation");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating narrative arc with OpenAI");
            return null;
        }
    }

    public async Task<string?> GenerateTransitionTextAsync(
        string fromSceneText,
        string toSceneText,
        string videoGoal,
        CancellationToken ct)
    {
        _logger.LogInformation("Generating transition text with OpenAI");

        try
        {
            var systemPrompt = "You are a professional scriptwriter specializing in smooth scene transitions.";
            
            var userPrompt = $@"Create a brief transition sentence or phrase (1-2 sentences maximum) to smoothly connect these two scenes:

Scene A: {fromSceneText}

Scene B: {toSceneText}

Video goal: {videoGoal}

The transition should feel natural and help the viewer understand the connection between these scenes. 
Return ONLY the transition text, no explanations or additional commentary:";

            var requestBody = new
            {
                model = _model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature = 0.7,
                max_tokens = 128
            };

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content, cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var responseDoc = JsonDocument.Parse(responseJson);

            if (responseDoc.RootElement.TryGetProperty("choices", out var choices) &&
                choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var contentProp))
                {
                    var transitionText = contentProp.GetString()?.Trim() ?? string.Empty;
                    
                    if (!string.IsNullOrWhiteSpace(transitionText))
                    {
                        _logger.LogInformation("Generated transition text: {Text}", transitionText);
                        return transitionText;
                    }
                }
            }

            _logger.LogWarning("No valid transition text from OpenAI");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating transition text with OpenAI");
            return null;
        }
    }

    /// <summary>
    /// Get available OpenAI models for the configured API key
    /// Returns models filtered for chat completion (gpt and o1 models)
    /// </summary>
    public async Task<List<ModelInfo>> GetAvailableModelsAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Fetching available OpenAI models");

        try
        {
            var requestUri = "https://api.openai.com/v1/models";
            
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            var response = await _httpClient.GetAsync(requestUri, cts.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch models: HTTP {StatusCode}", response.StatusCode);
                return new List<ModelInfo>();
            }

            var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var responseDoc = JsonDocument.Parse(responseJson);

            var models = new List<ModelInfo>();

            if (responseDoc.RootElement.TryGetProperty("data", out var dataArray))
            {
                foreach (var model in dataArray.EnumerateArray())
                {
                    if (model.TryGetProperty("id", out var idProp))
                    {
                        var modelId = idProp.GetString();
                        if (!string.IsNullOrEmpty(modelId))
                        {
                            // Filter for chat models only (models containing "gpt" or "o1")
                            if (modelId.Contains("gpt", StringComparison.OrdinalIgnoreCase) ||
                                modelId.Contains("o1", StringComparison.OrdinalIgnoreCase))
                            {
                                var created = model.TryGetProperty("created", out var createdProp)
                                    ? createdProp.GetInt64()
                                    : 0;

                                models.Add(new ModelInfo
                                {
                                    Id = modelId,
                                    Name = modelId,
                                    Capabilities = new[] { "chat", "completion" },
                                    Created = created
                                });
                            }
                        }
                    }
                }
            }

            // Sort by created timestamp (newest first)
            models = models.OrderByDescending(m => m.Created).ToList();

            _logger.LogInformation("Fetched {Count} chat models successfully", models.Count);

            return models;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Request timed out while fetching models");
            return new List<ModelInfo>();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error while fetching models");
            return new List<ModelInfo>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching available models");
            return new List<ModelInfo>();
        }
    }

    /// <summary>
    /// Generate audio narration using OpenAI's audio capabilities.
    /// Supports GPT-4o and GPT-4o-audio-preview models with audio output.
    /// </summary>
    /// <param name="text">Text to convert to speech</param>
    /// <param name="audioConfig">Audio configuration (voice, format, modalities)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Base64-encoded audio data and transcript</returns>
    public async Task<AudioResponse> GenerateWithAudioAsync(
        string text,
        AudioConfig audioConfig,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Generating audio with OpenAI (model: {Model}, voice: {Voice})", _model, audioConfig.Voice);

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Text cannot be empty", nameof(text));
        }

        // Validate model supports audio
        if (!_model.Contains("gpt-4o", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Model {_model} does not support audio output. Use gpt-4o or gpt-4o-audio-preview.");
        }

        var startTime = DateTime.UtcNow;
        Exception? lastException = null;

        for (int attempt = 0; attempt <= _maxRetries; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    var backoffDelay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    _logger.LogInformation("Retry attempt {Attempt}/{MaxRetries} after {Delay}s delay",
                        attempt, _maxRetries, backoffDelay.TotalSeconds);
                    await Task.Delay(backoffDelay, ct).ConfigureAwait(false);
                }

                // Build request with audio configuration
                var requestBody = new
                {
                    model = _model,
                    modalities = audioConfig.Modalities,
                    audio = new { voice = audioConfig.Voice.ToString().ToLowerInvariant(), format = audioConfig.Format.ToString().ToLowerInvariant() },
                    messages = new[]
                    {
                        new
                        {
                            role = "user",
                            content = text
                        }
                    }
                };

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(_timeout);

                var response = await _httpClient.PostAsync(
                    "https://api.openai.com/v1/chat/completions",
                    content,
                    cts.Token).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        throw new InvalidOperationException(
                            "OpenAI API key is invalid or has been revoked. Please check your API key in Settings → Providers → OpenAI");
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        _logger.LogWarning("OpenAI rate limit exceeded (attempt {Attempt}/{MaxRetries})", 
                            attempt + 1, _maxRetries + 1);
                        if (attempt >= _maxRetries)
                        {
                            throw new InvalidOperationException(
                                "OpenAI rate limit exceeded. Please wait a moment and try again, or upgrade your OpenAI plan.");
                        }
                        lastException = new Exception($"Rate limit exceeded: {errorContent}");
                        continue;
                    }
                    else if ((int)response.StatusCode >= 500)
                    {
                        _logger.LogWarning("OpenAI server error (attempt {Attempt}/{MaxRetries}): {StatusCode}",
                            attempt + 1, _maxRetries + 1, response.StatusCode);
                        if (attempt >= _maxRetries)
                        {
                            throw new InvalidOperationException(
                                $"OpenAI service is experiencing issues (HTTP {response.StatusCode}). Please try again later.");
                        }
                        lastException = new Exception($"Server error: {errorContent}");
                        continue;
                    }

                    response.EnsureSuccessStatusCode();
                }

                var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var responseDoc = JsonDocument.Parse(responseJson);

                if (responseDoc.RootElement.TryGetProperty("choices", out var choices) &&
                    choices.GetArrayLength() > 0)
                {
                    var firstChoice = choices[0];
                    if (firstChoice.TryGetProperty("message", out var message))
                    {
                        string? transcript = null;
                        string? audioData = null;

                        // Extract text transcript
                        if (message.TryGetProperty("content", out var contentProp) &&
                            contentProp.ValueKind == JsonValueKind.String)
                        {
                            transcript = contentProp.GetString();
                        }

                        // Extract audio data
                        if (message.TryGetProperty("audio", out var audioProp))
                        {
                            if (audioProp.TryGetProperty("data", out var dataProp))
                            {
                                audioData = dataProp.GetString();
                            }

                            // Use audio transcript if text content not provided
                            if (string.IsNullOrEmpty(transcript) &&
                                audioProp.TryGetProperty("transcript", out var audioTranscript))
                            {
                                transcript = audioTranscript.GetString();
                            }
                        }

                        if (string.IsNullOrWhiteSpace(audioData))
                        {
                            throw new InvalidOperationException("OpenAI did not return audio data in response");
                        }

                        var duration = DateTime.UtcNow - startTime;
                        _logger.LogInformation("Audio generated successfully in {Duration}s", duration.TotalSeconds);

                        return new AudioResponse
                        {
                            AudioData = audioData,
                            Transcript = transcript ?? text,
                            Format = audioConfig.Format.ToString().ToLowerInvariant(),
                            Voice = audioConfig.Voice.ToString().ToLowerInvariant()
                        };
                    }
                }

                _logger.LogWarning("OpenAI response did not contain expected audio structure");
                throw new InvalidOperationException("Invalid response structure from OpenAI API");
            }
            catch (TaskCanceledException ex)
            {
                lastException = ex;
                _logger.LogWarning(ex, "OpenAI audio request timed out (attempt {Attempt}/{MaxRetries})",
                    attempt + 1, _maxRetries + 1);
                if (attempt >= _maxRetries)
                {
                    throw new InvalidOperationException(
                        $"OpenAI audio request timed out after {_timeout.TotalSeconds}s. Please check your internet connection or try again later.", ex);
                }
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Failed to connect to OpenAI API (attempt {Attempt}/{MaxRetries})",
                    attempt + 1, _maxRetries + 1);
                if (attempt >= _maxRetries)
                {
                    throw new InvalidOperationException(
                        "Cannot connect to OpenAI API. Please check your internet connection and try again.", ex);
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < _maxRetries)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Error generating audio with OpenAI (attempt {Attempt}/{MaxRetries})",
                    attempt + 1, _maxRetries + 1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating audio with OpenAI after all retries");
                throw;
            }
        }

        throw new InvalidOperationException(
            $"Failed to generate audio with OpenAI after {_maxRetries + 1} attempts. Please try again later.", lastException);
    }

    /// <summary>
    /// Validate the OpenAI API key by calling the models endpoint
    /// Returns validation result with error details
    /// </summary>
    public async Task<ValidationResult> ValidateApiKeyAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Validating OpenAI API key");

        try
        {
            var requestUri = "https://api.openai.com/v1/models";
            
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            var response = await _httpClient.GetAsync(requestUri, cts.Token).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                // Get available models as part of validation
                var models = await GetAvailableModelsAsync(ct).ConfigureAwait(false);

                _logger.LogInformation("API key validated successfully, {Count} models available", models.Count);

                return new ValidationResult
                {
                    IsValid = true,
                    Message = "API key is valid",
                    AvailableModels = models
                };
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning("API key validation failed: Unauthorized");
                return new ValidationResult
                {
                    IsValid = false,
                    Message = "Invalid API key"
                };
            }

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning("API key validation failed: Rate limited");
                return new ValidationResult
                {
                    IsValid = false,
                    Message = "Rate limit exceeded or billing issue"
                };
            }

            _logger.LogWarning("API key validation failed: HTTP {StatusCode}", response.StatusCode);
            return new ValidationResult
            {
                IsValid = false,
                Message = $"Validation failed: HTTP {response.StatusCode}"
            };
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "API key validation timed out");
            return new ValidationResult
            {
                IsValid = false,
                Message = "Network connectivity issue"
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error during API key validation");
            return new ValidationResult
            {
                IsValid = false,
                Message = "Network connectivity issue"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during API key validation");
            return new ValidationResult
            {
                IsValid = false,
                Message = $"Validation error: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Whether this provider supports streaming (OpenAI supports streaming)
    /// </summary>
    public bool SupportsStreaming => true;

    /// <summary>
    /// Get provider characteristics for adaptive UI
    /// </summary>
    public LlmProviderCharacteristics GetCharacteristics()
    {
        return new LlmProviderCharacteristics
        {
            IsLocal = false,
            ExpectedFirstTokenMs = 500,
            ExpectedTokensPerSec = 30,
            SupportsStreaming = true,
            ProviderTier = "Pro",
            CostPer1KTokens = _model.Contains("gpt-4o", StringComparison.OrdinalIgnoreCase) ? 0.01m : 0.001m
        };
    }

    /// <summary>
    /// Stream script generation with real-time token-by-token updates
    /// </summary>
    public async IAsyncEnumerable<LlmStreamChunk> DraftScriptStreamAsync(
        Brief brief, 
        PlanSpec spec, 
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _logger.LogInformation("Starting streaming script generation with OpenAI (model: {Model}) for topic: {Topic}", 
            _model, brief.Topic);

        var startTime = DateTime.UtcNow;
        DateTime? firstTokenTime = null;

        // Build enhanced prompts
        string systemPrompt = EnhancedPromptTemplates.GetSystemPromptForScriptGeneration();
        string userPrompt = await _promptCustomizationService.BuildCustomizedPromptAsync(brief, spec, brief.PromptModifiers, ct).ConfigureAwait(false);

        // Apply enhancement callback if configured
        if (PromptEnhancementCallback != null)
        {
            userPrompt = await PromptEnhancementCallback(userPrompt, brief, spec).ConfigureAwait(false);
        }

        // Create streaming request
        var requestBody = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            temperature = 0.7,
            max_tokens = 2048,
            stream = true
        };

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);

        HttpResponseMessage? response = null;
        Exception? initError = null;
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
            {
                Content = content
            };
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new InvalidOperationException(
                    $"OpenAI API error: HTTP {response.StatusCode} - {errorContent}");
            }
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("Streaming cancelled by user");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initiating streaming script generation");
            initError = ex;
        }

        if (initError != null)
        {
            yield return new LlmStreamChunk
            {
                ProviderName = "OpenAI",
                Content = string.Empty,
                TokenIndex = 0,
                IsFinal = true,
                ErrorMessage = $"Streaming error: {initError.Message}"
            };
            yield break;
        }

        using (response!)
        {
            var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var reader = new StreamReader(stream);

            var accumulated = new StringBuilder();
            var tokenIndex = 0;

            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                
                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: "))
                {
                    continue;
                }

                var data = line.Substring(6); // Remove "data: " prefix
                
                if (data == "[DONE]")
                {
                    // Final chunk
                    var duration = DateTime.UtcNow - startTime;
                    var timeToFirstToken = firstTokenTime.HasValue 
                        ? (firstTokenTime.Value - startTime).TotalMilliseconds 
                        : 0;
                    var tokensPerSec = tokenIndex > 0 && duration.TotalSeconds > 0
                        ? tokenIndex / duration.TotalSeconds
                        : 0;

                    yield return new LlmStreamChunk
                    {
                        ProviderName = "OpenAI",
                        Content = string.Empty,
                        AccumulatedContent = accumulated.ToString(),
                        TokenIndex = tokenIndex,
                        IsFinal = true,
                        Metadata = new LlmStreamMetadata
                        {
                            TotalTokens = tokenIndex,
                            EstimatedCost = CalculateCost(tokenIndex),
                            TokensPerSecond = tokensPerSec,
                            IsLocalModel = false,
                            ModelName = _model,
                            TimeToFirstTokenMs = timeToFirstToken,
                            TotalDurationMs = duration.TotalMilliseconds,
                            FinishReason = "stop"
                        }
                    };
                    break;
                }

                // Parse JSON - skip on parse error
                JsonDocument? doc = null;
                try
                {
                    doc = JsonDocument.Parse(data);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse streaming JSON line: {Line}", data);
                    continue;
                }

                using (doc)
                {
                    var root = doc.RootElement;

                    if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                    {
                        continue;
                    }

                    var firstChoice = choices[0];
                    if (!firstChoice.TryGetProperty("delta", out var delta))
                    {
                        continue;
                    }

                    if (delta.TryGetProperty("content", out var contentProp))
                    {
                        var chunk = contentProp.GetString() ?? string.Empty;
                        
                        if (!string.IsNullOrEmpty(chunk))
                        {
                            if (!firstTokenTime.HasValue)
                            {
                                firstTokenTime = DateTime.UtcNow;
                            }

                            accumulated.Append(chunk);
                            tokenIndex++;

                            yield return new LlmStreamChunk
                            {
                                ProviderName = "OpenAI",
                                Content = chunk,
                                AccumulatedContent = accumulated.ToString(),
                                TokenIndex = tokenIndex,
                                IsFinal = false
                            };
                        }
                    }

                    // Check for finish_reason
                    if (firstChoice.TryGetProperty("finish_reason", out var finishReason) && 
                        finishReason.ValueKind != JsonValueKind.Null)
                    {
                        var duration = DateTime.UtcNow - startTime;
                        var timeToFirstToken = firstTokenTime.HasValue 
                            ? (firstTokenTime.Value - startTime).TotalMilliseconds 
                            : 0;
                        var tokensPerSec = tokenIndex > 0 && duration.TotalSeconds > 0
                            ? tokenIndex / duration.TotalSeconds
                            : 0;

                        yield return new LlmStreamChunk
                        {
                            ProviderName = "OpenAI",
                            Content = string.Empty,
                            AccumulatedContent = accumulated.ToString(),
                            TokenIndex = tokenIndex,
                            IsFinal = true,
                            Metadata = new LlmStreamMetadata
                            {
                                TotalTokens = tokenIndex,
                                EstimatedCost = CalculateCost(tokenIndex),
                                TokensPerSecond = tokensPerSec,
                                IsLocalModel = false,
                                ModelName = _model,
                                TimeToFirstTokenMs = timeToFirstToken,
                                TotalDurationMs = duration.TotalMilliseconds,
                                FinishReason = finishReason.GetString() ?? "stop"
                            }
                        };
                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Calculate estimated cost for OpenAI tokens
    /// </summary>
    private decimal CalculateCost(int tokens)
    {
        var costPer1KTokens = _model.Contains("gpt-4o", StringComparison.OrdinalIgnoreCase) ? 0.01m : 0.001m;
        return (tokens / 1000m) * costPer1KTokens;
    }
}

/// <summary>
/// Information about an available OpenAI model
/// </summary>
public class ModelInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string[] Capabilities { get; set; } = Array.Empty<string>();
    public long Created { get; set; }
}

/// <summary>
/// Result of API key validation
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<ModelInfo> AvailableModels { get; set; } = new();
}

/// <summary>
/// Response from audio generation API
/// </summary>
public class AudioResponse
{
    /// <summary>
    /// Base64-encoded audio data
    /// </summary>
    public string AudioData { get; set; } = string.Empty;

    /// <summary>
    /// Transcript of the generated audio
    /// </summary>
    public string Transcript { get; set; } = string.Empty;

    /// <summary>
    /// Audio format (wav, mp3, etc.)
    /// </summary>
    public string Format { get; set; } = "wav";

    /// <summary>
    /// Voice used for generation
    /// </summary>
    public string Voice { get; set; } = "alloy";
}
