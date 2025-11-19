using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aura.Core.AI;
using Aura.Core.Models;
using Aura.Core.Models.Narrative;
using Aura.Core.Models.Streaming;
using Aura.Core.Models.Visual;
using Aura.Core.Providers;
using Aura.Core.Services.AI;
using Microsoft.Extensions.Logging;

namespace Aura.Providers.Llm;

/// <summary>
/// LLM provider that uses Anthropic Claude API for script generation (Pro feature).
/// Supports prompt customization and Constitutional AI principles.
/// </summary>
public class AnthropicLlmProvider : ILlmProvider
{
    private readonly ILogger<AnthropicLlmProvider> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly int _maxRetries;
    private readonly TimeSpan _timeout;
    private readonly PromptCustomizationService _promptCustomizationService;

    public AnthropicLlmProvider(
        ILogger<AnthropicLlmProvider> logger,
        HttpClient httpClient,
        string apiKey,
        string model = "claude-3-5-sonnet-20241022",
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
        
        // Create PromptCustomizationService if not provided
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
                "Anthropic API key is not configured. Please add your API key in Settings → Providers → Anthropic",
                nameof(_apiKey));
        }

        // Allow test/mock keys for testing
        if (_apiKey.StartsWith("test-", StringComparison.OrdinalIgnoreCase) ||
            _apiKey.StartsWith("mock-", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Anthropic API keys should start with "sk-ant-" and be reasonably long
        if (!_apiKey.StartsWith("sk-ant-", StringComparison.Ordinal) || _apiKey.Length < 40)
        {
            throw new ArgumentException(
                "Anthropic API key format appears invalid. Please check your API key in Settings → Providers → Anthropic",
                nameof(_apiKey));
        }
    }

    public async Task<string> DraftScriptAsync(Brief brief, PlanSpec spec, CancellationToken ct)
    {
        _logger.LogInformation("Generating high-quality script with Anthropic Claude (model: {Model}) for topic: {Topic}", _model, brief.Topic);

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
                string userPrompt = _promptCustomizationService.BuildCustomizedPrompt(brief, spec, brief.PromptModifiers);

                // Anthropic uses separate system parameter (not in messages)
                var requestBody = new
                {
                    model = _model,
                    max_tokens = 4096,
                    temperature = 0.8,
                    top_p = 0.95,
                    system = systemPrompt,
                    messages = new[]
                    {
                        new { role = "user", content = userPrompt }
                    },
                    stop_sequences = new[] { "\n\nHuman:", "\n\nAssistant:" }
                };

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
                _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(_timeout);

                var response = await _httpClient.PostAsync("https://api.anthropic.com/v1/messages", content, cts.Token).ConfigureAwait(false);
                
                // Handle specific HTTP error codes
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized || 
                        response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        throw new InvalidOperationException(
                            "Anthropic API key is invalid or has been revoked. Please check your API key in Settings → Providers → Anthropic");
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        _logger.LogWarning("Anthropic rate limit exceeded (attempt {Attempt}/{MaxRetries})", attempt + 1, _maxRetries + 1);
                        if (attempt >= _maxRetries)
                        {
                            throw new InvalidOperationException(
                                "Anthropic rate limit exceeded. Please wait a moment and try again, or upgrade your Anthropic plan.");
                        }
                        lastException = new Exception($"Rate limit exceeded: {errorContent}");
                        continue; // Retry
                    }
                    else if ((int)response.StatusCode == 529)
                    {
                        _logger.LogWarning("Anthropic service overloaded (attempt {Attempt}/{MaxRetries})", attempt + 1, _maxRetries + 1);
                        if (attempt >= _maxRetries)
                        {
                            throw new InvalidOperationException(
                                "Anthropic service is currently overloaded. Please try again in a few minutes.");
                        }
                        lastException = new Exception($"Service overloaded: {errorContent}");
                        continue; // Retry
                    }
                    else if ((int)response.StatusCode >= 500)
                    {
                        _logger.LogWarning("Anthropic server error (attempt {Attempt}/{MaxRetries}): {StatusCode}", 
                            attempt + 1, _maxRetries + 1, response.StatusCode);
                        if (attempt >= _maxRetries)
                        {
                            throw new InvalidOperationException(
                                $"Anthropic service is experiencing issues (HTTP {response.StatusCode}). Please try again later.");
                        }
                        lastException = new Exception($"Server error: {errorContent}");
                        continue; // Retry
                    }
                    
                    response.EnsureSuccessStatusCode();
                }

                var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var responseDoc = JsonDocument.Parse(responseJson);

                if (responseDoc.RootElement.TryGetProperty("content", out var contentArray) &&
                    contentArray.GetArrayLength() > 0)
                {
                    var firstContent = contentArray[0];
                    if (firstContent.TryGetProperty("text", out var textProp))
                    {
                        string script = textProp.GetString() ?? string.Empty;
                        
                        if (string.IsNullOrWhiteSpace(script))
                        {
                            throw new InvalidOperationException("Anthropic returned an empty response");
                        }
                        
                        var duration = DateTime.UtcNow - startTime;
                        
                        _logger.LogInformation("Script generated successfully ({Length} characters) in {Duration}s", 
                            script.Length, duration.TotalSeconds);
                        
                        return script;
                    }
                }

                _logger.LogWarning("Anthropic response did not contain expected structure");
                throw new InvalidOperationException("Invalid response structure from Anthropic API");
            }
            catch (TaskCanceledException ex)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Anthropic request timed out (attempt {Attempt}/{MaxRetries})", attempt + 1, _maxRetries + 1);
                if (attempt >= _maxRetries)
                {
                    throw new InvalidOperationException(
                        $"Anthropic request timed out after {_timeout.TotalSeconds}s. Please check your internet connection or try again later.", ex);
                }
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Failed to connect to Anthropic API (attempt {Attempt}/{MaxRetries})", attempt + 1, _maxRetries + 1);
                if (attempt >= _maxRetries)
                {
                    throw new InvalidOperationException(
                        "Cannot connect to Anthropic API. Please check your internet connection and try again.", ex);
                }
            }
            catch (InvalidOperationException)
            {
                // Don't retry on validation errors or known issues
                throw;
            }
            catch (Exception ex) when (attempt < _maxRetries)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Error generating script with Anthropic (attempt {Attempt}/{MaxRetries})", attempt + 1, _maxRetries + 1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating script with Anthropic after all retries");
                throw;
            }
        }

        // Should not reach here, but just in case
        throw new InvalidOperationException(
            $"Failed to generate script with Anthropic after {_maxRetries + 1} attempts. Please try again later.", lastException);
    }

    public async Task<string> CompleteAsync(string prompt, CancellationToken ct)
    {
        _logger.LogInformation("Executing raw prompt completion with Anthropic Claude (model: {Model})", _model);

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

                var requestBody = new
                {
                    model = _model,
                    max_tokens = 2048,
                    temperature = 0.7,
                    messages = new[]
                    {
                        new { role = "user", content = prompt }
                    }
                };

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
                _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(_timeout);

                var response = await _httpClient.PostAsync("https://api.anthropic.com/v1/messages", content, cts.Token).ConfigureAwait(false);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        throw new InvalidOperationException(
                            "Anthropic API key is invalid or has been revoked. Please check your API key.");
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        if (attempt >= _maxRetries)
                        {
                            throw new InvalidOperationException("Anthropic rate limit exceeded. Please wait a moment and try again.");
                        }
                        lastException = new Exception($"Rate limit exceeded: {errorContent}");
                        continue;
                    }
                    else if ((int)response.StatusCode >= 500)
                    {
                        if (attempt >= _maxRetries)
                        {
                            throw new InvalidOperationException($"Anthropic service is experiencing issues (HTTP {response.StatusCode}). Please try again later.");
                        }
                        lastException = new Exception($"Server error: {errorContent}");
                        continue;
                    }
                    
                    response.EnsureSuccessStatusCode();
                }

                var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var responseDoc = JsonDocument.Parse(responseJson);

                if (responseDoc.RootElement.TryGetProperty("content", out var content2) && content2.GetArrayLength() > 0)
                {
                    var firstContent = content2[0];
                    if (firstContent.TryGetProperty("text", out var textProp))
                    {
                        string result = textProp.GetString() ?? string.Empty;
                        
                        if (string.IsNullOrWhiteSpace(result))
                        {
                            throw new InvalidOperationException("Anthropic returned an empty response");
                        }
                        
                        _logger.LogInformation("Completion generated successfully ({Length} characters)", result.Length);
                        return result;
                    }
                }

                throw new InvalidOperationException("Invalid response structure from Anthropic API");
            }
            catch (TaskCanceledException ex)
            {
                lastException = ex;
                if (attempt >= _maxRetries)
                {
                    throw new InvalidOperationException($"Anthropic request timed out after {_timeout.TotalSeconds}s. Please check your internet connection or try again later.", ex);
                }
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
                if (attempt >= _maxRetries)
                {
                    throw new InvalidOperationException("Cannot connect to Anthropic API. Please check your internet connection and try again.", ex);
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < _maxRetries)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Error completing prompt with Anthropic (attempt {Attempt}/{MaxRetries})", attempt + 1, _maxRetries + 1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing prompt with Anthropic after all retries");
                throw;
            }
        }

        throw new InvalidOperationException(
            $"Failed to complete prompt with Anthropic after {_maxRetries + 1} attempts. Please try again later.", lastException);
    }

    public async Task<SceneAnalysisResult?> AnalyzeSceneImportanceAsync(
        string sceneText,
        string? previousSceneText,
        string videoGoal,
        CancellationToken ct)
    {
        _logger.LogInformation("Analyzing scene importance with Anthropic Claude");

        try
        {
            var systemPrompt = "You are a video pacing expert. Analyze scenes for optimal timing. " +
                              "Return your response ONLY as valid JSON with no additional text or explanation.";
            
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
                max_tokens = 1024,
                temperature = 0.5,
                top_p = 0.9,
                system = systemPrompt,
                messages = new[]
                {
                    new { role = "user", content = userPrompt }
                }
            };

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30)); // Shorter timeout for analysis

            var response = await _httpClient.PostAsync("https://api.anthropic.com/v1/messages", content, cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var responseDoc = JsonDocument.Parse(responseJson);

            if (responseDoc.RootElement.TryGetProperty("content", out var contentArray) &&
                contentArray.GetArrayLength() > 0)
            {
                var firstContent = contentArray[0];
                if (firstContent.TryGetProperty("text", out var textProp))
                {
                    var analysisText = textProp.GetString() ?? string.Empty;
                    
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
                        _logger.LogWarning(ex, "Failed to parse scene analysis JSON from Anthropic: {Response}", analysisText);
                        return null;
                    }
                }
            }

            _logger.LogWarning("Anthropic scene analysis response did not contain expected structure");
            return null;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Anthropic scene analysis request timed out");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to connect to Anthropic API for scene analysis");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing scene with Anthropic");
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
        _logger.LogInformation("Generating visual prompt with Anthropic Claude");

        try
        {
            var systemPrompt = "You are a professional cinematographer and visual director. " +
                              "Create detailed visual prompts for image generation. " +
                              "Return your response ONLY as valid JSON with no additional text.";
            
            var userPrompt = $@"Create a detailed visual prompt for this scene and return JSON with:
- detailedDescription (string): Detailed visual description (100-200 words) of what should be shown
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
                max_tokens = 2048,
                temperature = 0.7,
                top_p = 0.95,
                system = systemPrompt,
                messages = new[]
                {
                    new { role = "user", content = userPrompt }
                }
            };

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            var response = await _httpClient.PostAsync("https://api.anthropic.com/v1/messages", content, cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var responseDoc = JsonDocument.Parse(responseJson);

            if (responseDoc.RootElement.TryGetProperty("content", out var contentArray) &&
                contentArray.GetArrayLength() > 0)
            {
                var firstContent = contentArray[0];
                if (firstContent.TryGetProperty("text", out var textProp))
                {
                    var promptText = textProp.GetString() ?? string.Empty;
                    
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
                        _logger.LogWarning(ex, "Failed to parse visual prompt JSON from Anthropic: {Response}", promptText);
                        return null;
                    }
                }
            }

            _logger.LogWarning("Anthropic visual prompt response did not contain expected structure");
            return null;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Anthropic visual prompt request timed out");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to connect to Anthropic API for visual prompt");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating visual prompt with Anthropic");
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
        _logger.LogInformation("Analyzing content complexity with Anthropic Claude for video goal: {Goal}", videoGoal);

        var prompt = BuildComplexityAnalysisPrompt(sceneText, previousSceneText, videoGoal);

        try
        {
            var systemPrompt = "You are an expert in cognitive science and educational content analysis. " +
                              "Analyze the complexity of video content to optimize pacing for viewer comprehension. " +
                              "Return your response ONLY as valid JSON with no additional text.";

            var requestBody = new
            {
                model = _model,
                max_tokens = 1024,
                temperature = 0.5,
                top_p = 0.9,
                system = systemPrompt,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                }
            };

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            var response = await _httpClient.PostAsync("https://api.anthropic.com/v1/messages", content, cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var responseDoc = JsonDocument.Parse(responseJson);

            if (responseDoc.RootElement.TryGetProperty("content", out var contentArray) &&
                contentArray.GetArrayLength() > 0)
            {
                var firstContent = contentArray[0];
                if (firstContent.TryGetProperty("text", out var textProp))
                {
                    var messageContent = textProp.GetString();
                    
                    if (string.IsNullOrEmpty(messageContent))
                    {
                        _logger.LogWarning("Anthropic returned empty complexity analysis");
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
            }

            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to connect to Anthropic API for complexity analysis");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing content complexity with Anthropic");
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
        _logger.LogInformation("Analyzing scene coherence with Anthropic Claude");

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
                max_tokens = 1024,
                temperature = 0.5,
                top_p = 0.9,
                system = systemPrompt,
                messages = new[]
                {
                    new { role = "user", content = userPrompt }
                }
            };

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            var response = await _httpClient.PostAsync("https://api.anthropic.com/v1/messages", content, cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var responseDoc = JsonDocument.Parse(responseJson);

            if (responseDoc.RootElement.TryGetProperty("content", out var contentArray) &&
                contentArray.GetArrayLength() > 0)
            {
                var firstContent = contentArray[0];
                if (firstContent.TryGetProperty("text", out var textProp))
                {
                    var analysisText = textProp.GetString() ?? string.Empty;
                    
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

            _logger.LogWarning("No valid response from Anthropic for scene coherence analysis");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing scene coherence with Anthropic");
            return null;
        }
    }

    public async Task<NarrativeArcResult?> ValidateNarrativeArcAsync(
        IReadOnlyList<string> sceneTexts,
        string videoGoal,
        string videoType,
        CancellationToken ct)
    {
        _logger.LogInformation("Validating narrative arc with Anthropic Claude for {VideoType} video", videoType);

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
                max_tokens = 2048,
                temperature = 0.5,
                top_p = 0.9,
                system = systemPrompt,
                messages = new[]
                {
                    new { role = "user", content = userPrompt }
                }
            };

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(45));

            var response = await _httpClient.PostAsync("https://api.anthropic.com/v1/messages", content, cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var responseDoc = JsonDocument.Parse(responseJson);

            if (responseDoc.RootElement.TryGetProperty("content", out var contentArray) &&
                contentArray.GetArrayLength() > 0)
            {
                var firstContent = contentArray[0];
                if (firstContent.TryGetProperty("text", out var textProp))
                {
                    var analysisText = textProp.GetString() ?? string.Empty;
                    
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

            _logger.LogWarning("No valid response from Anthropic for narrative arc validation");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating narrative arc with Anthropic");
            return null;
        }
    }

    public async Task<string?> GenerateTransitionTextAsync(
        string fromSceneText,
        string toSceneText,
        string videoGoal,
        CancellationToken ct)
    {
        _logger.LogInformation("Generating transition text with Anthropic Claude");

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
                max_tokens = 256,
                temperature = 0.7,
                top_p = 0.95,
                system = systemPrompt,
                messages = new[]
                {
                    new { role = "user", content = userPrompt }
                }
            };

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            var response = await _httpClient.PostAsync("https://api.anthropic.com/v1/messages", content, cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var responseDoc = JsonDocument.Parse(responseJson);

            if (responseDoc.RootElement.TryGetProperty("content", out var contentArray) &&
                contentArray.GetArrayLength() > 0)
            {
                var firstContent = contentArray[0];
                if (firstContent.TryGetProperty("text", out var textProp))
                {
                    var transitionText = textProp.GetString()?.Trim() ?? string.Empty;
                    
                    if (!string.IsNullOrWhiteSpace(transitionText))
                    {
                        _logger.LogInformation("Generated transition text: {Text}", transitionText);
                        return transitionText;
                    }
                }
            }

            _logger.LogWarning("No valid transition text from Anthropic");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating transition text with Anthropic");
            return null;
        }
    }

    /// <summary>
    /// Whether this provider supports streaming
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
            ExpectedFirstTokenMs = 400,
            ExpectedTokensPerSec = 25,
            SupportsStreaming = true,
            ProviderTier = "Pro",
            CostPer1KTokens = 0.008m
        };
    }

    /// <summary>
    /// Stream script generation with real-time token-by-token updates (Anthropic Claude)
    /// </summary>
    public async IAsyncEnumerable<LlmStreamChunk> DraftScriptStreamAsync(
        Brief brief, 
        PlanSpec spec, 
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _logger.LogInformation("Starting streaming script generation with Anthropic (model: {Model}) for topic: {Topic}", 
            _model, brief.Topic);

        var startTime = DateTime.UtcNow;
        DateTime? firstTokenTime = null;

        // Build enhanced prompts
        string systemPrompt = EnhancedPromptTemplates.GetSystemPromptForScriptGeneration();
        string userPrompt = _promptCustomizationService.BuildCustomizedPrompt(brief, spec, brief.PromptModifiers);

        // Create streaming request (Anthropic uses "stream": true)
        var requestBody = new
        {
            model = _model,
            max_tokens = 4096,
            temperature = 0.8,
            top_p = 0.95,
            system = systemPrompt,
            messages = new[]
            {
                new { role = "user", content = userPrompt }
            },
            stop_sequences = new[] { "\n\nHuman:", "\n\nAssistant:" },
            stream = true
        };

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
        _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);

        HttpResponseMessage? response = null;
        Exception? initError = null;
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
            {
                Content = content
            };
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new InvalidOperationException(
                    $"Anthropic API error: HTTP {response.StatusCode} - {errorContent}");
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
                ProviderName = "Anthropic",
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
            using var reader = new System.IO.StreamReader(stream);

            var accumulated = new StringBuilder();
            var tokenIndex = 0;
            var stopReason = "stop";

            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                
                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: "))
                {
                    continue;
                }

                var data = line.Substring(6); // Remove "data: " prefix

                // Anthropic streaming format uses different event types
                // event: message_start, content_block_start, content_block_delta, content_block_stop, message_delta, message_stop
                
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
                    // Check event type
                    if (doc.RootElement.TryGetProperty("type", out var eventType))
                    {
                        var type = eventType.GetString();
                        
                        if (type == "content_block_delta")
                        {
                            // Extract delta content
                            if (doc.RootElement.TryGetProperty("delta", out var delta) &&
                                delta.TryGetProperty("type", out var deltaType) &&
                                deltaType.GetString() == "text_delta" &&
                                delta.TryGetProperty("text", out var textDelta))
                            {
                                var chunk = textDelta.GetString() ?? string.Empty;
                                
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
                                        ProviderName = "Anthropic",
                                        Content = chunk,
                                        AccumulatedContent = accumulated.ToString(),
                                        TokenIndex = tokenIndex,
                                        IsFinal = false
                                    };
                                }
                            }
                        }
                        else if (type == "message_delta")
                        {
                            // Capture stop reason
                            if (doc.RootElement.TryGetProperty("delta", out var delta) &&
                                delta.TryGetProperty("stop_reason", out var stopReasonProp))
                            {
                                stopReason = stopReasonProp.GetString() ?? "stop";
                            }
                        }
                        else if (type == "message_stop")
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
                                ProviderName = "Anthropic",
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
                                    FinishReason = stopReason
                                }
                            };
                            break;
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Calculate estimated cost for Anthropic Claude API usage
    /// </summary>
    private decimal CalculateCost(int tokenCount)
    {
        // Anthropic Claude costs vary by model:
        // Claude 3.5 Sonnet: $0.003/1K input, $0.015/1K output
        // Using average of $0.009/1K for simplification
        return (decimal)tokenCount / 1000 * 0.009m;
    }
}
