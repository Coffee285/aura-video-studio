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
/// LLM provider that uses Google Gemini API for script generation (Pro feature).
/// Supports prompt customization from user preferences.
/// </summary>
public class GeminiLlmProvider : ILlmProvider
{
    private readonly ILogger<GeminiLlmProvider> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly int _maxRetries;
    private readonly TimeSpan _timeout;
    private readonly PromptCustomizationService _promptCustomizationService;

    public GeminiLlmProvider(
        ILogger<GeminiLlmProvider> logger,
        HttpClient httpClient,
        string apiKey,
        string model = "gemini-pro",
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
                "Gemini API key is not configured. Please add your API key in Settings → Providers → Gemini",
                nameof(_apiKey));
        }

        // Allow test/mock keys for testing
        if (_apiKey.StartsWith("test-", StringComparison.OrdinalIgnoreCase) ||
            _apiKey.StartsWith("mock-", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Gemini API keys should be at least 30 characters
        if (_apiKey.Length < 30)
        {
            throw new ArgumentException(
                "Gemini API key format appears invalid. Please check your API key in Settings → Providers → Gemini",
                nameof(_apiKey));
        }
    }

    public async Task<string> DraftScriptAsync(Brief brief, PlanSpec spec, CancellationToken ct)
    {
        _logger.LogInformation("Generating script with Gemini (model: {Model}) for topic: {Topic}", _model, brief.Topic);

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

                // Build enhanced prompt for quality content with user customizations
                string systemPrompt = EnhancedPromptTemplates.GetSystemPromptForScriptGeneration();
                string userPrompt = _promptCustomizationService.BuildCustomizedPrompt(brief, spec, brief.PromptModifiers);
                string prompt = $"{systemPrompt}\n\n{userPrompt}";

                // Call Gemini API
                var requestBody = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new[]
                            {
                                new { text = prompt }
                            }
                        }
                    },
                    generationConfig = new
                    {
                        temperature = 0.7,
                        maxOutputTokens = 2048,
                        topP = 0.9
                    }
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(_timeout);

                var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";
                var response = await _httpClient.PostAsync(url, content, cts.Token).ConfigureAwait(false);
                
                // Handle specific HTTP error codes
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized || 
                        response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        throw new InvalidOperationException(
                            "Gemini API key is invalid or access is forbidden. Please verify your API key in Settings → Providers → Gemini");
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        _logger.LogWarning("Gemini quota exceeded or rate limit hit (attempt {Attempt}/{MaxRetries})", attempt + 1, _maxRetries + 1);
                        if (attempt >= _maxRetries)
                        {
                            throw new InvalidOperationException(
                                "Gemini API quota exceeded or rate limit reached. Please check your quota at https://makersuite.google.com/app/apikey or wait before retrying.");
                        }
                        lastException = new Exception($"Quota/rate limit exceeded: {errorContent}");
                        continue; // Retry
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                    {
                        // Parse error for more details
                        throw new InvalidOperationException(
                            $"Gemini API request was invalid. This may indicate an issue with the model name '{_model}' or request format. Error: {errorContent}");
                    }
                    else if ((int)response.StatusCode >= 500)
                    {
                        _logger.LogWarning("Gemini server error (attempt {Attempt}/{MaxRetries}): {StatusCode}",
                            attempt + 1, _maxRetries + 1, response.StatusCode);
                        if (attempt >= _maxRetries)
                        {
                            throw new InvalidOperationException(
                                $"Gemini service is experiencing issues (HTTP {response.StatusCode}). Please try again later.");
                        }
                        lastException = new Exception($"Server error: {errorContent}");
                        continue; // Retry
                    }

                    response.EnsureSuccessStatusCode();
                }

                var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var responseDoc = JsonDocument.Parse(responseJson);

                if (responseDoc.RootElement.TryGetProperty("candidates", out var candidates) &&
                    candidates.GetArrayLength() > 0)
                {
                    var firstCandidate = candidates[0];
                    if (firstCandidate.TryGetProperty("content", out var contentObj) &&
                        contentObj.TryGetProperty("parts", out var parts) &&
                        parts.GetArrayLength() > 0)
                    {
                        var firstPart = parts[0];
                        if (firstPart.TryGetProperty("text", out var textProp))
                        {
                            string script = textProp.GetString() ?? string.Empty;
                            
                            if (string.IsNullOrWhiteSpace(script))
                            {
                                throw new InvalidOperationException("Gemini returned an empty response");
                            }

                            var duration = DateTime.UtcNow - startTime;
                            _logger.LogInformation("Script generated successfully ({Length} characters) in {Duration}s",
                                script.Length, duration.TotalSeconds);
                            return script;
                        }
                    }
                }

                _logger.LogWarning("Gemini response did not contain expected structure");
                throw new InvalidOperationException("Invalid response structure from Gemini API");
            }
            catch (TaskCanceledException ex)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Gemini request timed out (attempt {Attempt}/{MaxRetries})", attempt + 1, _maxRetries + 1);
                if (attempt >= _maxRetries)
                {
                    throw new InvalidOperationException(
                        $"Gemini request timed out after {_timeout.TotalSeconds}s. Please check your internet connection or try again later.", ex);
                }
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Failed to connect to Gemini API (attempt {Attempt}/{MaxRetries})", attempt + 1, _maxRetries + 1);
                if (attempt >= _maxRetries)
                {
                    throw new InvalidOperationException(
                        "Cannot connect to Gemini API. Please check your internet connection and try again.", ex);
                }
            }
            catch (InvalidOperationException)
            {
                // Don't retry on validation errors
                throw;
            }
            catch (Exception ex) when (attempt < _maxRetries)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Error generating script with Gemini (attempt {Attempt}/{MaxRetries})", attempt + 1, _maxRetries + 1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating script with Gemini after all retries");
                throw;
            }
        }

        // Should not reach here, but just in case
        throw new InvalidOperationException(
            $"Failed to generate script with Gemini after {_maxRetries + 1} attempts. Please try again later.", lastException);
    }

    public async Task<string> CompleteAsync(string prompt, CancellationToken ct)
    {
        _logger.LogInformation("Executing raw prompt completion with Gemini");

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
                    contents = new[]
                    {
                        new
                        {
                            parts = new[]
                            {
                                new { text = prompt }
                            }
                        }
                    },
                    generationConfig = new
                    {
                        temperature = 0.7,
                        maxOutputTokens = 2048
                    }
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";
                var response = await _httpClient.PostAsync(url, content, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var responseDoc = JsonDocument.Parse(responseJson);

                if (responseDoc.RootElement.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                {
                    var firstCandidate = candidates[0];
                    if (firstCandidate.TryGetProperty("content", out var contentObj) &&
                        contentObj.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0)
                    {
                        var firstPart = parts[0];
                        if (firstPart.TryGetProperty("text", out var textProp))
                        {
                            return textProp.GetString() ?? string.Empty;
                        }
                    }
                }

                throw new InvalidOperationException("Invalid response structure from Gemini API");
            }
            catch (Exception ex) when (attempt < _maxRetries)
            {
                _logger.LogWarning(ex, "Error completing prompt with Gemini (attempt {Attempt}/{MaxRetries})", attempt + 1, _maxRetries + 1);
            }
        }

        throw new InvalidOperationException($"Failed to complete prompt with Gemini after {_maxRetries + 1} attempts.");
    }

    public async Task<SceneAnalysisResult?> AnalyzeSceneImportanceAsync(
        string sceneText,
        string? previousSceneText,
        string videoGoal,
        CancellationToken ct)
    {
        _logger.LogInformation("Analyzing scene importance with Gemini");

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

            var prompt = $"{systemPrompt}\n\n{userPrompt}";

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.3,
                    maxOutputTokens = 512,
                    topP = 0.9
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30)); // Shorter timeout for analysis

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";
            var response = await _httpClient.PostAsync(url, content, cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var responseDoc = JsonDocument.Parse(responseJson);

            if (responseDoc.RootElement.TryGetProperty("candidates", out var candidates) &&
                candidates.GetArrayLength() > 0)
            {
                var firstCandidate = candidates[0];
                if (firstCandidate.TryGetProperty("content", out var contentObj) &&
                    contentObj.TryGetProperty("parts", out var parts) &&
                    parts.GetArrayLength() > 0)
                {
                    var firstPart = parts[0];
                    if (firstPart.TryGetProperty("text", out var textProp))
                    {
                        var analysisText = textProp.GetString() ?? string.Empty;
                        
                        try
                        {
                            // Clean up potential markdown code blocks
                            analysisText = analysisText.Trim();
                            if (analysisText.StartsWith("```json"))
                            {
                                analysisText = analysisText.Substring(7);
                            }
                            if (analysisText.StartsWith("```"))
                            {
                                analysisText = analysisText.Substring(3);
                            }
                            if (analysisText.EndsWith("```"))
                            {
                                analysisText = analysisText.Substring(0, analysisText.Length - 3);
                            }
                            analysisText = analysisText.Trim();

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
                            _logger.LogWarning(ex, "Failed to parse scene analysis JSON from Gemini: {Response}", analysisText);
                            return null;
                        }
                    }
                }
            }

            _logger.LogWarning("Gemini scene analysis response did not contain expected structure");
            return null;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Gemini scene analysis request timed out");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to connect to Gemini API for scene analysis");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing scene with Gemini");
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
        _logger.LogInformation("Generating visual prompt with Gemini");

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
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.7,
                    maxOutputTokens = 1024
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";
            var response = await _httpClient.PostAsync(url, content, cts.Token).ConfigureAwait(false);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _logger.LogWarning("Gemini visual prompt generation failed: {StatusCode} - {Error}", 
                    response.StatusCode, errorContent);
                return null;
            }

            var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var responseDoc = JsonDocument.Parse(responseJson);

            if (responseDoc.RootElement.TryGetProperty("candidates", out var candidates) &&
                candidates.GetArrayLength() > 0)
            {
                var firstCandidate = candidates[0];
                if (firstCandidate.TryGetProperty("content", out var contentObj) &&
                    contentObj.TryGetProperty("parts", out var parts) &&
                    parts.GetArrayLength() > 0)
                {
                    var firstPart = parts[0];
                    if (firstPart.TryGetProperty("text", out var textProp))
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

                            _logger.LogInformation("Visual prompt generated successfully with Gemini");
                            return result;
                        }
                        catch (JsonException ex)
                        {
                            _logger.LogWarning(ex, "Failed to parse visual prompt JSON from Gemini: {Response}", promptText);
                            return null;
                        }
                    }
                }
            }

            _logger.LogWarning("Gemini visual prompt response did not contain expected structure");
            return null;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Gemini visual prompt request timed out");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to connect to Gemini API for visual prompt");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating visual prompt with Gemini");
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
        _logger.LogInformation("Analyzing content complexity with Gemini for video goal: {Goal}", videoGoal);

        try
        {
            var systemPrompt = "You are an expert in cognitive science and educational content analysis. " +
                              "Analyze the complexity of video content to optimize pacing for viewer comprehension. " +
                              "Return your response ONLY as valid JSON with no additional text.";
            
            var userPrompt = $@"Analyze the cognitive complexity of this video scene content to optimize viewer comprehension and pacing.

VIDEO GOAL: {videoGoal}

SCENE CONTENT:
{sceneText}
{(!string.IsNullOrEmpty(previousSceneText) ? $"\nPREVIOUS SCENE (for context):\n{previousSceneText}" : "")}

Provide a JSON response with the following fields (all scores 0-100):
{{
  ""overall_complexity_score"": <0-100>,
  ""concept_difficulty"": <0-100, how difficult are the concepts?>,
  ""terminology_density"": <0-100, how many specialized terms?>,
  ""prerequisite_knowledge_level"": <0-100, how much prior knowledge assumed?>,
  ""multi_step_reasoning_required"": <0-100, requires following multiple logical steps?>,
  ""new_concepts_introduced"": <integer count of new concepts>,
  ""cognitive_processing_time_seconds"": <estimated time to process>,
  ""optimal_attention_window_seconds"": <5-15, how long to show this content?>,
  ""detailed_breakdown"": ""<2-3 sentence explanation of complexity factors>""
}}

Respond with ONLY the JSON object, no other text:";

            var prompt = $"{systemPrompt}\n\n{userPrompt}";

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.3,
                    maxOutputTokens = 512
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";
            var response = await _httpClient.PostAsync(url, content, cts.Token).ConfigureAwait(false);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _logger.LogWarning("Gemini complexity analysis failed: {StatusCode} - {Error}", 
                    response.StatusCode, errorContent);
                return null;
            }

            var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var responseDoc = JsonDocument.Parse(responseJson);

            if (responseDoc.RootElement.TryGetProperty("candidates", out var candidates) &&
                candidates.GetArrayLength() > 0)
            {
                var firstCandidate = candidates[0];
                if (firstCandidate.TryGetProperty("content", out var contentObj) &&
                    contentObj.TryGetProperty("parts", out var parts) &&
                    parts.GetArrayLength() > 0)
                {
                    var firstPart = parts[0];
                    if (firstPart.TryGetProperty("text", out var textProp))
                    {
                        var analysisText = textProp.GetString() ?? string.Empty;
                        
                        try
                        {
                            var analysisDoc = JsonDocument.Parse(analysisText);
                            var root = analysisDoc.RootElement;

                            var result = new ContentComplexityAnalysisResult(
                                OverallComplexityScore: GetDoubleProperty(root, "overall_complexity_score", 50.0),
                                ConceptDifficulty: GetDoubleProperty(root, "concept_difficulty", 50.0),
                                TerminologyDensity: GetDoubleProperty(root, "terminology_density", 50.0),
                                PrerequisiteKnowledgeLevel: GetDoubleProperty(root, "prerequisite_knowledge_level", 50.0),
                                MultiStepReasoningRequired: GetDoubleProperty(root, "multi_step_reasoning_required", 50.0),
                                NewConceptsIntroduced: GetIntProperty(root, "new_concepts_introduced", 3),
                                CognitiveProcessingTimeSeconds: GetDoubleProperty(root, "cognitive_processing_time_seconds", 10.0),
                                OptimalAttentionWindowSeconds: GetDoubleProperty(root, "optimal_attention_window_seconds", 10.0),
                                DetailedBreakdown: GetStringProperty(root, "detailed_breakdown", "No breakdown provided")
                            );

                            _logger.LogInformation("Content complexity analyzed: Overall={Score:F0}, NewConcepts={Concepts}",
                                result.OverallComplexityScore, result.NewConceptsIntroduced);

                            return result;
                        }
                        catch (JsonException ex)
                        {
                            _logger.LogWarning(ex, "Failed to parse complexity analysis JSON from Gemini");
                            return null;
                        }
                    }
                }
            }

            _logger.LogWarning("Gemini complexity analysis response did not contain expected structure");
            return null;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Gemini complexity analysis request timed out");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to connect to Gemini API for complexity analysis");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing content complexity with Gemini");
            return null;
        }
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
        _logger.LogInformation("Analyzing scene coherence with Gemini");

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
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.3,
                    maxOutputTokens = 512
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";
            var response = await _httpClient.PostAsync(url, content, cts.Token).ConfigureAwait(false);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _logger.LogWarning("Gemini coherence analysis failed: {StatusCode} - {Error}", 
                    response.StatusCode, errorContent);
                return null;
            }

            var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var responseDoc = JsonDocument.Parse(responseJson);

            if (responseDoc.RootElement.TryGetProperty("candidates", out var candidates) &&
                candidates.GetArrayLength() > 0)
            {
                var firstCandidate = candidates[0];
                if (firstCandidate.TryGetProperty("content", out var contentObj) &&
                    contentObj.TryGetProperty("parts", out var parts) &&
                    parts.GetArrayLength() > 0)
                {
                    var firstPart = parts[0];
                    if (firstPart.TryGetProperty("text", out var textProp))
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
                            _logger.LogWarning(ex, "Failed to parse coherence analysis JSON from Gemini");
                            return null;
                        }
                    }
                }
            }

            _logger.LogWarning("No valid response from Gemini for scene coherence analysis");
            return null;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Gemini coherence analysis request timed out");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to connect to Gemini API for coherence analysis");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing scene coherence with Gemini");
            return null;
        }
    }

    public async Task<NarrativeArcResult?> ValidateNarrativeArcAsync(
        IReadOnlyList<string> sceneTexts,
        string videoGoal,
        string videoType,
        CancellationToken ct)
    {
        _logger.LogInformation("Validating narrative arc with Gemini for {VideoType} video", videoType);

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
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.3,
                    maxOutputTokens = 1024
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(45));

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";
            var response = await _httpClient.PostAsync(url, content, cts.Token).ConfigureAwait(false);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _logger.LogWarning("Gemini narrative arc validation failed: {StatusCode} - {Error}", 
                    response.StatusCode, errorContent);
                return null;
            }

            var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var responseDoc = JsonDocument.Parse(responseJson);

            if (responseDoc.RootElement.TryGetProperty("candidates", out var candidates) &&
                candidates.GetArrayLength() > 0)
            {
                var firstCandidate = candidates[0];
                if (firstCandidate.TryGetProperty("content", out var contentObj) &&
                    contentObj.TryGetProperty("parts", out var parts) &&
                    parts.GetArrayLength() > 0)
                {
                    var firstPart = parts[0];
                    if (firstPart.TryGetProperty("text", out var textProp))
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
                            _logger.LogWarning(ex, "Failed to parse narrative arc JSON from Gemini");
                            return null;
                        }
                    }
                }
            }

            _logger.LogWarning("No valid response from Gemini for narrative arc validation");
            return null;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Gemini narrative arc validation request timed out");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to connect to Gemini API for narrative arc validation");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating narrative arc with Gemini");
            return null;
        }
    }

    public async Task<string?> GenerateTransitionTextAsync(
        string fromSceneText,
        string toSceneText,
        string videoGoal,
        CancellationToken ct)
    {
        _logger.LogInformation("Generating transition text with Gemini");

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
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.7,
                    maxOutputTokens = 128
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";
            var response = await _httpClient.PostAsync(url, content, cts.Token).ConfigureAwait(false);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _logger.LogWarning("Gemini transition text generation failed: {StatusCode} - {Error}", 
                    response.StatusCode, errorContent);
                return null;
            }

            var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var responseDoc = JsonDocument.Parse(responseJson);

            if (responseDoc.RootElement.TryGetProperty("candidates", out var candidates) &&
                candidates.GetArrayLength() > 0)
            {
                var firstCandidate = candidates[0];
                if (firstCandidate.TryGetProperty("content", out var contentObj) &&
                    contentObj.TryGetProperty("parts", out var parts) &&
                    parts.GetArrayLength() > 0)
                {
                    var firstPart = parts[0];
                    if (firstPart.TryGetProperty("text", out var textProp))
                    {
                        var transitionText = textProp.GetString()?.Trim() ?? string.Empty;
                        
                        if (!string.IsNullOrWhiteSpace(transitionText))
                        {
                            _logger.LogInformation("Generated transition text: {Text}", transitionText);
                            return transitionText;
                        }
                    }
                }
            }

            _logger.LogWarning("No valid transition text from Gemini");
            return null;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Gemini transition text request timed out");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to connect to Gemini API for transition text");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating transition text with Gemini");
            return null;
        }
    }

    // Removed legacy prompt building method - now using EnhancedPromptTemplates

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
            ExpectedFirstTokenMs = 600,
            ExpectedTokensPerSec = 25,
            SupportsStreaming = true,
            ProviderTier = "Pro",
            CostPer1KTokens = 0.0005m
        };
    }

    /// <summary>
    /// Stream script generation with real-time token-by-token updates (Google Gemini)
    /// </summary>
    public async IAsyncEnumerable<LlmStreamChunk> DraftScriptStreamAsync(
        Brief brief, 
        PlanSpec spec, 
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _logger.LogInformation("Starting streaming script generation with Gemini for topic: {Topic}", brief.Topic);

        var startTime = DateTime.UtcNow;
        DateTime? firstTokenTime = null;

        // Build enhanced prompts
        string systemPrompt = EnhancedPromptTemplates.GetSystemPromptForScriptGeneration();
        string userPrompt = _promptCustomizationService.BuildCustomizedPrompt(brief, spec, brief.PromptModifiers);
        string combinedPrompt = $"{systemPrompt}\n\n{userPrompt}";

        // Create streaming request (Gemini uses alt=sse for streaming)
        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = combinedPrompt }
                    }
                }
            },
            generationConfig = new
            {
                temperature = 0.7,
                topP = 0.95,
                topK = 40,
                maxOutputTokens = 2048
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);

        // Gemini streaming endpoint includes alt=sse parameter
        var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:streamGenerateContent?alt=sse&key={_apiKey}";

        HttpResponseMessage? response = null;
        Exception? initError = null;
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = content
            };
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new InvalidOperationException(
                    $"Gemini API error: HTTP {response.StatusCode} - {errorContent}");
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
                ProviderName = "Gemini",
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
            var finishReason = "STOP";

            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                
                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: "))
                {
                    continue;
                }

                var data = line.Substring(6); // Remove "data: " prefix

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
                    // Gemini streaming format: candidates[0].content.parts[0].text
                    if (doc.RootElement.TryGetProperty("candidates", out var candidates) &&
                        candidates.GetArrayLength() > 0)
                    {
                        var candidate = candidates[0];
                        
                        // Check for finish reason
                        if (candidate.TryGetProperty("finishReason", out var finishReasonProp))
                        {
                            finishReason = finishReasonProp.GetString() ?? "STOP";
                        }

                        // Extract content
                        if (candidate.TryGetProperty("content", out var contentObj) &&
                            contentObj.TryGetProperty("parts", out var parts) &&
                            parts.GetArrayLength() > 0)
                        {
                            var part = parts[0];
                            if (part.TryGetProperty("text", out var textProp))
                            {
                                var chunk = textProp.GetString() ?? string.Empty;
                                
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
                                        ProviderName = "Gemini",
                                        Content = chunk,
                                        AccumulatedContent = accumulated.ToString(),
                                        TokenIndex = tokenIndex,
                                        IsFinal = false
                                    };
                                }
                            }
                        }

                        // Check if this is the final chunk
                        if (finishReason != null && finishReason != "UNSPECIFIED")
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
                                ProviderName = "Gemini",
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
                                    FinishReason = finishReason
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
    /// Calculate estimated cost for Gemini API usage
    /// </summary>
    private decimal CalculateCost(int tokenCount)
    {
        // Gemini Pro: $0.00025/1K input, $0.0005/1K output
        // Using average of $0.000375/1K for simplification
        return (decimal)tokenCount / 1000 * 0.000375m;
    }
}
