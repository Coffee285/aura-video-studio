using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aura.Api.Models.ApiModels.V1;
using Aura.Core.Interfaces;
using Aura.Core.Models;
using Aura.Core.Models.Generation;
using Aura.Core.Orchestrator;
using Aura.Core.Providers;
using Aura.Core.Services;
using Aura.Core.Services.Generation;
using Aura.Core.Services.Providers;
using Aura.Providers.Llm;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aura.Api.Controllers;

/// <summary>
/// Controller for script generation with provider selection
/// </summary>
[ApiController]
[Route("api/scripts")]
public class ScriptsController : ControllerBase
{
    private readonly ILogger<ScriptsController> _logger;
    private readonly ScriptOrchestrator _scriptOrchestrator;
    private readonly ScriptProcessor _scriptProcessor;
    private readonly ScriptCacheService _cacheService;
    private readonly ProviderMixer _providerMixer;
    private readonly StreamingOrchestrator _streamingOrchestrator;

    /// <summary>
    /// In-memory script storage for MVP/demo purposes.
    /// In production, this should be replaced with persistent storage (database, cache server, etc.)
    /// to support application restarts and load-balanced deployments.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Script> _scriptStore = new();

    /// <summary>
    /// In-memory version storage. Maps scriptId -> list of versions.
    /// </summary>
    private static readonly ConcurrentDictionary<string, List<ScriptVersionDto>> _versionStore = new();

    private readonly Aura.Core.Services.RAG.VectorIndex? _vectorIndex;
    private readonly Aura.Core.Services.RAG.RagContextBuilder? _ragContextBuilder;

    private readonly IServiceProvider? _serviceProvider;

    public ScriptsController(
        ILogger<ScriptsController> logger,
        ScriptOrchestrator scriptOrchestrator,
        ScriptProcessor scriptProcessor,
        ScriptCacheService cacheService,
        ProviderMixer providerMixer,
        StreamingOrchestrator streamingOrchestrator,
        IServiceProvider? serviceProvider = null,
        Aura.Core.Services.RAG.VectorIndex? vectorIndex = null,
        Aura.Core.Services.RAG.RagContextBuilder? ragContextBuilder = null)
    {
        _logger = logger;
        _scriptOrchestrator = scriptOrchestrator;
        _scriptProcessor = scriptProcessor;
        _cacheService = cacheService;
        _providerMixer = providerMixer;
        _streamingOrchestrator = streamingOrchestrator;
        _serviceProvider = serviceProvider;
        _vectorIndex = vectorIndex;
        _ragContextBuilder = ragContextBuilder;
    }

    /// <summary>
    /// Generate a new script using provider selection
    /// </summary>
    [HttpPost("generate")]
    [Microsoft.AspNetCore.Http.Timeouts.RequestTimeout(1200000)] // 20 minutes - very lenient for slow systems, large models, and initial model loading
    public async Task<IActionResult> GenerateScript(
        [FromBody] GenerateScriptRequest? request,
        CancellationToken ct)
    {
        var correlationId = HttpContext.TraceIdentifier;

        // Validate request is not null
        if (request == null)
        {
            _logger.LogWarning("[{CorrelationId}] GenerateScript request is null", correlationId);
            return BadRequest(new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E400",
                Title = "Invalid Request",
                Status = 400,
                Detail = "Request body is required and cannot be null",
                Extensions = { ["correlationId"] = correlationId }
            });
        }

        // Validate required fields
        if (string.IsNullOrWhiteSpace(request.Topic))
        {
            _logger.LogWarning("[{CorrelationId}] GenerateScript request missing Topic", correlationId);
            return BadRequest(new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E400",
                Title = "Invalid Request",
                Status = 400,
                Detail = "Topic is required and cannot be empty",
                Extensions = { ["correlationId"] = correlationId }
            });
        }

        if (request.TargetDurationSeconds <= 0)
        {
            _logger.LogWarning("[{CorrelationId}] GenerateScript request has invalid TargetDurationSeconds: {Duration}",
                correlationId, request.TargetDurationSeconds);
            return BadRequest(new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E400",
                Title = "Invalid Request",
                Status = 400,
                Detail = "TargetDurationSeconds must be greater than 0",
                Extensions = { ["correlationId"] = correlationId }
            });
        }

        try
        {
            _logger.LogInformation(
                "[{CorrelationId}] POST /api/scripts/generate - Topic: {Topic}, Provider: {Provider}, Duration: {Duration}s",
                correlationId, request.Topic, request.PreferredProvider ?? "auto", request.TargetDurationSeconds);

            // Check if RAG configuration is explicitly provided in the request
            Aura.Core.Models.RagConfiguration? ragConfig = null;
            if (request.RagConfiguration != null)
            {
                // Use explicit RAG configuration from request
                ragConfig = new Aura.Core.Models.RagConfiguration(
                    Enabled: request.RagConfiguration.Enabled,
                    TopK: request.RagConfiguration.TopK,
                    MinimumScore: request.RagConfiguration.MinimumScore,
                    MaxContextTokens: request.RagConfiguration.MaxContextTokens,
                    IncludeCitations: request.RagConfiguration.IncludeCitations,
                    TightenClaims: request.RagConfiguration.TightenClaims);
                
                _logger.LogInformation(
                    "[{CorrelationId}] Using explicit RAG configuration: Enabled={Enabled}, TopK={TopK}",
                    correlationId, ragConfig.Enabled, ragConfig.TopK);
            }
            else if (_vectorIndex != null)
            {
                // Enable RAG automatically if documents are available in the index
                try
                {
                    var stats = await _vectorIndex.GetStatisticsAsync(ct).ConfigureAwait(false);
                    if (stats.TotalDocuments > 0)
                    {
                        _logger.LogInformation(
                            "[{CorrelationId}] RAG index contains {DocumentCount} documents, enabling RAG for script generation",
                            correlationId, stats.TotalDocuments);
                        ragConfig = new Aura.Core.Models.RagConfiguration(
                            Enabled: true,
                            TopK: 5,
                            MinimumScore: 0.6f,
                            MaxContextTokens: 2000,
                            IncludeCitations: true,
                            TightenClaims: false);
                    }
                    else
                    {
                        _logger.LogDebug(
                            "[{CorrelationId}] RAG index is empty, skipping RAG enhancement",
                            correlationId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "[{CorrelationId}] Failed to check RAG index status, continuing without RAG",
                        correlationId);
                }
            }

            // Build LLM parameters if any are provided
            Aura.Core.Models.LlmParameters? llmParams = null;
            if (request.Temperature.HasValue || request.TopP.HasValue || request.TopK.HasValue ||
                request.MaxTokens.HasValue || request.FrequencyPenalty.HasValue ||
                request.PresencePenalty.HasValue || request.StopSequences != null ||
                !string.IsNullOrWhiteSpace(request.ModelOverride))
            {
                llmParams = new Aura.Core.Models.LlmParameters(
                    Temperature: request.Temperature,
                    TopP: request.TopP,
                    TopK: request.TopK,
                    MaxTokens: request.MaxTokens,
                    FrequencyPenalty: request.FrequencyPenalty,
                    PresencePenalty: request.PresencePenalty,
                    StopSequences: request.StopSequences,
                    ModelOverride: request.ModelOverride);
            }

            var brief = new Brief(
                Topic: request.Topic,
                Audience: request.Audience,
                Goal: request.Goal,
                Tone: request.Tone,
                Language: request.Language,
                Aspect: ParseAspect(request.Aspect),
                RagConfiguration: ragConfig,
                LlmParameters: llmParams);

            var planSpec = new PlanSpec(
                TargetDuration: TimeSpan.FromSeconds(request.TargetDurationSeconds),
                Pacing: ParsePacing(request.Pacing),
                Density: ParseDensity(request.Density),
                Style: request.Style);

            // Use PreferredProvider directly - it can be a provider name (e.g., "Ollama") or a tier (e.g., "Free")
            // ProviderMixer.SelectLlmProvider will handle both cases
            // If PreferredProvider is null/empty/"Auto", default to "Free" tier
            var preferredTier = !string.IsNullOrWhiteSpace(request.PreferredProvider) &&
                                request.PreferredProvider != "Auto"
                                ? request.PreferredProvider
                                : "Free";

            _logger.LogInformation(
                "[{CorrelationId}] Script generation requested. Topic: {Topic}, PreferredProvider: {Provider} (resolved to: {Resolved}), ModelOverride: {ModelOverride}",
                correlationId, request.Topic, request.PreferredProvider ?? "null", preferredTier, request.ModelOverride ?? "null");

            var result = await _scriptOrchestrator.GenerateScriptAsync(
                brief, planSpec, preferredTier, offlineOnly: false, ct).ConfigureAwait(false);

            _logger.LogInformation("[{CorrelationId}] Script generation completed. Success: {Success}, ProviderUsed: {Provider}",
                correlationId, result.Success, result.ProviderUsed ?? "None");

            if (!result.Success || result.Script == null)
            {
                _logger.LogWarning(
                    "[{CorrelationId}] Script generation failed: {ErrorCode} - {ErrorMessage}, ProviderUsed: {Provider}, IsFallback: {IsFallback}",
                    correlationId, result.ErrorCode, result.ErrorMessage, result.ProviderUsed ?? "None", result.IsFallback);

                // Provide more helpful error messages based on error code
                var errorDetail = result.ErrorMessage ?? "Failed to generate script";
                if (result.ErrorCode == "E306")
                {
                    errorDetail = "Ollama service is not running. Please start Ollama or select a different provider.";
                }
                else if (result.ErrorCode == "E300" && result.ProviderUsed == null)
                {
                    errorDetail = "All LLM providers failed. Please ensure at least one provider (Ollama, OpenAI, Gemini, or RuleBased) is available and configured.";
                }
                else if (result.IsFallback)
                {
                    errorDetail = $"{errorDetail} (Fell back from {result.RequestedProvider ?? "requested provider"})";
                }

                return StatusCode(500, new ProblemDetails
                {
                    Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E300",
                    Title = "Script Generation Failed",
                    Status = 500,
                    Detail = errorDetail,
                    Extensions =
                    {
                        ["correlationId"] = correlationId,
                        ["errorCode"] = result.ErrorCode,
                        ["providerUsed"] = result.ProviderUsed ?? "None",
                        ["isFallback"] = result.IsFallback,
                        ["requestedProvider"] = result.RequestedProvider ?? "None"
                    }
                });
            }

            // Validate the script text before parsing
            if (string.IsNullOrWhiteSpace(result.Script))
            {
                _logger.LogError(
                    "[{CorrelationId}] Script generation returned empty or null script text. Provider: {Provider}",
                    correlationId, result.ProviderUsed ?? "Unknown");
                
                return StatusCode(500, new ProblemDetails
                {
                    Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E302",
                    Title = "Script Generation Failed",
                    Status = 500,
                    Detail = $"Provider {result.ProviderUsed ?? "Unknown"} returned an empty script. The model may not have generated any content. Please try again with a different prompt or provider.",
                    Extensions =
                    {
                        ["correlationId"] = correlationId,
                        ["errorCode"] = "E302",
                        ["providerUsed"] = result.ProviderUsed ?? "None"
                    }
                });
            }

            var scriptId = Guid.NewGuid().ToString();
            // Extract model used from the Brief LlmParameters if available
            // Use "provider-default" as fallback to distinguish from explicit "default" model selection
            var modelUsed = llmParams?.ModelOverride ?? "provider-default";
            
            Script script;
            try
            {
                script = ParseScriptFromText(result.Script, planSpec, result.ProviderUsed ?? "Unknown", modelUsed);
            }
            catch (Exception parseEx)
            {
                _logger.LogError(parseEx,
                    "[{CorrelationId}] Failed to parse script text. Script length: {Length}, Provider: {Provider}",
                    correlationId, result.Script?.Length ?? 0, result.ProviderUsed ?? "Unknown");
                
                return StatusCode(500, new ProblemDetails
                {
                    Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E303",
                    Title = "Script Parsing Failed",
                    Status = 500,
                    Detail = "The generated script could not be parsed into scenes. This may indicate an issue with the model output format. Please try again.",
                    Extensions =
                    {
                        ["correlationId"] = correlationId,
                        ["errorCode"] = "E303",
                        ["providerUsed"] = result.ProviderUsed ?? "None",
                        ["scriptLength"] = result.Script?.Length ?? 0
                    }
                });
            }

            // Validate parsed script has scenes
            if (script.Scenes == null || script.Scenes.Count == 0)
            {
                _logger.LogError(
                    "[{CorrelationId}] Parsed script has no scenes. Script text length: {Length}, Provider: {Provider}",
                    correlationId, result.Script?.Length ?? 0, result.ProviderUsed ?? "Unknown");
                
                return StatusCode(500, new ProblemDetails
                {
                    Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E304",
                    Title = "Invalid Script Structure",
                    Status = 500,
                    Detail = "The generated script could not be parsed into scenes. The model output may not be in the expected format. Please try again with a different prompt or provider.",
                    Extensions =
                    {
                        ["correlationId"] = correlationId,
                        ["errorCode"] = "E304",
                        ["providerUsed"] = result.ProviderUsed ?? "None"
                    }
                });
            }

            script = script with { CorrelationId = correlationId };

            try
            {
                script = _scriptProcessor.ValidateSceneTiming(script, planSpec.TargetDuration);
                script = _scriptProcessor.OptimizeNarrationFlow(script);
                script = _scriptProcessor.ApplyTransitions(script, planSpec.Style);
            }
            catch (Exception processingEx)
            {
                _logger.LogWarning(processingEx,
                    "[{CorrelationId}] Error during script post-processing, using unprocessed script",
                    correlationId);
                // Continue with unprocessed script rather than failing
            }

            _scriptStore[scriptId] = script;

            _logger.LogInformation(
                "[{CorrelationId}] Script generated successfully with provider {Provider}, ID: {ScriptId}, Scenes: {SceneCount}",
                correlationId, result.ProviderUsed, scriptId, script.Scenes.Count);

            var response = MapScriptToResponse(scriptId, script);
            
            // Final validation of response
            if (response.Scenes == null || response.Scenes.Count == 0)
            {
                _logger.LogError(
                    "[{CorrelationId}] MapScriptToResponse produced empty scenes. Original script had {SceneCount} scenes",
                    correlationId, script.Scenes.Count);
                
                return StatusCode(500, new ProblemDetails
                {
                    Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E305",
                    Title = "Script Mapping Failed",
                    Status = 500,
                    Detail = "An error occurred while mapping the script to the response format. Please try again.",
                    Extensions =
                    {
                        ["correlationId"] = correlationId,
                        ["errorCode"] = "E305"
                    }
                });
            }
            
            return Ok(response);
        }
        catch (TaskCanceledException ex) when (ct.IsCancellationRequested)
        {
            // Check if it was a timeout cancellation - only check for actual timeout indicators
            // Do NOT check for "canceled" as cancellations can happen for many reasons (client disconnect, app shutdown, etc.)
            var isTimeout = ex.InnerException is TimeoutException ||
                           ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase);

            _logger.LogWarning(ex,
                "[{CorrelationId}] Script generation was canceled. IsTimeout: {IsTimeout}, InnerException: {InnerExceptionType}",
                correlationId, isTimeout, ex.InnerException?.GetType().Name ?? "none");

            // Only return 408 for actual timeouts; for other cancellations, return a different status
            if (isTimeout)
            {
                return StatusCode(408, new ProblemDetails
                {
                    Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E408",
                    Title = "Request Timeout",
                    Status = 408,
                    Detail = "Script generation timed out after 20 minutes. This timeout is very lenient and designed for slow systems. " +
                             "If you're still hitting this timeout, your system may be too slow for the selected model, or there may be an issue with Ollama. " +
                             "Suggestions:\n" +
                             "  - Check if Ollama is actually running: 'ollama list'\n" +
                             "  - Try a smaller model: llama3.2:3b or llama3.2:1b\n" +
                             "  - Check system resources (RAM, CPU usage)\n" +
                             "  - Review backend logs for detailed error messages",
                    Extensions =
                    {
                        ["correlationId"] = correlationId,
                        ["errorCode"] = "E408",
                        ["suggestion"] = "Try a shorter topic or simpler prompt, or check Ollama status"
                    }
                });
            }

            // For non-timeout cancellations, return 499 (Client Closed Request) or generic cancellation message
            _logger.LogInformation(
                "[{CorrelationId}] Script generation was canceled (not due to timeout). Possible reasons: client disconnect, application shutdown, or manual cancellation.",
                correlationId);

            return StatusCode(499, new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E499",
                Title = "Request Cancelled",
                Status = 499,
                Detail = "Script generation was canceled. Please try again.",
                Extensions =
                {
                    ["correlationId"] = correlationId,
                    ["errorCode"] = "E499"
                }
            });
        }
        catch (Exception ex)
        {
            // Log the full exception details including inner exceptions
            _logger.LogError(ex,
                "[{CorrelationId}] Error generating script. ExceptionType: {ExceptionType}, Message: {Message}, InnerException: {InnerException}",
                correlationId,
                ex.GetType().Name,
                ex.Message,
                ex.InnerException?.Message ?? "none");

            // Extract more detailed error information
            var errorDetail = "An error occurred while generating the script";
            if (ex is TimeoutException timeoutEx)
            {
                errorDetail = $"Request timed out: {timeoutEx.Message}. The model may be processing a large request. Please try again.";
            }
            else if (ex is HttpRequestException httpEx)
            {
                errorDetail = $"HTTP error during script generation: {httpEx.Message}. Please check your provider configuration.";
            }
            else if (ex is InvalidOperationException invalidOpEx)
            {
                errorDetail = invalidOpEx.Message;
            }
            else if (!string.IsNullOrWhiteSpace(ex.Message))
            {
                errorDetail = ex.Message;
            }

            return StatusCode(500, new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E500",
                Title = "Internal Server Error",
                Status = 500,
                Detail = errorDetail,
                Extensions =
                {
                    ["correlationId"] = correlationId,
                    ["exceptionType"] = ex.GetType().Name,
                    ["errorCode"] = "E500"
                }
            });
        }
    }

    /// <summary>
    /// Get a previously generated script by ID
    /// </summary>
    [HttpGet("{id}")]
    public IActionResult GetScript(string id)
    {
        var correlationId = HttpContext.TraceIdentifier;

        if (_scriptStore.TryGetValue(id, out var script))
        {
            _logger.LogInformation("[{CorrelationId}] Retrieved script {ScriptId}", correlationId, id);
            var response = MapScriptToResponse(id, script);
            return Ok(response);
        }

        _logger.LogWarning("[{CorrelationId}] Script {ScriptId} not found", correlationId, id);

        return NotFound(new ProblemDetails
        {
            Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E404",
            Title = "Script Not Found",
            Status = 404,
            Detail = $"Script with ID '{id}' was not found",
            Extensions = { ["correlationId"] = correlationId }
        });
    }

    /// <summary>
    /// Update a specific scene in a script
    /// </summary>
    [HttpPut("{id}/scenes/{sceneNumber}")]
    public IActionResult UpdateScene(
        string id,
        int sceneNumber,
        [FromBody] UpdateSceneRequest request)
    {
        var correlationId = HttpContext.TraceIdentifier;

        if (!_scriptStore.TryGetValue(id, out var script))
        {
            return NotFound(new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E404",
                Title = "Script Not Found",
                Status = 404,
                Detail = $"Script with ID '{id}' was not found",
                Extensions = { ["correlationId"] = correlationId }
            });
        }

        var scene = script.Scenes.FirstOrDefault(s => s.Number == sceneNumber);
        if (scene == null)
        {
            return NotFound(new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E404",
                Title = "Scene Not Found",
                Status = 404,
                Detail = $"Scene {sceneNumber} not found in script '{id}'",
                Extensions = { ["correlationId"] = correlationId }
            });
        }

        var updatedScene = scene;
        if (request.Narration != null)
        {
            updatedScene = updatedScene with { Narration = request.Narration };
        }
        if (request.VisualPrompt != null)
        {
            updatedScene = updatedScene with { VisualPrompt = request.VisualPrompt };
        }
        if (request.DurationSeconds.HasValue)
        {
            updatedScene = updatedScene with { Duration = TimeSpan.FromSeconds(request.DurationSeconds.Value) };
        }

        var updatedScenes = script.Scenes.Select(s => s.Number == sceneNumber ? updatedScene : s).ToList();
        var updatedScript = script with { Scenes = updatedScenes };

        _scriptStore[id] = updatedScript;

        _logger.LogInformation(
            "[{CorrelationId}] Updated scene {SceneNumber} in script {ScriptId}",
            correlationId, sceneNumber, id);

        var response = MapScriptToResponse(id, updatedScript);
        return Ok(response);
    }

    /// <summary>
    /// Regenerate a specific scene in a script
    /// </summary>
    [HttpPost("{id}/scenes/{sceneNumber}/regenerate")]
    public async Task<IActionResult> RegenerateScene(
        string id,
        int sceneNumber,
        [FromBody] RegenerateSceneRequest? request,
        CancellationToken ct)
    {
        var correlationId = HttpContext.TraceIdentifier;

        if (!_scriptStore.TryGetValue(id, out var script))
        {
            return NotFound(new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E404",
                Title = "Script Not Found",
                Status = 404,
                Detail = $"Script with ID '{id}' was not found",
                Extensions = { ["correlationId"] = correlationId }
            });
        }

        var scene = script.Scenes.FirstOrDefault(s => s.Number == sceneNumber);
        if (scene == null)
        {
            return NotFound(new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E404",
                Title = "Scene Not Found",
                Status = 404,
                Detail = $"Scene {sceneNumber} not found in script '{id}'",
                Extensions = { ["correlationId"] = correlationId }
            });
        }

        SaveVersion(id, script, $"Before regenerating scene {sceneNumber}");

        _logger.LogInformation(
            "[{CorrelationId}] Regenerating scene {SceneNumber} in script {ScriptId} with context: {IncludeContext}",
            correlationId, sceneNumber, id, request?.IncludeContext ?? true);

        try
        {
            var contextInfo = string.Empty;
            if (request?.IncludeContext ?? true)
            {
                var prevScene = script.Scenes.FirstOrDefault(s => s.Number == sceneNumber - 1);
                var nextScene = script.Scenes.FirstOrDefault(s => s.Number == sceneNumber + 1);

                if (prevScene != null)
                {
                    contextInfo += $"Previous scene: {prevScene.Narration.Substring(0, Math.Min(100, prevScene.Narration.Length))}... ";
                }
                if (nextScene != null)
                {
                    contextInfo += $"Next scene: {nextScene.Narration.Substring(0, Math.Min(100, nextScene.Narration.Length))}...";
                }
            }

            var goal = request?.ImprovementGoal ?? "Regenerate this scene with fresh content";
            if (!string.IsNullOrEmpty(contextInfo))
            {
                goal += $" Context: {contextInfo}";
            }

            var brief = new Brief(
                Topic: scene.Narration.Length > 100
                    ? scene.Narration.Substring(0, 100) + "..."
                    : scene.Narration,
                Audience: null,
                Goal: goal,
                Tone: "Conversational",
                Language: "en",
                Aspect: Core.Models.Aspect.Widescreen16x9);

            var planSpec = new PlanSpec(
                TargetDuration: scene.Duration,
                Pacing: Core.Models.Pacing.Conversational,
                Density: Core.Models.Density.Balanced,
                Style: "Modern");

            var result = await _scriptOrchestrator.GenerateScriptAsync(
                brief, planSpec, script.Metadata.ProviderName, offlineOnly: false, ct).ConfigureAwait(false);

            if (!result.Success || result.Script == null)
            {
                _logger.LogWarning(
                    "[{CorrelationId}] Scene regeneration failed: {ErrorCode} - {ErrorMessage}",
                    correlationId, result.ErrorCode, result.ErrorMessage);

                return StatusCode(500, new ProblemDetails
                {
                    Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E300",
                    Title = "Scene Regeneration Failed",
                    Status = 500,
                    Detail = result.ErrorMessage ?? "Failed to regenerate scene",
                    Extensions =
                    {
                        ["correlationId"] = correlationId,
                        ["errorCode"] = result.ErrorCode
                    }
                });
            }

            var lines = result.Script.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var newNarration = lines.FirstOrDefault(l => l.Length > 10)?.Trim() ?? scene.Narration;

            var updatedScene = scene with
            {
                Narration = newNarration,
                VisualPrompt = $"Visual for: {newNarration.Substring(0, Math.Min(50, newNarration.Length))}"
            };

            var updatedScenes = script.Scenes.Select(s => s.Number == sceneNumber ? updatedScene : s).ToList();
            var updatedScript = script with { Scenes = updatedScenes };

            _scriptStore[id] = updatedScript;

            _logger.LogInformation(
                "[{CorrelationId}] Scene {SceneNumber} regenerated successfully in script {ScriptId}",
                correlationId, sceneNumber, id);

            var response = MapScriptToResponse(id, updatedScript);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{CorrelationId}] Error regenerating scene", correlationId);

            return StatusCode(500, new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E500",
                Title = "Internal Server Error",
                Status = 500,
                Detail = "An error occurred while regenerating the scene",
                Extensions = { ["correlationId"] = correlationId }
            });
        }
    }

    /// <summary>
    /// Regenerate a script with same or different provider
    /// </summary>
    [HttpPost("{id}/regenerate")]
    public async Task<IActionResult> RegenerateScript(
        string id,
        [FromBody] RegenerateScriptRequest request,
        CancellationToken ct)
    {
        var correlationId = HttpContext.TraceIdentifier;

        if (!_scriptStore.TryGetValue(id, out var oldScript))
        {
            return NotFound(new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E404",
                Title = "Script Not Found",
                Status = 404,
                Detail = $"Script with ID '{id}' was not found",
                Extensions = { ["correlationId"] = correlationId }
            });
        }

        _logger.LogInformation(
            "[{CorrelationId}] Regenerating script {ScriptId} with provider {Provider}",
            correlationId, id, request.PreferredProvider ?? "auto");

        try
        {
            var brief = new Brief(
                Topic: oldScript.Title,
                Audience: null,
                Goal: null,
                Tone: "Conversational",
                Language: "en",
                Aspect: Core.Models.Aspect.Widescreen16x9);

            var planSpec = new PlanSpec(
                TargetDuration: oldScript.TotalDuration,
                Pacing: Core.Models.Pacing.Conversational,
                Density: Core.Models.Density.Balanced,
                Style: "Modern");

            var preferredTier = request.PreferredProvider ?? oldScript.Metadata.ProviderName;

            var result = await _scriptOrchestrator.GenerateScriptAsync(
                brief, planSpec, preferredTier, offlineOnly: false, ct).ConfigureAwait(false);

            if (!result.Success || result.Script == null)
            {
                _logger.LogWarning(
                    "[{CorrelationId}] Script regeneration failed: {ErrorCode} - {ErrorMessage}",
                    correlationId, result.ErrorCode, result.ErrorMessage);

                return StatusCode(500, new ProblemDetails
                {
                    Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E300",
                    Title = "Script Regeneration Failed",
                    Status = 500,
                    Detail = result.ErrorMessage ?? "Failed to regenerate script",
                    Extensions =
                    {
                        ["correlationId"] = correlationId,
                        ["errorCode"] = result.ErrorCode
                    }
                });
            }

            var script = ParseScriptFromText(result.Script, planSpec, result.ProviderUsed ?? "Unknown");
            script = script with { CorrelationId = correlationId };

            script = _scriptProcessor.ValidateSceneTiming(script, planSpec.TargetDuration);
            script = _scriptProcessor.OptimizeNarrationFlow(script);
            script = _scriptProcessor.ApplyTransitions(script, planSpec.Style);

            _scriptStore[id] = script;

            _logger.LogInformation(
                "[{CorrelationId}] Script regenerated successfully with provider {Provider}, ID: {ScriptId}",
                correlationId, result.ProviderUsed, id);

            var response = MapScriptToResponse(id, script);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{CorrelationId}] Error regenerating script", correlationId);

            return StatusCode(500, new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E500",
                Title = "Internal Server Error",
                Status = 500,
                Detail = "An error occurred while regenerating the script",
                Extensions = { ["correlationId"] = correlationId }
            });
        }
    }

    /// <summary>
    /// List available LLM providers and their status
    /// </summary>
    [HttpGet("providers")]
    public async Task<IActionResult> ListProviders(CancellationToken ct)
    {
        var correlationId = HttpContext.TraceIdentifier;

        _logger.LogInformation("[{CorrelationId}] GET /api/scripts/providers", correlationId);

        // Check Ollama availability and get model info
        var ollamaAvailable = false;
        var ollamaModelName = "llama2";
        var ollamaModels = new List<string> { "llama2", "mistral", "codellama" };

        try
        {
            var ollamaDetectionService = _serviceProvider?.GetService<OllamaDetectionService>();
            if (ollamaDetectionService != null)
            {
                var ollamaStatus = await ollamaDetectionService.GetStatusAsync(ct).ConfigureAwait(false);
                ollamaAvailable = ollamaStatus.IsRunning;

                if (ollamaAvailable)
                {
                    var models = await ollamaDetectionService.GetModelsAsync(ct).ConfigureAwait(false);
                    if (models.Count > 0)
                    {
                        ollamaModels = models.Select(m => m.Name).ToList();
                        // Get the configured model from settings, or use the first available model
                        var providerSettings = _serviceProvider?.GetService<Aura.Core.Configuration.ProviderSettings>();
                        if (providerSettings != null)
                        {
                            var configuredModel = providerSettings.GetOllamaModel();
                            if (!string.IsNullOrWhiteSpace(configuredModel) && ollamaModels.Contains(configuredModel))
                            {
                                ollamaModelName = configuredModel;
                            }
                            else
                            {
                                ollamaModelName = ollamaModels.First();
                            }
                        }
                        else
                        {
                            ollamaModelName = ollamaModels.First();
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{CorrelationId}] Error checking Ollama availability", correlationId);
        }

        // Check other providers availability
        var openAiAvailable = false;
        var geminiAvailable = false;

        try
        {
            var keyStore = _serviceProvider?.GetService<Aura.Core.Configuration.IKeyStore>();
            if (keyStore != null)
            {
                var allKeys = keyStore.GetAllKeys();
                openAiAvailable = allKeys.ContainsKey("openai") && !string.IsNullOrWhiteSpace(allKeys["openai"]);
                geminiAvailable = allKeys.ContainsKey("google") && !string.IsNullOrWhiteSpace(allKeys["google"]);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{CorrelationId}] Error checking API key availability", correlationId);
        }

        var providers = new List<ProviderInfoDto>
        {
            new()
            {
                Name = "RuleBased",
                Tier = "Free",
                IsAvailable = true,
                RequiresInternet = false,
                RequiresApiKey = false,
                Capabilities = new List<string> { "offline", "deterministic", "template-based" },
                DefaultModel = "template-v1",
                EstimatedCostPer1KTokens = 0,
                AvailableModels = new List<string> { "template-v1" }
            },
            new()
            {
                Name = ollamaAvailable ? $"Ollama ({ollamaModelName})" : "Ollama",
                Tier = "Free",
                IsAvailable = ollamaAvailable,
                RequiresInternet = false,
                RequiresApiKey = false,
                Capabilities = new List<string> { "offline", "local", "customizable" },
                DefaultModel = ollamaModelName,
                EstimatedCostPer1KTokens = 0,
                AvailableModels = ollamaModels
            },
            new()
            {
                Name = "OpenAI",
                Tier = "Pro",
                IsAvailable = openAiAvailable,
                RequiresInternet = true,
                RequiresApiKey = true,
                Capabilities = new List<string> { "streaming", "function-calling", "json-mode" },
                DefaultModel = "gpt-4o-mini",
                EstimatedCostPer1KTokens = 0.0015m,
                AvailableModels = new List<string> { "gpt-4o", "gpt-4o-mini", "gpt-4-turbo", "gpt-3.5-turbo" }
            },
            new()
            {
                Name = "Gemini",
                Tier = "Pro",
                IsAvailable = geminiAvailable,
                RequiresInternet = true,
                RequiresApiKey = true,
                Capabilities = new List<string> { "streaming", "multimodal", "long-context" },
                DefaultModel = "gemini-pro",
                EstimatedCostPer1KTokens = 0.00025m,
                AvailableModels = new List<string> { "gemini-pro", "gemini-ultra" }
            }
        };

        return Ok(new { providers, correlationId });
    }

    private Script ParseScriptFromText(string scriptText, PlanSpec planSpec, string provider, string modelUsed = "provider-default")
    {
        if (string.IsNullOrWhiteSpace(scriptText))
        {
            throw new ArgumentException("Script text cannot be null or empty", nameof(scriptText));
        }

        var lines = scriptText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        if (lines.Length == 0)
        {
            throw new ArgumentException("Script text contains no valid lines", nameof(scriptText));
        }
        
        var title = lines.FirstOrDefault()?.Trim() ?? "Untitled Script";

        if (title.StartsWith("Title:", StringComparison.OrdinalIgnoreCase))
        {
            title = title.Substring(6).Trim();
        }

        var scenes = new List<ScriptScene>();
        var sceneNumber = 1;
        var totalDuration = planSpec.TargetDuration;
        var sceneDuration = TimeSpan.FromSeconds(totalDuration.TotalSeconds / Math.Max(1, lines.Length / 3));

        foreach (var line in lines.Skip(1))
        {
            if (!string.IsNullOrWhiteSpace(line) && line.Length > 10)
            {
                scenes.Add(new ScriptScene
                {
                    Number = sceneNumber++,
                    Narration = line.Trim(),
                    VisualPrompt = $"Visual for: {line.Trim().Substring(0, Math.Min(50, line.Length))}",
                    Duration = sceneDuration,
                    Transition = TransitionType.Cut
                });
            }
        }

        if (scenes.Count == 0)
        {
            scenes.Add(new ScriptScene
            {
                Number = 1,
                Narration = scriptText.Trim(),
                VisualPrompt = "Visual representation of script",
                Duration = totalDuration,
                Transition = TransitionType.Cut
            });
        }

        return new Script
        {
            Title = title,
            Scenes = scenes,
            TotalDuration = totalDuration,
            Metadata = new ScriptMetadata
            {
                GeneratedAt = DateTime.UtcNow,
                ProviderName = provider,
                ModelUsed = modelUsed,
                TokensUsed = scriptText.Length / 4,
                EstimatedCost = 0,
                Tier = provider == "RuleBased" ? Aura.Core.Models.Generation.ProviderTier.Free : Aura.Core.Models.Generation.ProviderTier.Pro
            }
        };
    }

    private GenerateScriptResponse MapScriptToResponse(string scriptId, Script script)
    {
        if (script == null)
        {
            throw new ArgumentNullException(nameof(script), "Script cannot be null");
        }

        if (script.Scenes == null || script.Scenes.Count == 0)
        {
            throw new InvalidOperationException($"Script {scriptId} has no scenes to map");
        }

        return new GenerateScriptResponse
        {
            ScriptId = scriptId,
            Title = script.Title ?? "Untitled Script",
            Scenes = script.Scenes.Select(s => new ScriptSceneDto
            {
                Number = s.Number,
                Narration = s.Narration ?? string.Empty,
                VisualPrompt = s.VisualPrompt ?? string.Empty,
                DurationSeconds = s.Duration.TotalSeconds,
                Transition = s.Transition.ToString()
            }).ToList(),
            TotalDurationSeconds = script.TotalDuration.TotalSeconds,
            Metadata = new ScriptMetadataDto
            {
                GeneratedAt = script.Metadata?.GeneratedAt ?? DateTime.UtcNow,
                ProviderName = script.Metadata?.ProviderName ?? "Unknown",
                ModelUsed = script.Metadata?.ModelUsed ?? "unknown",
                TokensUsed = script.Metadata?.TokensUsed ?? 0,
                EstimatedCost = script.Metadata?.EstimatedCost ?? 0,
                Tier = script.Metadata?.Tier.ToString() ?? "Free",
                GenerationTimeSeconds = script.Metadata?.GenerationTime.TotalSeconds ?? 0
            },
            CorrelationId = script.CorrelationId ?? string.Empty
        };
    }

    private Core.Models.Aspect ParseAspect(string aspect)
    {
        return aspect switch
        {
            "16:9" => Core.Models.Aspect.Widescreen16x9,
            "9:16" => Core.Models.Aspect.Vertical9x16,
            "1:1" => Core.Models.Aspect.Square1x1,
            _ => Core.Models.Aspect.Widescreen16x9
        };
    }

    private Core.Models.Pacing ParsePacing(string pacing)
    {
        return pacing.ToLowerInvariant() switch
        {
            "chill" => Core.Models.Pacing.Chill,
            "conversational" => Core.Models.Pacing.Conversational,
            "fast" => Core.Models.Pacing.Fast,
            _ => Core.Models.Pacing.Conversational
        };
    }

    private Core.Models.Density ParseDensity(string density)
    {
        return density.ToLowerInvariant() switch
        {
            "sparse" => Core.Models.Density.Sparse,
            "balanced" => Core.Models.Density.Balanced,
            "dense" => Core.Models.Density.Dense,
            _ => Core.Models.Density.Balanced
        };
    }

    /// <summary>
    /// Export script as text or markdown
    /// </summary>
    [HttpGet("{id}/export")]
    public IActionResult ExportScript(string id, [FromQuery] string format = "text")
    {
        var correlationId = HttpContext.TraceIdentifier;

        if (!_scriptStore.TryGetValue(id, out var script))
        {
            return NotFound(new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E404",
                Title = "Script Not Found",
                Status = 404,
                Detail = $"Script with ID '{id}' was not found",
                Extensions = { ["correlationId"] = correlationId }
            });
        }

        var content = format.ToLowerInvariant() switch
        {
            "markdown" => ExportAsMarkdown(script),
            "text" => ExportAsText(script),
            _ => ExportAsText(script)
        };

        var filename = $"{script.Title.Replace(" ", "_")}_{DateTime.UtcNow:yyyyMMdd}.{(format == "markdown" ? "md" : "txt")}";

        _logger.LogInformation(
            "[{CorrelationId}] Exporting script {ScriptId} as {Format}",
            correlationId, id, format);

        return File(
            System.Text.Encoding.UTF8.GetBytes(content),
            "text/plain",
            filename);
    }

    private string ExportAsText(Script script)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(script.Title);
        sb.AppendLine(new string('=', script.Title.Length));
        sb.AppendLine();
        sb.AppendLine($"Total Duration: {script.TotalDuration.TotalMinutes:F2} minutes");
        sb.AppendLine($"Provider: {script.Metadata.ProviderName}");
        sb.AppendLine($"Generated: {script.Metadata.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();
        sb.AppendLine(new string('-', 60));
        sb.AppendLine();

        foreach (var scene in script.Scenes)
        {
            sb.AppendLine($"Scene {scene.Number} ({scene.Duration.TotalSeconds:F1}s)");
            sb.AppendLine();
            sb.AppendLine(scene.Narration);
            sb.AppendLine();
            sb.AppendLine($"Visual: {scene.VisualPrompt}");
            sb.AppendLine();
            sb.AppendLine(new string('-', 60));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private string ExportAsMarkdown(Script script)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# {script.Title}");
        sb.AppendLine();
        sb.AppendLine("## Metadata");
        sb.AppendLine();
        sb.AppendLine($"- **Duration**: {script.TotalDuration.TotalMinutes:F2} minutes");
        sb.AppendLine($"- **Provider**: {script.Metadata.ProviderName}");
        sb.AppendLine($"- **Model**: {script.Metadata.ModelUsed}");
        sb.AppendLine($"- **Generated**: {script.Metadata.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        foreach (var scene in script.Scenes)
        {
            sb.AppendLine($"## Scene {scene.Number}");
            sb.AppendLine();
            sb.AppendLine($"**Duration**: {scene.Duration.TotalSeconds:F1}s | **Transition**: {scene.Transition}");
            sb.AppendLine();
            sb.AppendLine("### Narration");
            sb.AppendLine();
            sb.AppendLine(scene.Narration);
            sb.AppendLine();
            sb.AppendLine("### Visual");
            sb.AppendLine();
            sb.AppendLine($"> {scene.VisualPrompt}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Enhance/improve a script
    /// </summary>
    [HttpPost("{id}/enhance")]
    public Task<IActionResult> EnhanceScript(
        string id,
        [FromBody] ScriptEnhancementRequest request,
        CancellationToken ct)
    {
        var correlationId = HttpContext.TraceIdentifier;

        if (!_scriptStore.TryGetValue(id, out var script))
        {
            return Task.FromResult<IActionResult>(NotFound(new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E404",
                Title = "Script Not Found",
                Status = 404,
                Detail = $"Script with ID '{id}' was not found",
                Extensions = { ["correlationId"] = correlationId }
            }));
        }

        _logger.LogInformation(
            "[{CorrelationId}] Enhancing script {ScriptId} with goal: {Goal}",
            correlationId, id, request.Goal);

        try
        {
            var enhancedScript = script;

            if (request.ToneAdjustment.HasValue && request.ToneAdjustment.Value != 0)
            {
                var adjustedScenes = script.Scenes.Select(scene =>
                {
                    var narration = scene.Narration;
                    if (request.ToneAdjustment.Value > 0)
                    {
                        narration = narration.Replace(".", "!").Replace(",", "!");
                    }
                    return scene with { Narration = narration };
                }).ToList();

                enhancedScript = enhancedScript with { Scenes = adjustedScenes };
            }

            if (request.PacingAdjustment.HasValue && request.PacingAdjustment.Value != 0)
            {
                var adjustedScenes = enhancedScript.Scenes.Select(scene =>
                {
                    var duration = scene.Duration;
                    var adjustmentFactor = 1.0 + (request.PacingAdjustment.Value * 0.3);
                    var newDuration = TimeSpan.FromSeconds(duration.TotalSeconds * adjustmentFactor);
                    return scene with { Duration = newDuration };
                }).ToList();

                enhancedScript = enhancedScript with { Scenes = adjustedScenes };
            }

            SaveVersion(id, script, "Before enhancement");

            _scriptStore[id] = enhancedScript;

            _logger.LogInformation(
                "[{CorrelationId}] Script {ScriptId} enhanced successfully",
                correlationId, id);

            var response = MapScriptToResponse(id, enhancedScript);
            return Task.FromResult<IActionResult>(Ok(response));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{CorrelationId}] Error enhancing script", correlationId);

            return Task.FromResult<IActionResult>(StatusCode(500, new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E500",
                Title = "Internal Server Error",
                Status = 500,
                Detail = "An error occurred while enhancing the script",
                Extensions = { ["correlationId"] = correlationId }
            }));
        }
    }

    /// <summary>
    /// Reorder scenes in a script
    /// </summary>
    [HttpPost("{id}/reorder")]
    public IActionResult ReorderScenes(
        string id,
        [FromBody] ReorderScenesRequest request)
    {
        var correlationId = HttpContext.TraceIdentifier;

        if (!_scriptStore.TryGetValue(id, out var script))
        {
            return NotFound(new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E404",
                Title = "Script Not Found",
                Status = 404,
                Detail = $"Script with ID '{id}' was not found",
                Extensions = { ["correlationId"] = correlationId }
            });
        }

        if (request.SceneOrder.Count != script.Scenes.Count)
        {
            return BadRequest(new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E400",
                Title = "Invalid Request",
                Status = 400,
                Detail = "Scene order must contain all scenes",
                Extensions = { ["correlationId"] = correlationId }
            });
        }

        SaveVersion(id, script, "Before reordering");

        var reorderedScenes = new List<ScriptScene>();
        var sceneNumber = 1;

        foreach (var oldNumber in request.SceneOrder)
        {
            var scene = script.Scenes.FirstOrDefault(s => s.Number == oldNumber);
            if (scene != null)
            {
                reorderedScenes.Add(scene with { Number = sceneNumber++ });
            }
        }

        var updatedScript = script with { Scenes = reorderedScenes };
        _scriptStore[id] = updatedScript;

        _logger.LogInformation(
            "[{CorrelationId}] Reordered scenes in script {ScriptId}",
            correlationId, id);

        var response = MapScriptToResponse(id, updatedScript);
        return Ok(response);
    }

    /// <summary>
    /// Merge multiple scenes into one
    /// </summary>
    [HttpPost("{id}/merge")]
    public IActionResult MergeScenes(
        string id,
        [FromBody] MergeScenesRequest request)
    {
        var correlationId = HttpContext.TraceIdentifier;

        if (!_scriptStore.TryGetValue(id, out var script))
        {
            return NotFound(new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E404",
                Title = "Script Not Found",
                Status = 404,
                Detail = $"Script with ID '{id}' was not found",
                Extensions = { ["correlationId"] = correlationId }
            });
        }

        if (request.SceneNumbers.Count < 2)
        {
            return BadRequest(new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E400",
                Title = "Invalid Request",
                Status = 400,
                Detail = "Must specify at least 2 scenes to merge",
                Extensions = { ["correlationId"] = correlationId }
            });
        }

        SaveVersion(id, script, "Before merging scenes");

        var scenesToMerge = script.Scenes.Where(s => request.SceneNumbers.Contains(s.Number)).OrderBy(s => s.Number).ToList();

        if (scenesToMerge.Count != request.SceneNumbers.Count)
        {
            return BadRequest(new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E400",
                Title = "Invalid Request",
                Status = 400,
                Detail = "One or more specified scenes not found",
                Extensions = { ["correlationId"] = correlationId }
            });
        }

        var mergedNarration = string.Join(request.Separator, scenesToMerge.Select(s => s.Narration));
        var mergedVisualPrompt = string.Join("; ", scenesToMerge.Select(s => s.VisualPrompt));
        var mergedDuration = TimeSpan.FromSeconds(scenesToMerge.Sum(s => s.Duration.TotalSeconds));

        var mergedScene = new ScriptScene
        {
            Number = scenesToMerge.First().Number,
            Narration = mergedNarration,
            VisualPrompt = mergedVisualPrompt,
            Duration = mergedDuration,
            Transition = scenesToMerge.First().Transition
        };

        var newScenes = script.Scenes.Where(s => !request.SceneNumbers.Contains(s.Number)).ToList();
        newScenes.Add(mergedScene);
        newScenes = newScenes.OrderBy(s => s.Number).Select((s, index) => s with { Number = index + 1 }).ToList();

        var updatedScript = script with { Scenes = newScenes };
        _scriptStore[id] = updatedScript;

        _logger.LogInformation(
            "[{CorrelationId}] Merged {Count} scenes in script {ScriptId}",
            correlationId, request.SceneNumbers.Count, id);

        var response = MapScriptToResponse(id, updatedScript);
        return Ok(response);
    }

    /// <summary>
    /// Split a scene into two
    /// </summary>
    [HttpPost("{id}/scenes/{sceneNumber}/split")]
    public IActionResult SplitScene(
        string id,
        int sceneNumber,
        [FromBody] SplitSceneRequest request)
    {
        var correlationId = HttpContext.TraceIdentifier;

        if (!_scriptStore.TryGetValue(id, out var script))
        {
            return NotFound(new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E404",
                Title = "Script Not Found",
                Status = 404,
                Detail = $"Script with ID '{id}' was not found",
                Extensions = { ["correlationId"] = correlationId }
            });
        }

        var scene = script.Scenes.FirstOrDefault(s => s.Number == sceneNumber);
        if (scene == null)
        {
            return NotFound(new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E404",
                Title = "Scene Not Found",
                Status = 404,
                Detail = $"Scene {sceneNumber} not found in script '{id}'",
                Extensions = { ["correlationId"] = correlationId }
            });
        }

        if (request.SplitPosition <= 0 || request.SplitPosition >= scene.Narration.Length)
        {
            return BadRequest(new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E400",
                Title = "Invalid Request",
                Status = 400,
                Detail = "Split position must be within narration text",
                Extensions = { ["correlationId"] = correlationId }
            });
        }

        SaveVersion(id, script, "Before splitting scene");

        var firstPart = scene.Narration.Substring(0, request.SplitPosition).Trim();
        var secondPart = scene.Narration.Substring(request.SplitPosition).Trim();

        var wordCountFirst = firstPart.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var wordCountTotal = scene.Narration.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var durationRatio = (double)wordCountFirst / wordCountTotal;

        var firstDuration = TimeSpan.FromSeconds(scene.Duration.TotalSeconds * durationRatio);
        var secondDuration = TimeSpan.FromSeconds(scene.Duration.TotalSeconds * (1 - durationRatio));

        var firstScene = scene with
        {
            Narration = firstPart,
            Duration = firstDuration
        };

        var secondScene = scene with
        {
            Number = sceneNumber + 1,
            Narration = secondPart,
            Duration = secondDuration
        };

        var newScenes = new List<ScriptScene>();
        foreach (var s in script.Scenes)
        {
            if (s.Number == sceneNumber)
            {
                newScenes.Add(firstScene);
                newScenes.Add(secondScene);
            }
            else if (s.Number > sceneNumber)
            {
                newScenes.Add(s with { Number = s.Number + 1 });
            }
            else
            {
                newScenes.Add(s);
            }
        }

        var updatedScript = script with { Scenes = newScenes };
        _scriptStore[id] = updatedScript;

        _logger.LogInformation(
            "[{CorrelationId}] Split scene {SceneNumber} in script {ScriptId}",
            correlationId, sceneNumber, id);

        var response = MapScriptToResponse(id, updatedScript);
        return Ok(response);
    }

    /// <summary>
    /// Delete a scene from the script
    /// </summary>
    [HttpDelete("{id}/scenes/{sceneNumber}")]
    public IActionResult DeleteScene(string id, int sceneNumber)
    {
        var correlationId = HttpContext.TraceIdentifier;

        if (!_scriptStore.TryGetValue(id, out var script))
        {
            return NotFound(new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E404",
                Title = "Script Not Found",
                Status = 404,
                Detail = $"Script with ID '{id}' was not found",
                Extensions = { ["correlationId"] = correlationId }
            });
        }

        var scene = script.Scenes.FirstOrDefault(s => s.Number == sceneNumber);
        if (scene == null)
        {
            return NotFound(new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E404",
                Title = "Scene Not Found",
                Status = 404,
                Detail = $"Scene {sceneNumber} not found in script '{id}'",
                Extensions = { ["correlationId"] = correlationId }
            });
        }

        if (script.Scenes.Count == 1)
        {
            return BadRequest(new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E400",
                Title = "Invalid Request",
                Status = 400,
                Detail = "Cannot delete the last scene in a script",
                Extensions = { ["correlationId"] = correlationId }
            });
        }

        SaveVersion(id, script, "Before deleting scene");

        var newScenes = script.Scenes
            .Where(s => s.Number != sceneNumber)
            .Select((s, index) => s with { Number = index + 1 })
            .ToList();

        var updatedScript = script with { Scenes = newScenes };
        _scriptStore[id] = updatedScript;

        _logger.LogInformation(
            "[{CorrelationId}] Deleted scene {SceneNumber} from script {ScriptId}",
            correlationId, sceneNumber, id);

        var response = MapScriptToResponse(id, updatedScript);
        return Ok(response);
    }

    /// <summary>
    /// Regenerate all scenes in a script
    /// </summary>
    [HttpPost("{id}/regenerate-all")]
    public async Task<IActionResult> RegenerateAllScenes(
        string id,
        CancellationToken ct)
    {
        var correlationId = HttpContext.TraceIdentifier;

        if (!_scriptStore.TryGetValue(id, out var script))
        {
            return NotFound(new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E404",
                Title = "Script Not Found",
                Status = 404,
                Detail = $"Script with ID '{id}' was not found",
                Extensions = { ["correlationId"] = correlationId }
            });
        }

        _logger.LogInformation(
            "[{CorrelationId}] Regenerating all scenes in script {ScriptId}",
            correlationId, id);

        SaveVersion(id, script, "Before regenerating all scenes");

        try
        {
            var brief = new Brief(
                Topic: script.Title,
                Audience: null,
                Goal: null,
                Tone: "Conversational",
                Language: "en",
                Aspect: Core.Models.Aspect.Widescreen16x9);

            var planSpec = new PlanSpec(
                TargetDuration: script.TotalDuration,
                Pacing: Core.Models.Pacing.Conversational,
                Density: Core.Models.Density.Balanced,
                Style: "Modern");

            var result = await _scriptOrchestrator.GenerateScriptAsync(
                brief, planSpec, script.Metadata.ProviderName, offlineOnly: false, ct).ConfigureAwait(false);

            if (!result.Success || result.Script == null)
            {
                _logger.LogWarning(
                    "[{CorrelationId}] Regenerate all failed: {ErrorCode} - {ErrorMessage}",
                    correlationId, result.ErrorCode, result.ErrorMessage);

                return StatusCode(500, new ProblemDetails
                {
                    Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E300",
                    Title = "Regeneration Failed",
                    Status = 500,
                    Detail = result.ErrorMessage ?? "Failed to regenerate script",
                    Extensions =
                    {
                        ["correlationId"] = correlationId,
                        ["errorCode"] = result.ErrorCode
                    }
                });
            }

            var newScript = ParseScriptFromText(result.Script, planSpec, result.ProviderUsed ?? "Unknown");
            newScript = newScript with { CorrelationId = correlationId };

            newScript = _scriptProcessor.ValidateSceneTiming(newScript, planSpec.TargetDuration);
            newScript = _scriptProcessor.OptimizeNarrationFlow(newScript);
            newScript = _scriptProcessor.ApplyTransitions(newScript, planSpec.Style);

            _scriptStore[id] = newScript;

            _logger.LogInformation(
                "[{CorrelationId}] All scenes regenerated successfully in script {ScriptId}",
                correlationId, id);

            var response = MapScriptToResponse(id, newScript);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{CorrelationId}] Error regenerating all scenes", correlationId);

            return StatusCode(500, new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E500",
                Title = "Internal Server Error",
                Status = 500,
                Detail = "An error occurred while regenerating the script",
                Extensions = { ["correlationId"] = correlationId }
            });
        }
    }

    /// <summary>
    /// Get version history for a script
    /// </summary>
    [HttpGet("{id}/versions")]
    public IActionResult GetVersionHistory(string id)
    {
        var correlationId = HttpContext.TraceIdentifier;

        if (!_scriptStore.TryGetValue(id, out _))
        {
            return NotFound(new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E404",
                Title = "Script Not Found",
                Status = 404,
                Detail = $"Script with ID '{id}' was not found",
                Extensions = { ["correlationId"] = correlationId }
            });
        }

        var versions = _versionStore.GetOrAdd(id, _ => new List<ScriptVersionDto>());

        var response = new ScriptVersionHistoryResponse
        {
            Versions = versions,
            CurrentVersionId = versions.LastOrDefault()?.VersionId ?? string.Empty,
            CorrelationId = correlationId
        };

        _logger.LogInformation(
            "[{CorrelationId}] Retrieved {Count} versions for script {ScriptId}",
            correlationId, versions.Count, id);

        return Ok(response);
    }

    /// <summary>
    /// Revert to a previous version
    /// </summary>
    [HttpPost("{id}/versions/revert")]
    public IActionResult RevertToVersion(
        string id,
        [FromBody] RevertToVersionRequest request)
    {
        var correlationId = HttpContext.TraceIdentifier;

        if (!_scriptStore.TryGetValue(id, out var currentScript))
        {
            return NotFound(new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E404",
                Title = "Script Not Found",
                Status = 404,
                Detail = $"Script with ID '{id}' was not found",
                Extensions = { ["correlationId"] = correlationId }
            });
        }

        var versions = _versionStore.GetOrAdd(id, _ => new List<ScriptVersionDto>());
        var targetVersion = versions.FirstOrDefault(v => v.VersionId == request.VersionId);

        if (targetVersion == null)
        {
            return NotFound(new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E404",
                Title = "Version Not Found",
                Status = 404,
                Detail = $"Version '{request.VersionId}' not found",
                Extensions = { ["correlationId"] = correlationId }
            });
        }

        SaveVersion(id, currentScript, "Before reverting to version " + targetVersion.VersionNumber);

        var restoredScript = ParseScriptFromResponse(targetVersion.Script);
        _scriptStore[id] = restoredScript;

        _logger.LogInformation(
            "[{CorrelationId}] Reverted script {ScriptId} to version {VersionNumber}",
            correlationId, id, targetVersion.VersionNumber);

        var response = MapScriptToResponse(id, restoredScript);
        return Ok(response);
    }

    private void SaveVersion(string scriptId, Script script, string notes)
    {
        var versions = _versionStore.GetOrAdd(scriptId, _ => new List<ScriptVersionDto>());

        var versionNumber = versions.Count + 1;
        var versionId = Guid.NewGuid().ToString();

        var version = new ScriptVersionDto
        {
            VersionId = versionId,
            VersionNumber = versionNumber,
            CreatedAt = DateTime.UtcNow,
            Notes = notes,
            Script = MapScriptToResponse(scriptId, script)
        };

        versions.Add(version);

        if (versions.Count > 50)
        {
            versions.RemoveAt(0);
        }
    }

    private Script ParseScriptFromResponse(GenerateScriptResponse response)
    {
        var scenes = response.Scenes.Select(s => new ScriptScene
        {
            Number = s.Number,
            Narration = s.Narration,
            VisualPrompt = s.VisualPrompt,
            Duration = TimeSpan.FromSeconds(s.DurationSeconds),
            Transition = Enum.TryParse<TransitionType>(s.Transition, out var transition)
                ? transition
                : TransitionType.Cut
        }).ToList();

        return new Script
        {
            Title = response.Title,
            Scenes = scenes,
            TotalDuration = TimeSpan.FromSeconds(response.TotalDurationSeconds),
            Metadata = new ScriptMetadata
            {
                GeneratedAt = response.Metadata.GeneratedAt,
                ProviderName = response.Metadata.ProviderName,
                ModelUsed = response.Metadata.ModelUsed,
                TokensUsed = response.Metadata.TokensUsed,
                EstimatedCost = response.Metadata.EstimatedCost,
                Tier = Enum.TryParse<Aura.Core.Models.Generation.ProviderTier>(response.Metadata.Tier, out var tier)
                    ? tier
                    : Aura.Core.Models.Generation.ProviderTier.Free,
                GenerationTime = TimeSpan.FromSeconds(response.Metadata.GenerationTimeSeconds)
            },
            CorrelationId = response.CorrelationId
        };
    }

    /// <summary>
    /// Generate script with streaming support for real-time updates (SSE)
    /// </summary>
    [HttpPost("generate/stream")]
    public async Task StreamGenerateScript(
        [FromBody] GenerateScriptRequest request,
        CancellationToken ct)
    {
        var correlationId = HttpContext.TraceIdentifier;

        try
        {
            _logger.LogInformation(
                "[{CorrelationId}] POST /api/scripts/generate/stream - Topic: {Topic}, Provider: {Provider}",
                correlationId, request.Topic, request.PreferredProvider ?? "auto");

            Response.ContentType = "text/event-stream";
            Response.Headers.Append("Cache-Control", "no-cache");
            Response.Headers.Append("Connection", "keep-alive");
            Response.Headers.Append("X-Accel-Buffering", "no");

            var brief = new Brief(
                Topic: request.Topic,
                Audience: request.Audience,
                Goal: request.Goal,
                Tone: request.Tone,
                Language: request.Language,
                Aspect: ParseAspect(request.Aspect));

            var planSpec = new PlanSpec(
                TargetDuration: TimeSpan.FromSeconds(request.TargetDurationSeconds),
                Pacing: ParsePacing(request.Pacing),
                Density: ParseDensity(request.Density),
                Style: request.Style);

            // Get provider using keyed services
            ILlmProvider? provider = null;
            string providerName = request.PreferredProvider ?? "Ollama"; // Default to Ollama for free users

            // Try to get the requested provider
            if (!string.IsNullOrEmpty(request.PreferredProvider))
            {
                provider = HttpContext.RequestServices.GetKeyedService<ILlmProvider>(request.PreferredProvider);
                if (provider == null)
                {
                    _logger.LogWarning("Requested provider {Provider} not available, trying alternatives", request.PreferredProvider);
                }
            }

            // Fallback chain: try Ollama -> RuleBased
            if (provider == null)
            {
                provider = HttpContext.RequestServices.GetKeyedService<ILlmProvider>("Ollama");
                providerName = "Ollama";
            }

            if (provider == null)
            {
                provider = HttpContext.RequestServices.GetKeyedService<ILlmProvider>("RuleBased");
                providerName = "RuleBased";
            }

            if (provider == null)
            {
                throw new InvalidOperationException("No LLM providers available. Please configure at least one provider.");
            }

            // Send initial event with provider characteristics
            var characteristics = provider.GetCharacteristics();
            var initEvent = new
            {
                eventType = "init",
                providerName = providerName,
                isLocal = characteristics.IsLocal,
                expectedFirstTokenMs = characteristics.ExpectedFirstTokenMs,
                expectedTokensPerSec = characteristics.ExpectedTokensPerSec,
                costPer1KTokens = characteristics.CostPer1KTokens,
                supportsStreaming = characteristics.SupportsStreaming
            };

            await WriteSSEEvent("init", initEvent, ct).ConfigureAwait(false);

            if (!characteristics.SupportsStreaming || !provider.SupportsStreaming)
            {
                _logger.LogWarning("Provider {Provider} does not support streaming, using fallback", providerName);
                await WriteSSEEvent("error", new { errorMessage = $"Provider {providerName} does not support streaming" }, ct).ConfigureAwait(false);
                return;
            }

            // Stream the generation
            await foreach (var chunk in provider.DraftScriptStreamAsync(brief, planSpec, ct).ConfigureAwait(false))
            {
                if (chunk.ErrorMessage != null)
                {
                    await WriteSSEEvent("error", new { errorMessage = chunk.ErrorMessage }, ct).ConfigureAwait(false);
                    break;
                }

                if (chunk.IsFinal)
                {
                    // Final chunk with metadata
                    var finalEvent = new
                    {
                        eventType = "complete",
                        content = chunk.Content,
                        accumulatedContent = chunk.AccumulatedContent,
                        tokenCount = chunk.TokenIndex,
                        metadata = new
                        {
                            totalTokens = chunk.Metadata?.TotalTokens,
                            estimatedCost = chunk.Metadata?.EstimatedCost,
                            tokensPerSecond = chunk.Metadata?.TokensPerSecond,
                            isLocalModel = chunk.Metadata?.IsLocalModel,
                            modelName = chunk.Metadata?.ModelName,
                            timeToFirstTokenMs = chunk.Metadata?.TimeToFirstTokenMs,
                            totalDurationMs = chunk.Metadata?.TotalDurationMs,
                            finishReason = chunk.Metadata?.FinishReason
                        }
                    };

                    await WriteSSEEvent("complete", finalEvent, ct).ConfigureAwait(false);

                    _logger.LogInformation(
                        "[{CorrelationId}] Streaming generation complete. Provider: {Provider}, Tokens: {Tokens}, Tokens/sec: {TokensPerSec:F2}, Cost: ${Cost:F4}",
                        correlationId,
                        providerName,
                        chunk.TokenIndex,
                        chunk.Metadata?.TokensPerSecond ?? 0.0,
                        chunk.Metadata?.EstimatedCost ?? 0m);
                    break;
                }
                else
                {
                    // Regular chunk
                    var chunkEvent = new
                    {
                        eventType = "chunk",
                        content = chunk.Content,
                        accumulatedContent = chunk.AccumulatedContent,
                        tokenIndex = chunk.TokenIndex
                    };

                    await WriteSSEEvent("chunk", chunkEvent, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[{CorrelationId}] Streaming generation cancelled by client", correlationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{CorrelationId}] Error during streaming generation", correlationId);

            try
            {
                await WriteSSEEvent("error", new
                {
                    errorMessage = "An error occurred during script generation",
                    correlationId = correlationId
                }, ct).ConfigureAwait(false);
            }
            catch
            {
                // Ignore errors writing error event
            }
        }
    }

    /// <summary>
    /// Helper method to write Server-Sent Events
    /// </summary>
    private async Task WriteSSEEvent(string eventType, object data, CancellationToken ct)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(data);
        var message = $"event: {eventType}\ndata: {json}\n\n";
        var bytes = Encoding.UTF8.GetBytes(message);

        await Response.Body.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false);
        await Response.Body.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Generate script with tool calling enabled (research and fact-checking)
    /// </summary>
    [HttpPost("generate-with-tools")]
    public async Task<IActionResult> GenerateScriptWithTools(
        [FromBody] GenerateScriptRequest request,
        CancellationToken ct)
    {
        var correlationId = HttpContext.TraceIdentifier;

        try
        {
            _logger.LogInformation(
                "[{CorrelationId}] POST /api/scripts/generate-with-tools - Topic: {Topic}, EnableTools: true",
                correlationId, request.Topic);

            var brief = new Brief(
                Topic: request.Topic,
                Audience: request.Audience,
                Goal: request.Goal,
                Tone: request.Tone,
                Language: request.Language,
                Aspect: ParseAspect(request.Aspect));

            var planSpec = new PlanSpec(
                TargetDuration: TimeSpan.FromSeconds(request.TargetDurationSeconds),
                Pacing: ParsePacing(request.Pacing),
                Density: ParseDensity(request.Density),
                Style: request.Style);

            var loggerFactory = LoggerFactory.Create(builder =>
                builder.SetMinimumLevel(LogLevel.Information));

            var researchLogger = loggerFactory.CreateLogger<Core.AI.Tools.ScriptResearchTool>();
            var factCheckLogger = loggerFactory.CreateLogger<Core.AI.Tools.FactCheckTool>();

            var tools = new List<Core.AI.Tools.IToolExecutor>
            {
                new Core.AI.Tools.ScriptResearchTool(researchLogger),
                new Core.AI.Tools.FactCheckTool(factCheckLogger)
            };

            var ollamaProvider = new OllamaLlmProvider(
                _logger as ILogger<OllamaLlmProvider> ?? throw new InvalidOperationException("Logger not available"),
                new System.Net.Http.HttpClient(),
                baseUrl: "http://127.0.0.1:11434",
                model: request.Model ?? "llama3.2"
            );

            _logger.LogInformation(
                "[{CorrelationId}] Starting tool-enabled generation with {ToolCount} tools",
                correlationId, tools.Count);

            var result = await ollamaProvider.GenerateWithToolsAsync(
                brief, planSpec, tools, maxToolIterations: 5, ct).ConfigureAwait(false);

            if (!result.Success || string.IsNullOrWhiteSpace(result.GeneratedScript))
            {
                _logger.LogWarning(
                    "[{CorrelationId}] Tool-enabled generation failed: {ErrorMessage}",
                    correlationId, result.ErrorMessage);

                return StatusCode(500, new ProblemDetails
                {
                    Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E300",
                    Title = "Script Generation Failed",
                    Status = 500,
                    Detail = result.ErrorMessage ?? "Failed to generate script with tools",
                    Extensions =
                    {
                        ["correlationId"] = correlationId,
                        ["toolCalls"] = result.TotalToolCalls,
                        ["iterations"] = result.TotalIterations
                    }
                });
            }

            var scriptId = Guid.NewGuid().ToString();
            var script = ParseScriptFromText(result.GeneratedScript, planSpec, "Ollama-Tools");

            script = script with { CorrelationId = correlationId };

            script = _scriptProcessor.ValidateSceneTiming(script, planSpec.TargetDuration);
            script = _scriptProcessor.OptimizeNarrationFlow(script);
            script = _scriptProcessor.ApplyTransitions(script, planSpec.Style);

            _scriptStore[scriptId] = script;

            _logger.LogInformation(
                "[{CorrelationId}] Script generated with tools. ID: {ScriptId}, ToolCalls: {ToolCalls}, Iterations: {Iterations}",
                correlationId, scriptId, result.TotalToolCalls, result.TotalIterations);

            var response = MapScriptToResponse(scriptId, script);

            var enhancedResponse = new
            {
                response.ScriptId,
                response.Title,
                response.Scenes,
                response.TotalDurationSeconds,
                response.Metadata,
                response.CorrelationId,
                ToolUsage = new
                {
                    Enabled = true,
                    TotalToolCalls = result.TotalToolCalls,
                    TotalIterations = result.TotalIterations,
                    GenerationTimeSeconds = result.GenerationTime.TotalSeconds,
                    ToolExecutions = result.ToolExecutionLog.Select(entry => new
                    {
                        entry.ToolName,
                        entry.Arguments,
                        ResultLength = entry.Result.Length,
                        ExecutionTimeMs = entry.ExecutionTime.TotalMilliseconds,
                        entry.Timestamp
                    }).ToList()
                }
            };

            return Ok(enhancedResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{CorrelationId}] Error generating script with tools", correlationId);

            return StatusCode(500, new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E500",
                Title = "Internal Server Error",
                Status = 500,
                Detail = "An error occurred while generating the script with tools",
                Extensions = { ["correlationId"] = correlationId }
            });
        }
    }
}
