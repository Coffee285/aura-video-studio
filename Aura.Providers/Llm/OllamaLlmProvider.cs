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
using Aura.Core.Models;
using Aura.Core.Models.Generation;
using Aura.Core.Models.Narrative;
using Aura.Core.Models.Ollama;
using Aura.Core.Models.Streaming;
using Aura.Core.Models.Visual;
using Aura.Core.Providers;
using Aura.Core.Services.AI;
using Microsoft.Extensions.Logging;

namespace Aura.Providers.Llm;

/// <summary>
/// LLM provider that uses a local Ollama instance for script generation.
/// Supports optional ML-driven enhancements via callbacks and prompt customization.
/// </summary>
public class OllamaLlmProvider : ILlmProvider
{
    private readonly ILogger<OllamaLlmProvider> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly int _maxRetries;
    private readonly TimeSpan _timeout;
    private readonly PromptCustomizationService _promptCustomizationService;

    // Cache for availability check to avoid repeated calls
    private DateTime _lastAvailabilityCheck = DateTime.MinValue;
    private bool _lastAvailabilityResult = false;
    private readonly TimeSpan _availabilityCacheDuration = TimeSpan.FromSeconds(10);
    private readonly object _availabilityCacheLock = new object();

    /// <summary>
    /// Optional callback to enhance prompts before generation
    /// </summary>
    public Func<string, Brief, PlanSpec, Task<string>>? PromptEnhancementCallback { get; set; }

    /// <summary>
    /// Optional callback to track generation performance
    /// </summary>
    public Action<double, TimeSpan, bool>? PerformanceTrackingCallback { get; set; }

    public OllamaLlmProvider(
        ILogger<OllamaLlmProvider> logger,
        HttpClient httpClient,
        string baseUrl = "http://127.0.0.1:11434",
        string model = "llama3.1:8b-q4_k_m",
        int maxRetries = 2,
        int timeoutSeconds = 900, // 15 minutes - lenient for slow systems and large models (PR #523)
        PromptCustomizationService? promptCustomizationService = null)
    {
        _logger = logger;
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _baseUrl = ValidateBaseUrl(baseUrl);
        _model = model;
        _maxRetries = maxRetries;
        _timeout = TimeSpan.FromSeconds(timeoutSeconds);

        // CRITICAL: Ensure HttpClient timeout is longer than provider timeout
        // HttpClient has a default 100-second timeout that would kill connections
        // before our 15-minute provider timeout is reached
        if (_httpClient.Timeout < TimeSpan.FromSeconds(timeoutSeconds + 300))
        {
            _logger.LogWarning(
                "HttpClient timeout ({HttpClientTimeout}s) is shorter than provider timeout ({ProviderTimeout}s). " +
                "Increasing HttpClient timeout to prevent premature cancellation. " +
                "This should be configured in DI registration instead.",
                _httpClient.Timeout.TotalSeconds, timeoutSeconds);
            
            _httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds + 300); // Add 5-minute buffer
            
            _logger.LogInformation(
                "HttpClient timeout increased to {NewTimeout}s to accommodate slow Ollama generation",
                _httpClient.Timeout.TotalSeconds);
        }

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

        _logger.LogInformation(
            "OllamaLlmProvider initialized with baseUrl={BaseUrl}, model={Model}, " +
            "providerTimeout={ProviderTimeout}s, httpTimeout={HttpTimeout}s (lenient for slow systems)",
            _baseUrl, _model, timeoutSeconds, _httpClient.Timeout.TotalSeconds);
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

    public async Task<string> DraftScriptAsync(Brief brief, PlanSpec spec, CancellationToken ct)
    {
        // Use model override from LlmParameters if provided, otherwise use default model
        var modelToUse = !string.IsNullOrWhiteSpace(brief.LlmParameters?.ModelOverride)
            ? brief.LlmParameters.ModelOverride
            : _model;
        _logger.LogInformation("Generating script with Ollama (model: {Model}) at {BaseUrl} for topic: {Topic}. ModelOverride: {ModelOverride}, DefaultModel: {DefaultModel}",
            modelToUse, _baseUrl, brief.Topic, brief.LlmParameters?.ModelOverride ?? "null", _model);

        // Pre-check: Validate Ollama is available before attempting generation
        var isAvailable = await IsServiceAvailableAsync(ct).ConfigureAwait(false);
        if (!isAvailable)
        {
            var diagnosticMessage = await GetConnectionDiagnosticsAsync(ct).ConfigureAwait(false);
            var errorMessage = $"Cannot connect to Ollama at {_baseUrl}. {diagnosticMessage}";
            _logger.LogError("Ollama availability check failed. {ErrorMessage}", errorMessage);
            throw new InvalidOperationException(errorMessage);
        }

        var startTime = DateTime.UtcNow;
        Exception? lastException = null;

        for (int attempt = 0; attempt <= _maxRetries; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    _logger.LogInformation("Retry attempt {Attempt}/{MaxRetries} for Ollama", attempt, _maxRetries);
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct).ConfigureAwait(false); // Exponential backoff
                }

                // Build enhanced prompt for quality content with user customizations
                string systemPrompt = EnhancedPromptTemplates.GetSystemPromptForScriptGeneration();
                string userPrompt = await _promptCustomizationService.BuildCustomizedPromptAsync(brief, spec, brief.PromptModifiers, ct).ConfigureAwait(false);

                // Apply enhancement callback if configured
                if (PromptEnhancementCallback != null)
                {
                    userPrompt = await PromptEnhancementCallback(userPrompt, brief, spec).ConfigureAwait(false);
                }

                string prompt = $"{systemPrompt}\n\n{userPrompt}";

                // Get LLM parameters from brief, with defaults
                var llmParams = brief.LlmParameters;
                var temperature = llmParams?.Temperature ?? 0.7;
                var maxTokens = llmParams?.MaxTokens ?? 2048;
                var topP = llmParams?.TopP ?? 0.9;
                var topK = llmParams?.TopK;

                // Call Ollama API with proper format
                // Ollama uses num_predict (not max_tokens) and supports top_k
                // Ollama doesn't support frequency_penalty or presence_penalty
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
                    model = modelToUse,
                    prompt = prompt,
                    stream = false,
                    options = options
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(_timeout);

                var response = await _httpClient.PostAsync($"{_baseUrl}/api/generate", content, cts.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                // CRITICAL FIX: Use cts.Token instead of ct for ReadAsStringAsync
                // The original code used 'ct' which could have a shorter timeout set by the caller,
                // causing the response read to be cancelled even after the POST completed successfully.
                // This was the root cause of "empty content" issues after long Ollama generation.
                var responseJson = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);

                // Parse and validate response structure
                JsonDocument? responseDoc = null;
                try
                {
                    responseDoc = JsonDocument.Parse(responseJson);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Failed to parse Ollama JSON response: {Response}", responseJson.Substring(0, Math.Min(500, responseJson.Length)));
                    throw new InvalidOperationException("Ollama returned invalid JSON response", ex);
                }

                // Check for errors in response
                if (responseDoc.RootElement.TryGetProperty("error", out var errorElement))
                {
                    var errorMessage = errorElement.GetString() ?? "Unknown error";
                    _logger.LogError("Ollama API error: {Error}", errorMessage);
                    throw new InvalidOperationException($"Ollama API error: {errorMessage}");
                }

                if (responseDoc.RootElement.TryGetProperty("response", out var responseText))
                {
                    string script = responseText.GetString() ?? string.Empty;

                    _logger.LogInformation("Ollama returned response with {Length} characters (attempt {Attempt}/{MaxRetries})",
                        script.Length, attempt + 1, _maxRetries + 1);

                    if (string.IsNullOrWhiteSpace(script))
                    {
                        _logger.LogError("Ollama returned an empty response. Response JSON: {Response}",
                            responseJson.Substring(0, Math.Min(1000, responseJson.Length)));
                        throw new InvalidOperationException("Ollama returned an empty response");
                    }

                    // Log a preview of the response for debugging
                    var preview = script.Substring(0, Math.Min(200, script.Length));
                    _logger.LogDebug("Ollama response preview: {Preview}...", preview);

                    var duration = DateTime.UtcNow - startTime;

                    _logger.LogInformation("Script generated successfully with Ollama ({Length} characters) in {Duration:F1}s using model {Model}",
                        script.Length, duration.TotalSeconds, modelToUse);

                    // Track performance if callback configured
                    PerformanceTrackingCallback?.Invoke(75.0, duration, true);

                    return script;
                }

                // Check for other response fields that might indicate an issue
                if (responseDoc.RootElement.TryGetProperty("done", out var doneElement))
                {
                    var isDone = doneElement.GetBoolean();
                    _logger.LogWarning("Ollama response has 'done' field: {Done}, but no 'response' field. Full response: {Response}",
                        isDone, responseJson.Substring(0, Math.Min(500, responseJson.Length)));
                }

                _logger.LogError("Ollama response did not contain expected 'response' field. Available fields: {Fields}. Response: {Response}",
                    string.Join(", ", responseDoc.RootElement.EnumerateObject().Select(p => p.Name)),
                    responseJson.Substring(0, Math.Min(500, responseJson.Length)));
                throw new InvalidOperationException($"Invalid response structure from Ollama. Expected 'response' field but got: {responseJson.Substring(0, Math.Min(200, responseJson.Length))}");
            }
            catch (TaskCanceledException ex)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Ollama request timed out (attempt {Attempt}/{MaxRetries})", attempt + 1, _maxRetries + 1);
                if (attempt >= _maxRetries)
                {
                    var duration = DateTime.UtcNow - startTime;
                    PerformanceTrackingCallback?.Invoke(0, duration, false);
                    throw new InvalidOperationException(
                        $"Ollama request timed out after {_timeout.TotalSeconds}s. The model '{_model}' may be loading or Ollama may be overloaded.", ex);
                }
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Failed to connect to Ollama at {BaseUrl} (attempt {Attempt}/{MaxRetries})", _baseUrl, attempt + 1, _maxRetries + 1);
                if (attempt >= _maxRetries)
                {
                    var duration = DateTime.UtcNow - startTime;
                    PerformanceTrackingCallback?.Invoke(0, duration, false);
                    throw new InvalidOperationException(
                        $"Cannot connect to Ollama at {_baseUrl}. Please ensure Ollama is running: 'ollama serve'", ex);
                }
            }
            catch (Exception ex) when (attempt < _maxRetries)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Error generating script with Ollama (attempt {Attempt}/{MaxRetries})", attempt + 1, _maxRetries + 1);
            }
            catch (Exception ex)
            {
                var duration = DateTime.UtcNow - startTime;
                PerformanceTrackingCallback?.Invoke(0, duration, false);
                _logger.LogError(ex, "Error generating script with Ollama after all retries");
                throw;
            }
        }

        // Should not reach here, but just in case
        var finalDuration = DateTime.UtcNow - startTime;
        PerformanceTrackingCallback?.Invoke(0, finalDuration, false);
        throw new InvalidOperationException(
            $"Failed to generate script with Ollama at {_baseUrl} after {_maxRetries + 1} attempts. Please verify Ollama is running and model '{_model}' is available.",
            lastException);
    }

    public async Task<string> CompleteAsync(string prompt, CancellationToken ct)
    {
        _logger.LogInformation("Executing raw prompt completion with Ollama at {BaseUrl}", _baseUrl);

        for (int attempt = 0; attempt <= _maxRetries; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct).ConfigureAwait(false);
                }

                var requestBody = new
                {
                    model = _model,
                    prompt = prompt,
                    stream = false,
                    options = new
                    {
                        temperature = 0.7,
                        num_predict = 2048
                    }
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Apply provider timeout consistently with DraftScriptAsync
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(_timeout);

                var response = await _httpClient.PostAsync($"{_baseUrl}/api/generate", content, cts.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                // Use cts.Token for reading response to maintain consistent timeout
                var responseJson = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                var responseDoc = JsonDocument.Parse(responseJson);

                if (responseDoc.RootElement.TryGetProperty("response", out var responseProp))
                {
                    var result = responseProp.GetString() ?? string.Empty;
                    
                    // Validate response is not empty
                    if (string.IsNullOrWhiteSpace(result))
                    {
                        var responsePreview = string.IsNullOrEmpty(responseJson) 
                            ? "(empty)" 
                            : responseJson.Substring(0, Math.Min(500, responseJson.Length));
                        _logger.LogWarning("Ollama CompleteAsync returned empty response. Raw response: {Response}",
                            responsePreview);
                        throw new InvalidOperationException("Ollama returned an empty response for prompt completion");
                    }
                    
                    _logger.LogDebug("Ollama CompleteAsync succeeded with {Length} characters", result.Length);
                    return result;
                }

                _logger.LogError("Ollama response did not contain 'response' field. Available fields: {Fields}",
                    string.Join(", ", responseDoc.RootElement.EnumerateObject().Select(p => p.Name)));
                throw new InvalidOperationException("Invalid response structure from Ollama API");
            }
            catch (TaskCanceledException ex) when (attempt < _maxRetries)
            {
                _logger.LogWarning(ex, "Ollama CompleteAsync timed out (attempt {Attempt}/{MaxRetries})", attempt + 1, _maxRetries + 1);
            }
            catch (HttpRequestException ex) when (attempt < _maxRetries)
            {
                _logger.LogWarning(ex, "Ollama CompleteAsync connection failed (attempt {Attempt}/{MaxRetries})", attempt + 1, _maxRetries + 1);
            }
            catch (Exception ex) when (attempt < _maxRetries)
            {
                _logger.LogWarning(ex, "Error completing prompt with Ollama (attempt {Attempt}/{MaxRetries})", attempt + 1, _maxRetries + 1);
            }
        }

        throw new InvalidOperationException($"Failed to complete prompt with Ollama after {_maxRetries + 1} attempts.");
    }

    /// <summary>
    /// Generate a chat completion response for ideation, brainstorming, and other conversational tasks.
    /// Uses Ollama's chat API with system/user message pattern for more reliable JSON output.
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
            "Generating chat completion with Ollama (model: {Model}) at {BaseUrl}",
            modelToUse, _baseUrl);

        // Pre-check: Validate Ollama is available before attempting generation
        var isAvailable = await IsServiceAvailableAsync(ct).ConfigureAwait(false);
        if (!isAvailable)
        {
            var diagnosticMessage = await GetConnectionDiagnosticsAsync(ct).ConfigureAwait(false);
            var errorMessage = $"Cannot connect to Ollama at {_baseUrl}. {diagnosticMessage}";
            _logger.LogError("Ollama availability check failed. {ErrorMessage}", errorMessage);
            throw new InvalidOperationException(errorMessage);
        }

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

                // Build options with LLM parameters
                var temperature = parameters?.Temperature ?? 0.7;
                var maxTokens = parameters?.MaxTokens ?? 2048;
                var topP = parameters?.TopP ?? 0.9;
                var topK = parameters?.TopK;

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

                // Use Ollama chat API with messages format for better JSON handling
                var messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                };

                var requestBody = new
                {
                    model = modelToUse,
                    messages = messages,
                    stream = false,
                    format = "json", // Request JSON format from Ollama
                    options = options
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(_timeout);

                var response = await _httpClient.PostAsync($"{_baseUrl}/api/chat", content, cts.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(responseJson))
                {
                    _logger.LogError("Ollama chat API returned empty JSON response");
                    throw new InvalidOperationException("Ollama returned an empty JSON response");
                }

                var responseDoc = JsonDocument.Parse(responseJson);

                // Check for error field first
                if (responseDoc.RootElement.TryGetProperty("error", out var errorProp))
                {
                    var errorMessage = errorProp.GetString() ?? "Unknown error";
                    _logger.LogError("Ollama chat API returned error: {Error}", errorMessage);
                    throw new InvalidOperationException($"Ollama API error: {errorMessage}");
                }

                // Extract message content from chat response
                if (responseDoc.RootElement.TryGetProperty("message", out var messageProp) &&
                    messageProp.TryGetProperty("content", out var contentProp))
                {
                    var result = contentProp.GetString() ?? string.Empty;

                    if (string.IsNullOrWhiteSpace(result))
                    {
                        _logger.LogWarning("Ollama chat API returned empty message content. Response: {Response}",
                            responseJson.Substring(0, Math.Min(500, responseJson.Length)));
                        throw new InvalidOperationException("Ollama returned an empty message content");
                    }

                    _logger.LogInformation("Chat completion succeeded with {Length} characters", result.Length);
                    return result;
                }

                // Log available fields for debugging
                var availableFields = string.Join(", ", responseDoc.RootElement.EnumerateObject().Select(p => p.Name));
                _logger.LogError(
                    "Ollama chat response did not contain expected 'message.content' field. Available fields: {Fields}",
                    availableFields);
                throw new InvalidOperationException($"Invalid response structure from Ollama chat API. Available fields: {availableFields}");
            }
            catch (TaskCanceledException ex) when (attempt < _maxRetries)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Ollama chat completion timed out (attempt {Attempt}/{MaxRetries})",
                    attempt + 1, _maxRetries + 1);
            }
            catch (HttpRequestException ex) when (attempt < _maxRetries)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Ollama chat completion connection failed (attempt {Attempt}/{MaxRetries})",
                    attempt + 1, _maxRetries + 1);
            }
            catch (Exception ex) when (attempt < _maxRetries)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Error during chat completion with Ollama (attempt {Attempt}/{MaxRetries})",
                    attempt + 1, _maxRetries + 1);
            }
        }

        throw new InvalidOperationException(
            $"Failed to generate chat completion with Ollama after {_maxRetries + 1} attempts. " +
            $"Please ensure Ollama is running and model '{modelToUse}' is available.",
            lastException);
    }

    public async Task<SceneAnalysisResult?> AnalyzeSceneImportanceAsync(
        string sceneText,
        string? previousSceneText,
        string videoGoal,
        CancellationToken ct)
    {
        _logger.LogInformation("Analyzing scene importance with Ollama");

        try
        {
            // Build prompt for scene analysis
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

            var prompt = $"{systemPrompt}\n\n{userPrompt}";

            var requestBody = new
            {
                model = _model,
                prompt = prompt,
                stream = false,
                options = new
                {
                    temperature = 0.3, // Lower temperature for more consistent JSON
                    top_p = 0.9,
                    num_predict = 512
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30)); // Shorter timeout for analysis

            var response = await _httpClient.PostAsync($"{_baseUrl}/api/generate", content, cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            var responseDoc = JsonDocument.Parse(responseJson);

            if (responseDoc.RootElement.TryGetProperty("response", out var responseText))
            {
                var analysisText = responseText.GetString() ?? string.Empty;

                // Try to parse the JSON response
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
                    _logger.LogWarning(ex, "Failed to parse scene analysis JSON from Ollama: {Response}", analysisText);
                    return null;
                }
            }

            _logger.LogWarning("Ollama scene analysis response did not contain expected 'response' field");
            return null;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Ollama scene analysis request timed out");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to connect to Ollama for scene analysis");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing scene with Ollama");
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
        _logger.LogInformation("Generating visual prompt with Ollama");

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

            var prompt = $"{systemPrompt}\n\n{userPrompt}";

            var requestBody = new
            {
                model = _model,
                prompt = prompt,
                stream = false,
                options = new
                {
                    temperature = 0.7,
                    top_p = 0.9,
                    num_predict = 1024
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            var response = await _httpClient.PostAsync($"{_baseUrl}/api/generate", content, cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            var responseDoc = JsonDocument.Parse(responseJson);

            if (responseDoc.RootElement.TryGetProperty("response", out var responseText))
            {
                var promptText = responseText.GetString() ?? string.Empty;

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
                    _logger.LogWarning(ex, "Failed to parse visual prompt JSON from Ollama: {Response}", promptText);
                    return null;
                }
            }

            _logger.LogWarning("Ollama visual prompt response did not contain expected 'response' field");
            return null;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Ollama visual prompt request timed out");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to connect to Ollama for visual prompt");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating visual prompt with Ollama");
            return null;
        }
    }

    private static string[] ParseStringArray(JsonElement root, string propertyName)
    {
        if (root.TryGetProperty(propertyName, out var arrayProp) && arrayProp.ValueKind == JsonValueKind.Array)
        {
            var items = new List<string>();
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
        _logger.LogInformation("Analyzing content complexity with Ollama");

        var prompt = BuildComplexityAnalysisPrompt(sceneText, previousSceneText, videoGoal);

        try
        {
            var systemPrompt = "You are an expert in cognitive science and educational content analysis. " +
                              "Analyze the complexity of video content to optimize pacing for viewer comprehension. " +
                              "Return your response ONLY as valid JSON with no additional text.";

            var fullPrompt = $"{systemPrompt}\n\n{prompt}";

            var requestBody = new
            {
                model = _model,
                prompt = fullPrompt,
                stream = false,
                options = new
                {
                    temperature = 0.3,
                    top_p = 0.9,
                    num_predict = 512
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            var response = await _httpClient.PostAsync($"{_baseUrl}/api/generate", content, cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            var responseDoc = JsonDocument.Parse(responseJson);

            if (responseDoc.RootElement.TryGetProperty("response", out var responseText))
            {
                var messageContent = responseText.GetString();

                if (string.IsNullOrEmpty(messageContent))
                {
                    _logger.LogWarning("Ollama returned empty complexity analysis");
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

            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to connect to Ollama for complexity analysis");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing content complexity with Ollama");
            return null;
        }
    }

    private static string BuildComplexityAnalysisPrompt(string sceneText, string? previousSceneText, string videoGoal)
    {
        var prompt = new StringBuilder();
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

    public async Task<SceneCoherenceResult?> AnalyzeSceneCoherenceAsync(
        string fromSceneText,
        string toSceneText,
        string videoGoal,
        CancellationToken ct)
    {
        _logger.LogInformation("Analyzing scene coherence with Ollama");

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

            var prompt = $"{systemPrompt}\n\n{userPrompt}";

            var requestBody = new
            {
                model = _model,
                prompt = prompt,
                stream = false,
                options = new
                {
                    temperature = 0.3,
                    top_p = 0.9,
                    num_predict = 512
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            var response = await _httpClient.PostAsync($"{_baseUrl}/api/generate", content, cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            var responseDoc = JsonDocument.Parse(responseJson);

            if (responseDoc.RootElement.TryGetProperty("response", out var responseText))
            {
                var analysisText = responseText.GetString() ?? string.Empty;

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

            _logger.LogWarning("No valid response from Ollama for scene coherence analysis");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing scene coherence with Ollama");
            return null;
        }
    }

    public async Task<NarrativeArcResult?> ValidateNarrativeArcAsync(
        IReadOnlyList<string> sceneTexts,
        string videoGoal,
        string videoType,
        CancellationToken ct)
    {
        _logger.LogInformation("Validating narrative arc with Ollama for {VideoType} video", videoType);

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

            var prompt = $"{systemPrompt}\n\n{userPrompt}";

            var requestBody = new
            {
                model = _model,
                prompt = prompt,
                stream = false,
                options = new
                {
                    temperature = 0.3,
                    top_p = 0.9,
                    num_predict = 1024
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(45));

            var response = await _httpClient.PostAsync($"{_baseUrl}/api/generate", content, cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            var responseDoc = JsonDocument.Parse(responseJson);

            if (responseDoc.RootElement.TryGetProperty("response", out var responseText))
            {
                var analysisText = responseText.GetString() ?? string.Empty;

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

            _logger.LogWarning("No valid response from Ollama for narrative arc validation");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating narrative arc with Ollama");
            return null;
        }
    }

    public async Task<string?> GenerateTransitionTextAsync(
        string fromSceneText,
        string toSceneText,
        string videoGoal,
        CancellationToken ct)
    {
        _logger.LogInformation("Generating transition text with Ollama");

        try
        {
            var systemPrompt = "You are a professional scriptwriter specializing in smooth scene transitions.";

            var userPrompt = $@"Create a brief transition sentence or phrase (1-2 sentences maximum) to smoothly connect these two scenes:

Scene A: {fromSceneText}

Scene B: {toSceneText}

Video goal: {videoGoal}

The transition should feel natural and help the viewer understand the connection between these scenes.
Return ONLY the transition text, no explanations or additional commentary:";

            var prompt = $"{systemPrompt}\n\n{userPrompt}";

            var requestBody = new
            {
                model = _model,
                prompt = prompt,
                stream = false,
                options = new
                {
                    temperature = 0.7,
                    top_p = 0.9,
                    num_predict = 128
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            var response = await _httpClient.PostAsync($"{_baseUrl}/api/generate", content, cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            var responseDoc = JsonDocument.Parse(responseJson);

            if (responseDoc.RootElement.TryGetProperty("response", out var responseText))
            {
                var transitionText = responseText.GetString()?.Trim() ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(transitionText))
                {
                    _logger.LogInformation("Generated transition text: {Text}", transitionText);
                    return transitionText;
                }
            }

            _logger.LogWarning("No valid transition text from Ollama");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating transition text with Ollama");
            return null;
        }
    }

    // Removed legacy prompt building method - now using EnhancedPromptTemplates

    /// <summary>
    /// Check if Ollama service is available at the configured base URL
    /// Results are cached for 10 seconds to avoid repeated checks
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <param name="forceRefresh">If true, bypasses cache and performs a fresh check</param>
    public async Task<bool> IsServiceAvailableAsync(CancellationToken ct = default, bool forceRefresh = false)
    {
        // Check if parent token is already cancelled before proceeding
        if (ct.IsCancellationRequested)
        {
            _logger.LogDebug("Availability check cancelled before starting (parent token already cancelled)");
            return false;
        }

        // Check cache first to avoid repeated availability checks (unless force refresh is requested)
        if (!forceRefresh)
        {
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
        }
        else
        {
            _logger.LogDebug("Force refresh requested, bypassing availability cache");
        }

        try
        {
            _logger.LogInformation("Checking Ollama service availability at {BaseUrl}", _baseUrl);

            // Create a new CancellationTokenSource that is NOT linked to the parent ct
            // This prevents premature cancellation if the parent token has a short timeout
            // The availability check needs its own independent timeout
            // However, we still monitor the parent token and exit early if it's cancelled
            using var cts = new CancellationTokenSource();
            // Increased timeout to 15 seconds to account for slow startup or model loading
            cts.CancelAfter(TimeSpan.FromSeconds(15));

            // Try /api/version first (lightweight endpoint)
            try
            {
                // Check parent cancellation before making request
                if (ct.IsCancellationRequested)
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
                _logger.LogInformation("Ollama /api/version endpoint timed out after 15s, trying /api/tags as fallback. Inner exception: {InnerException}", ex.InnerException?.Message);
            }
            catch (HttpRequestException ex)
            {
                // Log detailed connection error information with improved categorization
                var innerException = ex.InnerException;
                var errorCategory = CategorizeConnectionError(ex, innerException);
                var errorDetails = $"HttpRequestException: {ex.Message}";
                if (innerException != null)
                {
                    errorDetails += $", InnerException: {innerException.GetType().Name} - {innerException.Message}";
                }
                _logger.LogInformation("Ollama /api/version endpoint failed ({Category}): {ErrorDetails}, trying /api/tags as fallback",
                    errorCategory, errorDetails);
            }
            catch (Exception ex)
            {
                _logger.LogInformation("Ollama /api/version endpoint error: {ExceptionType} - {Message}, trying /api/tags as fallback", ex.GetType().Name, ex.Message);
            }

            // Fallback to /api/tags if version endpoint failed for any reason (timeout, error status, or exception)
            try
            {
                // Check parent cancellation before fallback attempt
                if (ct.IsCancellationRequested)
                {
                    _logger.LogDebug("Availability check cancelled by parent token before /api/tags fallback");
                    return false;
                }

                using var fallbackCts = new CancellationTokenSource();
                fallbackCts.CancelAfter(TimeSpan.FromSeconds(10));
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
                _logger.LogWarning("Ollama /api/tags fallback endpoint timed out after 10s. Inner exception: {InnerException}", ex.InnerException?.Message);
            }
            catch (HttpRequestException ex)
            {
                // Log detailed connection error information with improved categorization
                var innerException = ex.InnerException;
                var errorCategory = CategorizeConnectionError(ex, innerException);
                var errorDetails = $"HttpRequestException: {ex.Message}";
                if (innerException != null)
                {
                    errorDetails += $", InnerException: {innerException.GetType().Name} - {innerException.Message}";
                }
                _logger.LogWarning("Ollama /api/tags fallback endpoint failed ({Category}): {ErrorDetails}",
                    errorCategory, errorDetails);
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
    /// Invalidates the availability cache, forcing the next check to perform a fresh availability test
    /// </summary>
    public void InvalidateAvailabilityCache()
    {
        lock (_availabilityCacheLock)
        {
            _lastAvailabilityCheck = DateTime.MinValue;
            _lastAvailabilityResult = false;
            _logger.LogDebug("Ollama availability cache invalidated");
        }
    }

    /// <summary>
    /// Categorizes connection errors for better diagnostics
    /// </summary>
    private static string CategorizeConnectionError(HttpRequestException ex, Exception? innerException)
    {
        if (innerException != null)
        {
            var innerMessage = innerException.Message;
            if (innerMessage.Contains("No connection could be made", StringComparison.OrdinalIgnoreCase) ||
                innerMessage.Contains("Connection refused", StringComparison.OrdinalIgnoreCase) ||
                innerMessage.Contains("actively refused", StringComparison.OrdinalIgnoreCase))
            {
                return "ConnectionRefused";
            }

            if (innerMessage.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                innerMessage.Contains("timed out", StringComparison.OrdinalIgnoreCase))
            {
                return "Timeout";
            }

            if (innerMessage.Contains("name resolution", StringComparison.OrdinalIgnoreCase) ||
                innerMessage.Contains("DNS", StringComparison.OrdinalIgnoreCase))
            {
                return "DnsResolution";
            }
        }

        if (ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            return "Timeout";
        }

        return "Unknown";
    }

    /// <summary>
    /// Get list of available models from Ollama
    /// </summary>
    public async Task<List<OllamaModelInfo>> GetAvailableModelsAsync(CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Fetching available Ollama models from {BaseUrl}/api/tags", _baseUrl);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            var response = await _httpClient.GetAsync($"{_baseUrl}/api/tags", cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            var tagsDoc = JsonDocument.Parse(content);

            var models = new List<OllamaModelInfo>();

            if (tagsDoc.RootElement.TryGetProperty("models", out var modelsArray) &&
                modelsArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var modelElement in modelsArray.EnumerateArray())
                {
                    var name = modelElement.TryGetProperty("name", out var nameProp)
                        ? nameProp.GetString() ?? ""
                        : "";

                    var size = modelElement.TryGetProperty("size", out var sizeProp)
                        ? sizeProp.GetInt64()
                        : 0;

                    var modifiedAt = modelElement.TryGetProperty("modified_at", out var modifiedProp)
                        ? modifiedProp.GetString()
                        : null;

                    if (!string.IsNullOrEmpty(name))
                    {
                        DateTime? parsedDate = null;
                        if (!string.IsNullOrEmpty(modifiedAt) && DateTime.TryParse(modifiedAt, out var dt))
                        {
                            parsedDate = dt;
                        }

                        models.Add(new OllamaModelInfo
                        {
                            Name = name,
                            Size = size,
                            Modified = parsedDate
                        });
                    }
                }
            }

            _logger.LogInformation("Found {Count} Ollama models", models.Count);
            return models;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Ollama models from {BaseUrl}", _baseUrl);
            return new List<OllamaModelInfo>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Ollama models from {BaseUrl}", _baseUrl);
            return new List<OllamaModelInfo>();
        }
    }

    /// <summary>
    /// Get connection diagnostics information
    /// </summary>
    private async Task<string> GetConnectionDiagnosticsAsync(CancellationToken ct)
    {
        var diagnostics = new List<string>
        {
            "Please ensure Ollama is running.",
            "Installation: Visit https://ollama.com to download and install.",
            "Start service: Run 'ollama serve' in a terminal."
        };

        try
        {
            var endpoints = new[] { "http://localhost:11434", "http://127.0.0.1:11434" };
            var checkedEndpoints = new List<string>();

            foreach (var endpoint in endpoints)
            {
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(TimeSpan.FromSeconds(2));

                    var response = await _httpClient.GetAsync($"{endpoint}/api/version", cts.Token).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        diagnostics.Add($"Note: Ollama detected at {endpoint} but not at {_baseUrl}. Check your base URL configuration.");
                        break;
                    }
                    checkedEndpoints.Add(endpoint);
                }
                catch
                {
                    checkedEndpoints.Add(endpoint);
                }
            }

            if (checkedEndpoints.Count > 0)
            {
                diagnostics.Add($"Checked endpoints: {string.Join(", ", checkedEndpoints)}");
            }
        }
        catch
        {
            // Diagnostics check failed, return basic message
        }

        return string.Join(" ", diagnostics);
    }

    /// <summary>
    /// Generate script with proper error handling for missing models
    /// </summary>
    public async Task<Script> GenerateScriptAsync(Brief brief, PlanSpec spec, CancellationToken ct)
    {
        _logger.LogInformation("Generating script with Ollama (model: {Model}) at {BaseUrl} for topic: {Topic}",
            _model, _baseUrl, brief.Topic);

        var startTime = DateTime.UtcNow;

        try
        {
            // Build enhanced prompt for quality content with user customizations
            string systemPrompt = EnhancedPromptTemplates.GetSystemPromptForScriptGeneration();
            string userPrompt = await _promptCustomizationService.BuildCustomizedPromptAsync(brief, spec, brief.PromptModifiers, ct).ConfigureAwait(false);

            // Apply enhancement callback if configured
            if (PromptEnhancementCallback != null)
            {
                userPrompt = await PromptEnhancementCallback(userPrompt, brief, spec).ConfigureAwait(false);
            }

            string prompt = $"{systemPrompt}\n\n{userPrompt}";

            // Call Ollama API with JSON format
            var requestBody = new
            {
                model = _model,
                prompt = prompt,
                stream = false,
                format = "json",
                options = new
                {
                    temperature = 0.7,
                    top_p = 0.9,
                    num_predict = 2048
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_timeout); // Use configured timeout (default 300s for slow local models)

            var response = await _httpClient.PostAsync($"{_baseUrl}/api/generate", content, cts.Token).ConfigureAwait(false);

            // Check for model not found error
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                if (errorContent.Contains("model") && errorContent.Contains("not found"))
                {
                    throw new InvalidOperationException(
                        $"Model '{_model}' not found. Please pull the model first using: ollama pull {_model}");
                }
                response.EnsureSuccessStatusCode();
            }

            var responseJson = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            var responseDoc = JsonDocument.Parse(responseJson);

            if (responseDoc.RootElement.TryGetProperty("response", out var responseText))
            {
                string scriptText = responseText.GetString() ?? string.Empty;
                var duration = DateTime.UtcNow - startTime;

                _logger.LogInformation("Script generated successfully with Ollama ({Length} characters) in {Duration}s",
                    scriptText.Length, duration.TotalSeconds);

                // Track performance if callback configured
                PerformanceTrackingCallback?.Invoke(75.0, duration, true);

                // Parse script into structured scenes
                var scenes = ParseScriptIntoScenes(scriptText, spec);

                return new Script
                {
                    Title = brief.Topic,
                    Scenes = scenes,
                    TotalDuration = scenes.Count > 0
                        ? TimeSpan.FromSeconds(scenes.Sum(s => s.Duration.TotalSeconds))
                        : spec.TargetDuration,
                    Metadata = new ScriptMetadata
                    {
                        ProviderName = "Ollama",
                        ModelUsed = _model,
                        GenerationTime = duration,
                        TokensUsed = EstimateTokenCount(scriptText),
                        Tier = ProviderTier.Free
                    }
                };
            }

            _logger.LogWarning("Ollama response did not contain expected 'response' field");
            throw new Exception("Invalid response from Ollama");
        }
        catch (TaskCanceledException ex)
        {
            var duration = DateTime.UtcNow - startTime;
            PerformanceTrackingCallback?.Invoke(0, duration, false);
            _logger.LogError(ex, "Ollama request timed out after 120 seconds");
            throw new InvalidOperationException(
                $"Ollama request timed out after 120s. The model '{_model}' may be loading or Ollama may be overloaded.", ex);
        }
        catch (HttpRequestException ex)
        {
            var duration = DateTime.UtcNow - startTime;
            PerformanceTrackingCallback?.Invoke(0, duration, false);
            _logger.LogError(ex, "Failed to connect to Ollama at {BaseUrl}", _baseUrl);
            throw new InvalidOperationException(
                $"Cannot connect to Ollama at {_baseUrl}. Please ensure Ollama is running: 'ollama serve'", ex);
        }
    }

    private List<ScriptScene> ParseScriptIntoScenes(string scriptText, PlanSpec spec)
    {
        // Simple scene parsing - split by newlines and create scenes
        var scenes = new List<ScriptScene>();
        var lines = scriptText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        int sceneNumber = 1;
        foreach (var line in lines)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                scenes.Add(new ScriptScene
                {
                    Number = sceneNumber++,
                    Narration = line.Trim(),
                    Duration = TimeSpan.FromSeconds(5),
                    Transition = TransitionType.Cut
                });
            }
        }

        if (scenes.Count == 0)
        {
            scenes.Add(new ScriptScene
            {
                Number = 1,
                Narration = scriptText,
                Duration = spec.TargetDuration,
                Transition = TransitionType.Cut
            });
        }

        return scenes;
    }

    private int EstimateTokenCount(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }
        return (int)Math.Ceiling(text.Length / 4.0);
    }

    /// <summary>
    /// Generate script with streaming support for real-time token-by-token updates
    /// </summary>
    public async IAsyncEnumerable<OllamaStreamResponse> GenerateStreamingAsync(
        Brief brief,
        PlanSpec spec,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _logger.LogInformation("Starting streaming generation with Ollama (model: {Model}) for topic: {Topic}",
            _model, brief.Topic);

        var isAvailable = await IsServiceAvailableAsync(ct).ConfigureAwait(false);
        if (!isAvailable)
        {
            var diagnosticMessage = await GetConnectionDiagnosticsAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Cannot connect to Ollama at {_baseUrl}. {diagnosticMessage}");
        }

        string systemPrompt = EnhancedPromptTemplates.GetSystemPromptForScriptGeneration();
        string userPrompt = await _promptCustomizationService.BuildCustomizedPromptAsync(brief, spec, brief.PromptModifiers, ct).ConfigureAwait(false);

        if (PromptEnhancementCallback != null)
        {
            userPrompt = await PromptEnhancementCallback(userPrompt, brief, spec).ConfigureAwait(false);
        }

        string prompt = $"{systemPrompt}\n\n{userPrompt}";

        var requestBody = new
        {
            model = _model,
            prompt = prompt,
            stream = true,
            options = new
            {
                temperature = 0.7,
                top_p = 0.9,
                num_predict = 2048
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/generate")
        {
            Content = content
        };

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to connect to Ollama at {BaseUrl}", _baseUrl);
            throw new InvalidOperationException(
                $"Cannot connect to Ollama at {_baseUrl}. Please ensure Ollama is running: 'ollama serve'", ex);
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("Streaming cancelled by user");
            throw;
        }

        using (response)
        {
            var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var reader = new StreamReader(stream);

            var cumulativeTokens = 0;

            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                OllamaStreamResponse? chunk = null;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    var model = root.TryGetProperty("model", out var modelProp)
                        ? modelProp.GetString() ?? _model
                        : _model;

                    var createdAt = root.TryGetProperty("created_at", out var createdAtProp)
                        ? createdAtProp.GetString() ?? string.Empty
                        : string.Empty;

                    var responseText = root.TryGetProperty("response", out var responseProp)
                        ? responseProp.GetString() ?? string.Empty
                        : string.Empty;

                    var done = root.TryGetProperty("done", out var doneProp) && doneProp.GetBoolean();

                    long? totalDuration = null;
                    long? loadDuration = null;
                    int? promptEvalCount = null;
                    long? promptEvalDuration = null;
                    int? evalCount = null;
                    long? evalDuration = null;

                    if (done)
                    {
                        if (root.TryGetProperty("total_duration", out var totalDurProp) && totalDurProp.ValueKind == JsonValueKind.Number)
                        {
                            totalDuration = totalDurProp.GetInt64();
                        }

                        if (root.TryGetProperty("load_duration", out var loadDurProp) && loadDurProp.ValueKind == JsonValueKind.Number)
                        {
                            loadDuration = loadDurProp.GetInt64();
                        }

                        if (root.TryGetProperty("prompt_eval_count", out var promptEvalCountProp) && promptEvalCountProp.ValueKind == JsonValueKind.Number)
                        {
                            promptEvalCount = promptEvalCountProp.GetInt32();
                        }

                        if (root.TryGetProperty("prompt_eval_duration", out var promptEvalDurProp) && promptEvalDurProp.ValueKind == JsonValueKind.Number)
                        {
                            promptEvalDuration = promptEvalDurProp.GetInt64();
                        }

                        if (root.TryGetProperty("eval_count", out var evalCountProp) && evalCountProp.ValueKind == JsonValueKind.Number)
                        {
                            evalCount = evalCountProp.GetInt32();
                        }

                        if (root.TryGetProperty("eval_duration", out var evalDurProp) && evalDurProp.ValueKind == JsonValueKind.Number)
                        {
                            evalDuration = evalDurProp.GetInt64();
                        }

                        _logger.LogInformation("Streaming complete. Total tokens: {Tokens}, Tokens/sec: {TokensPerSec:F2}",
                            evalCount ?? 0,
                            evalCount.HasValue && evalDuration.HasValue && evalDuration.Value > 0
                                ? (double)evalCount.Value / (evalDuration.Value / 1_000_000_000.0)
                                : 0.0);
                    }

                    if (!string.IsNullOrEmpty(responseText))
                    {
                        cumulativeTokens++;
                    }

                    chunk = new OllamaStreamResponse
                    {
                        Model = model,
                        CreatedAt = createdAt,
                        Response = responseText,
                        Done = done,
                        TotalDuration = totalDuration,
                        LoadDuration = loadDuration,
                        PromptEvalCount = promptEvalCount,
                        PromptEvalDuration = promptEvalDuration,
                        EvalCount = done ? evalCount : cumulativeTokens,
                        EvalDuration = evalDuration
                    };
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse streaming JSON line: {Line}", line);
                    continue;
                }

                if (chunk != null)
                {
                    yield return chunk;

                    if (chunk.Done)
                    {
                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Generate script with tool calling support for enhanced content with research and fact-checking
    /// </summary>
    public async Task<ToolCallingResult> GenerateWithToolsAsync(
        Brief brief,
        PlanSpec spec,
        List<Core.AI.Tools.IToolExecutor> tools,
        int maxToolIterations = 5,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Starting tool-enabled generation for topic: {Topic} with {ToolCount} tools",
            brief.Topic, tools.Count);

        var startTime = DateTime.UtcNow;
        var conversationHistory = new List<OllamaMessageWithToolCalls>();
        var toolExecutionLog = new List<ToolExecutionEntry>();
        var totalToolCalls = 0;

        try
        {
            string systemPrompt = EnhancedPromptTemplates.GetSystemPromptForScriptGeneration();
            string userPrompt = await _promptCustomizationService.BuildCustomizedPromptAsync(brief, spec, brief.PromptModifiers, ct).ConfigureAwait(false);

            if (PromptEnhancementCallback != null)
            {
                userPrompt = await PromptEnhancementCallback(userPrompt, brief, spec).ConfigureAwait(false);
            }

            conversationHistory.Add(new OllamaMessageWithToolCalls
            {
                Role = "user",
                Content = $"{systemPrompt}\n\n{userPrompt}"
            });

            for (int iteration = 0; iteration < maxToolIterations; iteration++)
            {
                _logger.LogInformation("Tool iteration {Iteration}/{MaxIterations}", iteration + 1, maxToolIterations);

                var toolDefinitions = tools.Select(t => t.GetToolDefinition()).ToList();

                var requestBody = new
                {
                    model = _model,
                    messages = conversationHistory.Select(m => new
                    {
                        role = m.Role,
                        content = m.Content ?? string.Empty
                    }).ToList(),
                    tools = toolDefinitions,
                    stream = false,
                    options = new
                    {
                        temperature = 0.7,
                        top_p = 0.9,
                        num_predict = 2048
                    }
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(_timeout);

                var response = await _httpClient.PostAsync($"{_baseUrl}/api/chat", content, cts.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                var responseDoc = JsonDocument.Parse(responseJson);

                if (!responseDoc.RootElement.TryGetProperty("message", out var messageElement))
                {
                    throw new InvalidOperationException("Response does not contain 'message' property");
                }

                var assistantMessage = JsonSerializer.Deserialize<OllamaMessageWithToolCalls>(
                    messageElement.GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (assistantMessage == null)
                {
                    throw new InvalidOperationException("Failed to deserialize assistant message");
                }

                conversationHistory.Add(assistantMessage);

                if (assistantMessage.ToolCalls == null || assistantMessage.ToolCalls.Count == 0)
                {
                    _logger.LogInformation("No tool calls requested. Generation complete.");

                    var finalScript = assistantMessage.Content ?? string.Empty;
                    var duration = DateTime.UtcNow - startTime;

                    PerformanceTrackingCallback?.Invoke(85.0, duration, true);

                    return new ToolCallingResult
                    {
                        Success = true,
                        GeneratedScript = finalScript,
                        ToolExecutionLog = toolExecutionLog,
                        TotalToolCalls = totalToolCalls,
                        TotalIterations = iteration + 1,
                        GenerationTime = duration
                    };
                }

                _logger.LogInformation("Processing {Count} tool call(s)", assistantMessage.ToolCalls.Count);
                totalToolCalls += assistantMessage.ToolCalls.Count;

                foreach (var toolCall in assistantMessage.ToolCalls)
                {
                    var tool = tools.FirstOrDefault(t => t.Name == toolCall.Function.Name);

                    if (tool == null)
                    {
                        _logger.LogWarning("Unknown tool requested: {ToolName}", toolCall.Function.Name);
                        continue;
                    }

                    _logger.LogInformation("Executing tool: {ToolName} with args: {Arguments}",
                        toolCall.Function.Name, toolCall.Function.Arguments);

                    var executionStart = DateTime.UtcNow;
                    var toolResult = await tool.ExecuteAsync(toolCall.Function.Arguments, ct).ConfigureAwait(false);
                    var executionDuration = DateTime.UtcNow - executionStart;

                    toolExecutionLog.Add(new ToolExecutionEntry
                    {
                        ToolName = tool.Name,
                        Arguments = toolCall.Function.Arguments,
                        Result = toolResult,
                        ExecutionTime = executionDuration,
                        Timestamp = DateTime.UtcNow
                    });

                    _logger.LogInformation("Tool {ToolName} executed in {Duration}ms",
                        tool.Name, executionDuration.TotalMilliseconds);

                    conversationHistory.Add(new OllamaMessageWithToolCalls
                    {
                        Role = "tool",
                        Content = $"Tool: {tool.Name}\nResult: {toolResult}"
                    });
                }
            }

            _logger.LogWarning("Reached maximum tool iterations ({MaxIterations}) without completion",
                maxToolIterations);

            var lastMessage = conversationHistory.LastOrDefault(m => m.Role == "assistant");
            var scriptContent = lastMessage?.Content ?? "Generation incomplete after maximum iterations";

            return new ToolCallingResult
            {
                Success = false,
                GeneratedScript = scriptContent,
                ToolExecutionLog = toolExecutionLog,
                TotalToolCalls = totalToolCalls,
                TotalIterations = maxToolIterations,
                GenerationTime = DateTime.UtcNow - startTime,
                ErrorMessage = "Maximum tool calling iterations reached"
            };
        }
        catch (TaskCanceledException ex)
        {
            var duration = DateTime.UtcNow - startTime;
            PerformanceTrackingCallback?.Invoke(0, duration, false);
            _logger.LogError(ex, "Tool-enabled generation timed out");

            return new ToolCallingResult
            {
                Success = false,
                ErrorMessage = "Generation timed out",
                ToolExecutionLog = toolExecutionLog,
                TotalToolCalls = totalToolCalls,
                GenerationTime = duration
            };
        }
        catch (HttpRequestException ex)
        {
            var duration = DateTime.UtcNow - startTime;
            PerformanceTrackingCallback?.Invoke(0, duration, false);
            _logger.LogError(ex, "Failed to connect to Ollama for tool-enabled generation");

            return new ToolCallingResult
            {
                Success = false,
                ErrorMessage = $"Cannot connect to Ollama at {_baseUrl}",
                ToolExecutionLog = toolExecutionLog,
                TotalToolCalls = totalToolCalls,
                GenerationTime = duration
            };
        }
        catch (Exception ex)
        {
            var duration = DateTime.UtcNow - startTime;
            PerformanceTrackingCallback?.Invoke(0, duration, false);
            _logger.LogError(ex, "Error during tool-enabled generation");

            return new ToolCallingResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                ToolExecutionLog = toolExecutionLog,
                TotalToolCalls = totalToolCalls,
                GenerationTime = duration
            };
        }
    }

    /// <summary>
    /// Whether this provider supports streaming (Ollama supports streaming)
    /// </summary>
    public bool SupportsStreaming => true;

    /// <summary>
    /// Get provider characteristics for adaptive UI
    /// </summary>
    public LlmProviderCharacteristics GetCharacteristics()
    {
        return new LlmProviderCharacteristics
        {
            IsLocal = true,
            ExpectedFirstTokenMs = 2000, // Model loading time
            ExpectedTokensPerSec = 5, // Consumer hardware average
            SupportsStreaming = true,
            ProviderTier = "Free",
            CostPer1KTokens = null // Free!
        };
    }

    /// <summary>
    /// Stream script generation with unified interface (wraps existing GenerateStreamingAsync)
    /// </summary>
    public async IAsyncEnumerable<LlmStreamChunk> DraftScriptStreamAsync(
        Brief brief,
        PlanSpec spec,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _logger.LogInformation("Starting unified streaming script generation with Ollama for topic: {Topic}", brief.Topic);

        var startTime = DateTime.UtcNow;
        DateTime? firstTokenTime = null;
        var accumulated = new StringBuilder();
        var tokenIndex = 0;
        LlmStreamChunk? errorChunk = null;

        var streamEnumerator = GenerateStreamingAsync(brief, spec, ct).ConfigureAwait(false).GetAsyncEnumerator();

        try
        {
            while (true)
            {
                OllamaStreamResponse ollamaChunk;
                try
                {
                    if (!await streamEnumerator.MoveNextAsync())
                    {
                        break;
                    }
                    ollamaChunk = streamEnumerator.Current;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during unified streaming script generation");
                    errorChunk = new LlmStreamChunk
                    {
                        ProviderName = "Ollama",
                        Content = string.Empty,
                        TokenIndex = tokenIndex,
                        IsFinal = true,
                        ErrorMessage = $"Streaming error: {ex.Message}"
                    };
                    break;
                }

                if (!string.IsNullOrEmpty(ollamaChunk.Response))
                {
                    if (!firstTokenTime.HasValue)
                    {
                        firstTokenTime = DateTime.UtcNow;
                    }

                    accumulated.Append(ollamaChunk.Response);
                    tokenIndex++;

                    yield return new LlmStreamChunk
                    {
                        ProviderName = "Ollama",
                        Content = ollamaChunk.Response,
                        AccumulatedContent = accumulated.ToString(),
                        TokenIndex = tokenIndex,
                        IsFinal = false
                    };
                }

                if (ollamaChunk.Done)
                {
                    var duration = DateTime.UtcNow - startTime;
                    var timeToFirstToken = firstTokenTime.HasValue
                        ? (firstTokenTime.Value - startTime).TotalMilliseconds
                        : 0;
                    var tokensPerSec = ollamaChunk.GetTokensPerSecond() ?? 0.0;

                    yield return new LlmStreamChunk
                    {
                        ProviderName = "Ollama",
                        Content = string.Empty,
                        AccumulatedContent = accumulated.ToString(),
                        TokenIndex = ollamaChunk.EvalCount ?? tokenIndex,
                        IsFinal = true,
                        Metadata = new LlmStreamMetadata
                        {
                            TotalTokens = ollamaChunk.EvalCount ?? tokenIndex,
                            EstimatedCost = 0m, // Free!
                            TokensPerSecond = tokensPerSec,
                            IsLocalModel = true,
                            ModelName = _model,
                            TimeToFirstTokenMs = timeToFirstToken,
                            TotalDurationMs = ollamaChunk.TotalDuration.HasValue
                                ? ollamaChunk.TotalDuration.Value / 1_000_000.0
                                : duration.TotalMilliseconds,
                            FinishReason = "stop"
                        }
                    };
                    break;
                }
            }
        }
        finally
        {
            await streamEnumerator.DisposeAsync();
        }

        if (errorChunk != null)
        {
            yield return errorChunk;
        }
    }
}

/// <summary>
/// Information about an Ollama model
/// </summary>
public class OllamaModelInfo
{
    public string Name { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime? Modified { get; set; }
}

/// <summary>
/// Result of tool-enabled script generation
/// </summary>
public class ToolCallingResult
{
    public bool Success { get; set; }
    public string GeneratedScript { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public List<ToolExecutionEntry> ToolExecutionLog { get; set; } = new();
    public int TotalToolCalls { get; set; }
    public int TotalIterations { get; set; }
    public TimeSpan GenerationTime { get; set; }
}

/// <summary>
/// Log entry for a single tool execution
/// </summary>
public class ToolExecutionEntry
{
    public string ToolName { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public TimeSpan ExecutionTime { get; set; }
    public DateTime Timestamp { get; set; }
}
