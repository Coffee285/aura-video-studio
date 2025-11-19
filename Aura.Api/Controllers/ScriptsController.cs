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
using Aura.Core.Services;
using Aura.Core.Services.Generation;
using Aura.Providers.Llm;
using Microsoft.AspNetCore.Mvc;
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

    public ScriptsController(
        ILogger<ScriptsController> logger,
        ScriptOrchestrator scriptOrchestrator,
        ScriptProcessor scriptProcessor,
        ScriptCacheService cacheService,
        ProviderMixer providerMixer,
        StreamingOrchestrator streamingOrchestrator)
    {
        _logger = logger;
        _scriptOrchestrator = scriptOrchestrator;
        _scriptProcessor = scriptProcessor;
        _cacheService = cacheService;
        _providerMixer = providerMixer;
        _streamingOrchestrator = streamingOrchestrator;
    }

    /// <summary>
    /// Generate a new script using provider selection
    /// </summary>
    [HttpPost("generate")]
    public async Task<IActionResult> GenerateScript(
        [FromBody] GenerateScriptRequest request,
        CancellationToken ct)
    {
        var correlationId = HttpContext.TraceIdentifier;
        
        try
        {
            _logger.LogInformation(
                "[{CorrelationId}] POST /api/scripts/generate - Topic: {Topic}, Provider: {Provider}",
                correlationId, request.Topic, request.PreferredProvider ?? "auto");

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

            var preferredTier = request.PreferredProvider ?? "Free";
            
            var result = await _scriptOrchestrator.GenerateScriptAsync(
                brief, planSpec, preferredTier, offlineOnly: false, ct).ConfigureAwait(false);

            if (!result.Success || result.Script == null)
            {
                _logger.LogWarning(
                    "[{CorrelationId}] Script generation failed: {ErrorCode} - {ErrorMessage}",
                    correlationId, result.ErrorCode, result.ErrorMessage);

                return StatusCode(500, new ProblemDetails
                {
                    Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E300",
                    Title = "Script Generation Failed",
                    Status = 500,
                    Detail = result.ErrorMessage ?? "Failed to generate script",
                    Extensions =
                    {
                        ["correlationId"] = correlationId,
                        ["errorCode"] = result.ErrorCode
                    }
                });
            }

            var scriptId = Guid.NewGuid().ToString();
            var script = ParseScriptFromText(result.Script, planSpec, result.ProviderUsed ?? "Unknown");
            
            script = script with { CorrelationId = correlationId };
            
            script = _scriptProcessor.ValidateSceneTiming(script, planSpec.TargetDuration);
            script = _scriptProcessor.OptimizeNarrationFlow(script);
            script = _scriptProcessor.ApplyTransitions(script, planSpec.Style);

            _scriptStore[scriptId] = script;

            _logger.LogInformation(
                "[{CorrelationId}] Script generated successfully with provider {Provider}, ID: {ScriptId}",
                correlationId, result.ProviderUsed, scriptId);

            var response = MapScriptToResponse(scriptId, script);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{CorrelationId}] Error generating script", correlationId);
            
            return StatusCode(500, new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E500",
                Title = "Internal Server Error",
                Status = 500,
                Detail = "An error occurred while generating the script",
                Extensions = { ["correlationId"] = correlationId }
            });
        }
    }

    /// <summary>
    /// Stream script generation with real-time token-by-token updates (SSE)
    /// </summary>
    [HttpPost("generate-stream")]
    [Produces("text/event-stream")]
    public async Task GenerateScriptStream(
        [FromBody] GenerateScriptRequest request,
        CancellationToken ct)
    {
        var correlationId = HttpContext.TraceIdentifier;
        
        try
        {
            _logger.LogInformation(
                "[{CorrelationId}] POST /api/scripts/generate-stream - Topic: {Topic}, Provider: {Provider}",
                correlationId, request.Topic, request.PreferredProvider ?? "auto");

            // Set SSE headers
            Response.Headers.Append("Content-Type", "text/event-stream");
            Response.Headers.Append("Cache-Control", "no-cache");
            Response.Headers.Append("Connection", "keep-alive");

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

            // First, determine which provider to use
            var result = await _scriptOrchestrator.GenerateScriptAsync(
                brief, planSpec, request.PreferredProvider ?? "Free", offlineOnly: false, ct).ConfigureAwait(false);

            if (!result.Success || string.IsNullOrWhiteSpace(result.ProviderUsed))
            {
                // Send error event
                var errorData = new { type = "error", message = result.ErrorMessage ?? "Failed to generate script" };
                await Response.WriteAsync($"event: error\ndata: {System.Text.Json.JsonSerializer.Serialize(errorData)}\n\n", ct).ConfigureAwait(false);
                await Response.Body.FlushAsync(ct).ConfigureAwait(false);
                return;
            }

            // For now, send the complete result as we don't have direct provider access
            // In a future iteration, we can enhance this with true streaming by accessing providers directly
            _logger.LogInformation(
                "[{CorrelationId}] Script generated successfully with provider {Provider}",
                correlationId, result.ProviderUsed);

            // Send as a single complete event for now
            var completeData = new
            {
                type = "complete",
                content = result.Script ?? string.Empty,
                providerName = result.ProviderUsed,
                metadata = new
                {
                    isLocal = result.ProviderUsed == "Ollama" || result.ProviderUsed == "RuleBased",
                    estimatedCost = result.ProviderUsed == "Ollama" || result.ProviderUsed == "RuleBased" ? 0m : 0.01m
                }
            };

            await Response.WriteAsync($"event: complete\ndata: {System.Text.Json.JsonSerializer.Serialize(completeData)}\n\n", ct).ConfigureAwait(false);
            await Response.Body.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[{CorrelationId}] Streaming cancelled by client", correlationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{CorrelationId}] Error during streaming script generation", correlationId);
            
            try
            {
                var errorData = new { type = "error", message = $"An error occurred: {ex.Message}" };
                await Response.WriteAsync($"event: error\ndata: {System.Text.Json.JsonSerializer.Serialize(errorData)}\n\n", ct).ConfigureAwait(false);
                await Response.Body.FlushAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                // Client may have disconnected, ignore
            }
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
    public Task<IActionResult> ListProviders(CancellationToken ct)
    {
        var correlationId = HttpContext.TraceIdentifier;
        
        _logger.LogInformation("[{CorrelationId}] GET /api/scripts/providers", correlationId);

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
                Name = "Ollama",
                Tier = "Free",
                IsAvailable = false,
                RequiresInternet = false,
                RequiresApiKey = false,
                Capabilities = new List<string> { "offline", "local", "customizable" },
                DefaultModel = "llama2",
                EstimatedCostPer1KTokens = 0,
                AvailableModels = new List<string> { "llama2", "mistral", "codellama" }
            },
            new()
            {
                Name = "OpenAI",
                Tier = "Pro",
                IsAvailable = false,
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
                IsAvailable = false,
                RequiresInternet = true,
                RequiresApiKey = true,
                Capabilities = new List<string> { "streaming", "multimodal", "long-context" },
                DefaultModel = "gemini-pro",
                EstimatedCostPer1KTokens = 0.00025m,
                AvailableModels = new List<string> { "gemini-pro", "gemini-ultra" }
            }
        };

        return Task.FromResult<IActionResult>(Ok(new { providers, correlationId }));
    }

    private Script ParseScriptFromText(string scriptText, PlanSpec planSpec, string provider)
    {
        var lines = scriptText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
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
                ModelUsed = "default",
                TokensUsed = scriptText.Length / 4,
                EstimatedCost = 0,
                Tier = provider == "RuleBased" ? ProviderTier.Free : ProviderTier.Pro
            }
        };
    }

    private GenerateScriptResponse MapScriptToResponse(string scriptId, Script script)
    {
        return new GenerateScriptResponse
        {
            ScriptId = scriptId,
            Title = script.Title,
            Scenes = script.Scenes.Select(s => new ScriptSceneDto
            {
                Number = s.Number,
                Narration = s.Narration,
                VisualPrompt = s.VisualPrompt,
                DurationSeconds = s.Duration.TotalSeconds,
                Transition = s.Transition.ToString()
            }).ToList(),
            TotalDurationSeconds = script.TotalDuration.TotalSeconds,
            Metadata = new ScriptMetadataDto
            {
                GeneratedAt = script.Metadata.GeneratedAt,
                ProviderName = script.Metadata.ProviderName,
                ModelUsed = script.Metadata.ModelUsed,
                TokensUsed = script.Metadata.TokensUsed,
                EstimatedCost = script.Metadata.EstimatedCost,
                Tier = script.Metadata.Tier.ToString(),
                GenerationTimeSeconds = script.Metadata.GenerationTime.TotalSeconds
            },
            CorrelationId = script.CorrelationId
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
                Tier = Enum.TryParse<ProviderTier>(response.Metadata.Tier, out var tier)
                    ? tier
                    : ProviderTier.Free,
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
                "[{CorrelationId}] POST /api/scripts/generate/stream - Topic: {Topic}, Stream: true",
                correlationId, request.Topic);

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

            var ollamaProvider = new OllamaLlmProvider(
                _logger as ILogger<OllamaLlmProvider> ?? throw new InvalidOperationException("Logger not available"),
                new System.Net.Http.HttpClient(),
                baseUrl: "http://127.0.0.1:11434",
                model: request.Model ?? "llama3.1:8b-q4_k_m"
            );

            var streamSource = ollamaProvider.GenerateStreamingAsync(brief, planSpec, ct);

            await foreach (var eventData in _streamingOrchestrator.StreamScriptGenerationAsync(
                streamSource, request.Topic, ct).ConfigureAwait(false))
            {
                var sseMessage = _streamingOrchestrator.FormatAsServerSentEvent(eventData);
                var bytes = Encoding.UTF8.GetBytes(sseMessage);
                
                await Response.Body.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false);
                await Response.Body.FlushAsync(ct).ConfigureAwait(false);

                if (eventData.IsComplete)
                {
                    _logger.LogInformation(
                        "[{CorrelationId}] Streaming generation complete. Tokens: {Tokens}, Tokens/sec: {TokensPerSec:F2}",
                        correlationId,
                        eventData.TokenCount,
                        eventData.TokensPerSecond ?? 0.0);
                    break;
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
            
            var errorEvent = new
            {
                eventType = "error",
                errorMessage = "An error occurred during script generation",
                correlationId = correlationId
            };
            
            var errorMessage = $"event: error\ndata: {System.Text.Json.JsonSerializer.Serialize(errorEvent)}\n\n";
            var bytes = Encoding.UTF8.GetBytes(errorMessage);
            await Response.Body.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false);
            await Response.Body.FlushAsync(ct).ConfigureAwait(false);
        }
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
