using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Aura.Core.Errors;
using Aura.Core.Models;
using Aura.Core.Models.Audio;
using Aura.Core.Models.Generation;
using Aura.Core.Providers;
using Aura.Core.Services;
using Aura.Core.Services.Assets;
using Aura.Core.Services.Audio;
using Aura.Core.Services.Fallback;
using Aura.Core.Services.Generation;
using Aura.Core.Services.Orchestration;
using Aura.Core.Services.PacingServices;
using Aura.Core.Models.Timeline;
using Aura.Core.Utilities;
using Aura.Core.Validation;
using Microsoft.Extensions.Logging;
using LibraryAssetType = Aura.Core.Models.Assets.AssetType;

namespace Aura.Core.Orchestrator;

/// <summary>
/// Orchestrates the video generation pipeline from brief to final render.
/// Implements the stage-by-stage workflow: Plan → Script → TTS → Assets → Compose → Render.
/// </summary>
public class VideoOrchestrator
{
    // Stall detection constants
    private const float StallProgressThreshold = 0.1f;
    private const int StallTimeoutSeconds = 60;

    // Output path extraction constants
    private const string OutputDirectoryName = "AuraVideoStudio";
    private const string OutputSubdirectoryName = "Output";
    private const int RecentFileThresholdMinutes = 10;
    private const string RecoveryAudioNotFound = "NOT_FOUND";

    private readonly ILogger<VideoOrchestrator> _logger;
    private readonly ILlmProvider _llmProvider;
    private readonly ITtsProvider _ttsProvider;
    private readonly IVideoComposer _videoComposer;
    private readonly VideoGenerationOrchestrator _smartOrchestrator;
    private readonly ResourceMonitor _resourceMonitor;
    private readonly IImageProvider? _imageProvider;
    private readonly PreGenerationValidator _preGenerationValidator;
    private readonly ScriptValidator _scriptValidator;
    private readonly ProviderRetryWrapper _retryWrapper;
    private readonly TtsOutputValidator _ttsValidator;
    private readonly ImageOutputValidator _imageValidator;
    private readonly LlmOutputValidator _llmValidator;
    private readonly ResourceCleanupManager _cleanupManager;
    private readonly Services.PacingServices.IntelligentPacingOptimizer? _pacingOptimizer;
    private readonly Services.PacingServices.PacingApplicationService? _pacingApplicationService;
    private readonly Timeline.TimelineBuilder _timelineBuilder;
    private readonly Configuration.ProviderSettings _providerSettings;
    private readonly NarrationOptimizationService? _narrationOptimizationService;
    private readonly PipelineOrchestrationEngine? _pipelineEngine;
    private readonly Services.RAG.RagScriptEnhancer? _ragScriptEnhancer;
    private readonly Telemetry.RunTelemetryCollector _telemetryCollector;
    private readonly Dependencies.FFmpegResolver? _ffmpegResolver;
    private readonly AssetTaggingService? _assetTaggingService;
    private readonly TopicAwareFallbackGenerator _fallbackGenerator;
    private readonly Services.AudioIntelligence.IVoiceEnhancementService? _voiceEnhancementService;
    private readonly Services.AudioIntelligence.IMusicMatchingService? _musicMatchingService;
    private readonly Services.AudioIntelligence.IIntelligentDuckingService? _intelligentDuckingService;

    public VideoOrchestrator(
        ILogger<VideoOrchestrator> logger,
        ILlmProvider llmProvider,
        ITtsProvider ttsProvider,
        IVideoComposer videoComposer,
        VideoGenerationOrchestrator smartOrchestrator,
        ResourceMonitor resourceMonitor,
        PreGenerationValidator preGenerationValidator,
        ScriptValidator scriptValidator,
        ProviderRetryWrapper retryWrapper,
        TtsOutputValidator ttsValidator,
        ImageOutputValidator imageValidator,
        LlmOutputValidator llmValidator,
        ResourceCleanupManager cleanupManager,
        Timeline.TimelineBuilder timelineBuilder,
        Configuration.ProviderSettings providerSettings,
        Telemetry.RunTelemetryCollector telemetryCollector,
        IImageProvider? imageProvider = null,
        Services.PacingServices.IntelligentPacingOptimizer? pacingOptimizer = null,
        Services.PacingServices.PacingApplicationService? pacingApplicationService = null,
        NarrationOptimizationService? narrationOptimizationService = null,
        PipelineOrchestrationEngine? pipelineEngine = null,
        Services.RAG.RagScriptEnhancer? ragScriptEnhancer = null,
        Dependencies.FFmpegResolver? ffmpegResolver = null,
        AssetTaggingService? assetTaggingService = null,
        TopicAwareFallbackGenerator? fallbackGenerator = null,
        Services.AudioIntelligence.IVoiceEnhancementService? voiceEnhancementService = null,
        Services.AudioIntelligence.IMusicMatchingService? musicMatchingService = null,
        Services.AudioIntelligence.IIntelligentDuckingService? intelligentDuckingService = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(llmProvider);
        ArgumentNullException.ThrowIfNull(ttsProvider);
        ArgumentNullException.ThrowIfNull(videoComposer);
        ArgumentNullException.ThrowIfNull(smartOrchestrator);
        ArgumentNullException.ThrowIfNull(resourceMonitor);
        ArgumentNullException.ThrowIfNull(preGenerationValidator);
        ArgumentNullException.ThrowIfNull(scriptValidator);
        ArgumentNullException.ThrowIfNull(retryWrapper);
        ArgumentNullException.ThrowIfNull(ttsValidator);
        ArgumentNullException.ThrowIfNull(imageValidator);
        ArgumentNullException.ThrowIfNull(llmValidator);
        ArgumentNullException.ThrowIfNull(cleanupManager);
        ArgumentNullException.ThrowIfNull(timelineBuilder);
        ArgumentNullException.ThrowIfNull(providerSettings);
        ArgumentNullException.ThrowIfNull(telemetryCollector);

        _logger = logger;
        _llmProvider = llmProvider;
        _ttsProvider = ttsProvider;
        _videoComposer = videoComposer;
        _smartOrchestrator = smartOrchestrator;
        _resourceMonitor = resourceMonitor;
        _imageProvider = imageProvider;
        _preGenerationValidator = preGenerationValidator;
        _scriptValidator = scriptValidator;
        _retryWrapper = retryWrapper;
        _ttsValidator = ttsValidator;
        _imageValidator = imageValidator;
        _llmValidator = llmValidator;
        _cleanupManager = cleanupManager;
        _pacingOptimizer = pacingOptimizer;
        _pacingApplicationService = pacingApplicationService;
        _timelineBuilder = timelineBuilder;
        _providerSettings = providerSettings;
        _telemetryCollector = telemetryCollector;
        _narrationOptimizationService = narrationOptimizationService;
        _pipelineEngine = pipelineEngine;
        _ragScriptEnhancer = ragScriptEnhancer;
        _ffmpegResolver = ffmpegResolver;
        _assetTaggingService = assetTaggingService;
        _fallbackGenerator = fallbackGenerator ?? new TopicAwareFallbackGenerator();
        _voiceEnhancementService = voiceEnhancementService;
        _musicMatchingService = musicMatchingService;
        _intelligentDuckingService = intelligentDuckingService;

        if (_assetTaggingService != null)
        {
            _logger.LogInformation("AssetTaggingService configured for intelligent asset selection");
        }

        if (_voiceEnhancementService != null || _musicMatchingService != null || _intelligentDuckingService != null)
        {
            _logger.LogInformation("Audio Intelligence Suite configured for enhanced audio processing");
        }
    }

    /// <summary>
    /// Execute a pipeline stage with proper error handling, cancellation, and timeout
    /// </summary>
    private async Task<T> ExecuteStageAsync<T>(
        string stageName,
        Func<CancellationToken, Task<T>> stageAction,
        Action<string>? progressCallback,
        CancellationToken ct,
        int timeoutSeconds = 600)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("[{StageName}] Starting stage execution", stageName);

        try
        {
            SafeReportProgress(progressCallback, "Starting " + stageName + "...");

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            var result = await stageAction(linkedCts.Token).ConfigureAwait(false);

            stopwatch.Stop();
            _logger.LogInformation(
                "[{StageName}] Stage completed successfully in {Elapsed}ms",
                stageName, stopwatch.ElapsedMilliseconds);

            SafeReportProgress(progressCallback, stageName + " completed");

            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogWarning("[{StageName}] Stage cancelled by user", stageName);
            throw;
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            var message = "Stage '" + stageName + "' timed out after " + timeoutSeconds + " seconds. " +
                "This may indicate the provider is not responding.";
            var error = new VideoGenerationException(message, stageName, new TimeoutException());

            _logger.LogError(error, "[{StageName}] Stage timed out", stageName);
            throw error;
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            var message = "Stage '" + stageName + "' failed due to network error: " + ex.Message + ". " +
                "Please check that all required services are running.";
            var error = new VideoGenerationException(message, stageName, ex);

            _logger.LogError(error, "[{StageName}] Network error", stageName);
            throw error;
        }
        catch (VideoGenerationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var message = "Stage '" + stageName + "' failed: " + ex.Message;
            var error = new VideoGenerationException(message, stageName, ex);

            _logger.LogError(error, "[{StageName}] Unexpected error after {Elapsed}ms", stageName, stopwatch.ElapsedMilliseconds);
            throw error;
        }
    }

    /// <summary>
    /// Safely report progress without breaking the pipeline if callback throws
    /// </summary>
    private void SafeReportProgress(Action<string>? progressCallback, string message)
    {
        if (progressCallback == null) return;

        try
        {
            progressCallback(message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Progress callback threw exception for message: {Message}", message);
        }
    }

    /// <summary>
    /// Validates that all required services are available and functional before starting generation.
    /// This prevents wasting time on partial generation that will fail.
    /// </summary>
    /// <returns>Tuple of (isValid, errors list)</returns>
    public async Task<(bool isValid, List<string> errors)> ValidatePipelineAsync(
        CancellationToken ct = default)
    {
        var errors = new List<string>();
        _logger.LogInformation("Running pre-flight validation for video generation pipeline");

        // 1. Check LLM Provider
        try
        {
            if (_llmProvider == null)
            {
                errors.Add("LLM Provider not available - cannot generate scripts");
            }
            else
            {
                _logger.LogDebug("✓ LLM Provider available: {Type}", _llmProvider.GetType().Name);

                // For Ollama, check if it's actually running using reflection since we don't have a direct reference
                var providerType = _llmProvider.GetType();
                if (providerType.Name == "OllamaLlmProvider")
                {
                    var healthCheckMethod = providerType.GetMethod("IsServiceAvailableAsync");
                    if (healthCheckMethod != null)
                    {
                        try
                        {
                            var task = healthCheckMethod.Invoke(_llmProvider, new object[] { ct });
                            if (task is Task<bool> healthTask)
                            {
                                var isHealthy = await healthTask.ConfigureAwait(false);
                                if (!isHealthy)
                                {
                                    errors.Add("Ollama LLM configured but not responding. Please start Ollama.");
                                }
                                else
                                {
                                    _logger.LogInformation("✓ Ollama LLM service is healthy");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to check Ollama health");
                            errors.Add($"Ollama LLM health check failed: {ex.Message}");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add($"LLM Provider error: {ex.Message}");
        }

        // 2. Check TTS Provider
        try
        {
            if (_ttsProvider == null)
            {
                errors.Add("TTS Provider not available - cannot generate narration");
            }
            else
            {
                var voices = await _ttsProvider.GetAvailableVoicesAsync().ConfigureAwait(false);
                if (voices.Count == 0)
                {
                    errors.Add("TTS Provider has no available voices. Check TTS configuration.");
                }
                else
                {
                    _logger.LogDebug("✓ TTS Provider available: {Type} with {Count} voices",
                        _ttsProvider.GetType().Name, voices.Count);
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add($"TTS Provider error: {ex.Message}");
        }

        // 3. Check FFmpeg Availability
        try
        {
            if (_videoComposer == null)
            {
                errors.Add("Video Composer not available - cannot render final video");
            }
            else
            {
                _logger.LogDebug("✓ Video Composer available: {Type}", _videoComposer.GetType().Name);

                // Use FFmpeg resolver if available
                if (_ffmpegResolver != null)
                {
                    var ffmpegResult = await _ffmpegResolver.ResolveAsync(ct: ct).ConfigureAwait(false);

                    if (!ffmpegResult.Found)
                    {
                        errors.Add($"FFmpeg not found. Checked {ffmpegResult.AttemptedPaths.Count} locations. Please install FFmpeg or run setup.");
                    }
                    else if (!ffmpegResult.IsValid)
                    {
                        errors.Add($"FFmpeg found at {ffmpegResult.Path} but is not valid. {ffmpegResult.Error}");
                    }
                    else
                    {
                        _logger.LogInformation("✓ FFmpeg found at: {Path} (version: {Version})",
                            ffmpegResult.Path, ffmpegResult.Version);
                    }
                }
                else
                {
                    _logger.LogWarning("FFmpeg resolver not available - skipping FFmpeg validation");
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Video Composer error: {ex.Message}");
        }

        // 4. Check Image Provider (optional - can use placeholders)
        if (_imageProvider == null)
        {
            _logger.LogWarning("Image Provider not configured - will use fallback placeholders");
        }
        else
        {
            _logger.LogDebug("✓ Image Provider available: {Type}", _imageProvider.GetType().Name);
        }

        // 5. Check output directory is writable
        try
        {
            var outputDir = Path.Combine(Path.GetTempPath(), "AuraVideoStudio", "Output");
            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // Test write permissions
            var testFile = Path.Combine(outputDir, $"write_test_{Guid.NewGuid()}.tmp");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);

            _logger.LogDebug("✓ Output directory writable: {Path}", outputDir);
        }
        catch (Exception ex)
        {
            errors.Add($"Output directory not writable: {ex.Message}");
        }

        var isValid = errors.Count == 0;

        if (!isValid)
        {
            _logger.LogError("Pipeline validation FAILED with {Count} errors:", errors.Count);
            foreach (var error in errors)
            {
                _logger.LogError("  ❌ {Error}", error);
            }
        }
        else
        {
            _logger.LogInformation("✅ Pipeline validation PASSED - all services ready");
        }

        return (isValid, errors);
    }

    /// <summary>
    /// Generates a complete video from the provided brief and specifications using smart orchestration
    /// and returns the detailed generation result (including timelines and narration metadata).
    /// </summary>
    public async Task<VideoGenerationResult> GenerateVideoResultAsync(
        Brief brief,
        PlanSpec planSpec,
        VoiceSpec voiceSpec,
        RenderSpec renderSpec,
        SystemProfile systemProfile,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        string? jobId = null,
        string? correlationId = null,
        bool isQuickDemo = false,
        IProgress<GenerationProgress>? detailedProgress = null,
        IImageProvider? imageProviderOverride = null)
    {
        ArgumentNullException.ThrowIfNull(brief);
        ArgumentNullException.ThrowIfNull(planSpec);
        ArgumentNullException.ThrowIfNull(voiceSpec);
        ArgumentNullException.ThrowIfNull(renderSpec);
        ArgumentNullException.ThrowIfNull(systemProfile);

        // Ensure RenderSpec carries a job identifier so FFmpeg log naming aligns with JobRunner.
        var resolvedJobId = jobId ?? renderSpec.JobId;
        var resolvedCorrelationId = correlationId ?? resolvedJobId ?? Guid.NewGuid().ToString();
        var renderSpecWithJob = renderSpec.JobId == null && resolvedJobId != null
            ? renderSpec with { JobId = resolvedJobId }
            : renderSpec;
        correlationId = resolvedCorrelationId;

        // Use the resolved render spec for downstream operations
        renderSpec = renderSpecWithJob;

        var startTime = DateTime.UtcNow;
        var providerFailures = new List<ProviderException>();
        var activeImageProvider = imageProviderOverride ?? _imageProvider;

        try
        {
            // Pre-generation validation
            var validationMsg = "Validating system readiness...";
            _logger.LogInformation("[Orchestrator] {Message}", validationMsg);
            progress?.Report(validationMsg);
            detailedProgress?.Report(ProgressBuilder.CreateBriefProgress(0, validationMsg, correlationId));

            var validationResult = await _preGenerationValidator.ValidateSystemReadyAsync(brief, planSpec, progress: null, ct).ConfigureAwait(false);
            if (!validationResult.IsValid)
            {
                var issues = string.Join("\n", validationResult.Issues);
                _logger.LogError("Pre-generation validation failed: {Issues}", issues);
                throw new ValidationException("System validation failed", validationResult.Issues);
            }
            _logger.LogInformation("Pre-generation validation passed");

            var validationPassedMsg = "System validation passed";
            _logger.LogInformation("[Orchestrator] {Message}", validationPassedMsg);
            detailedProgress?.Report(ProgressBuilder.CreateBriefProgress(50, validationPassedMsg, correlationId));

            var pipelineMsg = "Starting smart video generation pipeline...";
            _logger.LogInformation("[Orchestrator] {Message}", pipelineMsg);
            progress?.Report(pipelineMsg);
            detailedProgress?.Report(ProgressBuilder.CreateBriefProgress(100, pipelineMsg, correlationId));

            _logger.LogInformation("Using smart orchestration for topic: {Topic}, IsQuickDemo: {IsQuickDemo}", brief.Topic, isQuickDemo);

            // Create task executor that maps generation tasks to providers
            _logger.LogInformation("[Orchestrator] Creating task executor for generation tasks");
            var executorContext = CreateTaskExecutor(
                brief,
                planSpec,
                voiceSpec,
                renderSpec,
                ct,
                isQuickDemo,
                progress,
                detailedProgress,
                correlationId,
                activeImageProvider);
            var taskExecutor = executorContext.Executor;
            var recoveryResults = executorContext.RecoveryResults;

            // Map progress events from orchestration to both string and detailed progress
            var orchestrationProgress = new Progress<OrchestrationProgress>(p =>
            {
                _logger.LogDebug("[Orchestrator] Progress: {Stage} - {Completed}/{Total} tasks ({Percent:F1}%)",
                    p.CurrentStage, p.CompletedTasks, p.TotalTasks, p.ProgressPercentage);
                progress?.Report($"{p.CurrentStage}: {p.ProgressPercentage:F1}%");

                // Map to detailed progress with stage-specific information
                if (detailedProgress != null)
                {
                    var genProgress = MapOrchestrationProgressToDetailed(
                        p,
                        p.CompletedTasks,
                        p.TotalTasks,
                        correlationId);
                    detailedProgress.Report(genProgress);
                }
            });

            // Execute smart orchestration
            var result = await _smartOrchestrator.OrchestrateGenerationAsync(
                brief, planSpec, systemProfile, taskExecutor, orchestrationProgress, ct,
                recoveryResultsCallback: (key, value) =>
                {
                    recoveryResults[key] = value;
                    if (string.Equals(key, "audio", StringComparison.OrdinalIgnoreCase) && value is string recoveredAudioPath)
                    {
                        executorContext.NarrationPath ??= recoveredAudioPath;
                    }
                }
            ).ConfigureAwait(false);

            if (!result.Succeeded)
            {
                var elapsedTime = DateTime.UtcNow - startTime;
                var reasons = string.Join("; ", result.FailureReasons);

                _logger.LogError(
                    "Pipeline failed after {ElapsedSeconds}s: {FailedTasks}/{TotalTasks} tasks failed. Reasons: {Reasons}",
                    elapsedTime.TotalSeconds,
                    result.FailedTasks,
                    result.TotalTasks,
                    reasons);

                throw new PipelineException(
                    "Generation",
                    $"Generation failed: {result.FailedTasks}/{result.TotalTasks} tasks failed. Reasons: {reasons}",
                    completedTasks: result.TotalTasks - result.FailedTasks,
                    totalTasks: result.TotalTasks,
                    correlationId: correlationId,
                    providerFailures: providerFailures,
                    elapsedBeforeFailure: elapsedTime);
            }

            // Extract final video path from composition task result with multiple fallback strategies
            // CRITICAL FIX: Check executorContext.FinalVideoPath FIRST as it's set directly by the render operation
            string? outputPath = null;
            string extractionMethod = "unknown";

            _logger.LogInformation("Extracting output path. Available task result keys: {Keys}, ExecutorState.FinalVideoPath: {FinalVideoPath}",
                string.Join(", ", result.TaskResults.Keys),
                executorContext.FinalVideoPath ?? "(null)");

            // Strategy 1: Check TaskExecutorState.FinalVideoPath (PRIMARY - most reliable)
            // This is set directly by the VideoComposition task executor at line 1965
            if (!string.IsNullOrEmpty(executorContext.FinalVideoPath))
            {
                if (File.Exists(executorContext.FinalVideoPath))
                {
                    outputPath = executorContext.FinalVideoPath;
                    extractionMethod = "executor_state_final_video_path";
                    _logger.LogInformation("Extracted output path from executor state (Strategy 1 - Primary): {Path}", outputPath);
                }
                else
                {
                    _logger.LogWarning("Executor state FinalVideoPath does not exist: {Path}", executorContext.FinalVideoPath);
                }
            }

            // Strategy 2: Try primary "composition" task result key
            if (string.IsNullOrEmpty(outputPath))
            {
                if (result.TaskResults.TryGetValue("composition", out var compositionTask))
                {
                    if (compositionTask.Result is string compositionPath &&
                        !string.IsNullOrEmpty(compositionPath) &&
                        File.Exists(compositionPath))
                    {
                        outputPath = compositionPath;
                        extractionMethod = "composition_task_result";
                        _logger.LogInformation("Extracted output path from composition task result (Strategy 2): {Path}", outputPath);
                    }
                    else
                    {
                        var pathValue = compositionTask.Result as string;
                        if (!string.IsNullOrEmpty(pathValue) && !File.Exists(pathValue))
                        {
                            _logger.LogWarning("Composition task returned path that does not exist: {Path}", pathValue);
                        }
                        else
                        {
                            _logger.LogWarning("Composition task exists but result is not a valid string. Result type: {Type}, Value: {Value}",
                                compositionTask.Result?.GetType()?.Name ?? "null",
                                compositionTask.Result?.ToString() ?? "null");
                        }
                    }
                }
            }

            // Strategy 3: Try alternate task result keys
            if (string.IsNullOrEmpty(outputPath))
            {
                string[] alternateKeys = { "render", "video_output", "final_video", "output" };
                foreach (var key in alternateKeys)
                {
                    if (result.TaskResults.TryGetValue(key, out var taskResult) &&
                        taskResult.Result is string altPath &&
                        !string.IsNullOrEmpty(altPath) &&
                        File.Exists(altPath))
                    {
                        outputPath = altPath;
                        extractionMethod = $"{key}_task_result";
                        _logger.LogInformation("Extracted output path from alternate task result key '{Key}' (Strategy 3): {Path}", key, outputPath);
                        break;
                    }
                    else if (taskResult?.Result is string nonExistentPath && !string.IsNullOrEmpty(nonExistentPath) && !File.Exists(nonExistentPath))
                    {
                        _logger.LogWarning("Task result key '{Key}' returned path that does not exist: {Path}", key, nonExistentPath);
                    }
                }
            }

            // Strategy 4: Scan output directory for recently created .mp4 files
            if (string.IsNullOrEmpty(outputPath))
            {
                try
                {
                    var outputDir = Path.Combine(Path.GetTempPath(), OutputDirectoryName, OutputSubdirectoryName);
                    if (Directory.Exists(outputDir))
                    {
                        var recentFiles = Directory.GetFiles(outputDir, "*.mp4")
                            .Select(f => new FileInfo(f))
                            .Where(fi => fi.Exists && (DateTime.UtcNow - fi.CreationTimeUtc).TotalMinutes < RecentFileThresholdMinutes)
                            .OrderByDescending(fi => fi.CreationTimeUtc)
                            .ToList();

                        if (recentFiles.Count > 0)
                        {
                            outputPath = recentFiles[0].FullName;
                            extractionMethod = "directory_scan_fallback";
                            _logger.LogWarning(
                                "Output path extracted via directory scan fallback. Found {Count} recent .mp4 files, using: {Path}",
                                recentFiles.Count, outputPath);
                        }
                        else
                        {
                            _logger.LogWarning("Directory scan found no recent .mp4 files in {OutputDir}", outputDir);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Output directory does not exist: {OutputDir}", outputDir);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to scan output directory for video files");
                }
            }

            // If still no path found, provide detailed error with available task keys
            if (string.IsNullOrEmpty(outputPath))
            {
                var availableKeys = string.Join(", ", result.TaskResults.Keys);

                // CRITICAL FIX: Enhanced error message with directory diagnostic information
                var outputDir = Path.Combine(Path.GetTempPath(), OutputDirectoryName, OutputSubdirectoryName);
                var dirExists = Directory.Exists(outputDir);
                var fileCount = dirExists ? Directory.GetFiles(outputDir, "*.mp4").Length : 0;

                var errorMessage = string.Format(
                    "Video generation completed but output path not returned. " +
                    "Checked all extraction strategies but found no valid path. " +
                    "Available task result keys: [{0}]. " +
                    "Job ID: {1}. " +
                    "Output directory: {2} (exists: {3}, mp4 files: {4}). " +
                    "Check backend logs for the actual file location.",
                    availableKeys,
                    jobId ?? "unknown",
                    outputDir,
                    dirExists,
                    fileCount);

                _logger.LogError(
                    "Output path extraction failed. TaskResults keys: {Keys}, ExecutorState.FinalVideoPath: {FinalVideoPath}, OutputDir: {OutputDir}, DirExists: {DirExists}, Mp4Count: {Mp4Count}",
                    availableKeys, executorContext.FinalVideoPath ?? "(null)", outputDir, dirExists, fileCount);

                throw new InvalidOperationException(errorMessage);
            }

            // Validate output file exists before returning (critical for job completion)
            if (!File.Exists(outputPath))
            {
                throw new InvalidOperationException(
                    $"Video render completed but output file not found. Expected path: {outputPath}. Extraction method: {extractionMethod}");
            }

            _logger.LogInformation(
                "Smart orchestration completed. Video at: {Path} (verified exists). Extraction method: {Method}",
                outputPath, extractionMethod);

            var providerTimeline = executorContext.Timeline;
            if (providerTimeline == null)
            {
                _logger.LogWarning("Video orchestration completed without a captured timeline for job {JobId}", jobId ?? "unknown");
            }

            var editableTimeline = providerTimeline != null ? ConvertToEditableTimeline(providerTimeline) : null;
            return new VideoGenerationResult(
                outputPath,
                providerTimeline,
                editableTimeline,
                executorContext.NarrationPath,
                providerTimeline?.SubtitlesPath,
                correlationId);
        }
        catch (ValidationException)
        {
            // Re-throw validation exceptions without wrapping
            throw;
        }
        catch (ProviderException providerEx)
        {
            // Track provider failures and re-throw as pipeline exception
            providerFailures.Add(providerEx);
            var elapsedTime = DateTime.UtcNow - startTime;

            _logger.LogError(
                providerEx,
                "Provider {ProviderName} ({ProviderType}) failed with error code {ErrorCode}",
                providerEx.ProviderName,
                providerEx.Type,
                providerEx.SpecificErrorCode);

            throw new PipelineException(
                "Provider",
                $"Provider {providerEx.ProviderName} failed: {providerEx.Message}",
                correlationId: correlationId,
                providerFailures: providerFailures,
                elapsedBeforeFailure: elapsedTime,
                innerException: providerEx);
        }
        catch (PipelineException)
        {
            // Re-throw pipeline exceptions without wrapping
            throw;
        }
        catch (Exception ex)
        {
            var elapsedTime = DateTime.UtcNow - startTime;
            _logger.LogError(ex, "Error during smart video generation after {ElapsedSeconds}s", elapsedTime.TotalSeconds);

            throw new PipelineException(
                "Unknown",
                $"Unexpected error during video generation: {ex.Message}",
                correlationId: correlationId,
                providerFailures: providerFailures,
                elapsedBeforeFailure: elapsedTime,
                innerException: ex);
        }
        finally
        {
            // Clean up temporary resources on completion or failure
            _cleanupManager.CleanupAll();
        }
    }

    /// <summary>
    /// Generates a complete video using smart orchestration and returns only the output path
    /// (legacy compatibility wrapper).
    /// </summary>
    public async Task<string> GenerateVideoAsync(
        Brief brief,
        PlanSpec planSpec,
        VoiceSpec voiceSpec,
        RenderSpec renderSpec,
        SystemProfile systemProfile,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        string? jobId = null,
        string? correlationId = null,
        bool isQuickDemo = false,
        IProgress<GenerationProgress>? detailedProgress = null,
        IImageProvider? imageProviderOverride = null)
    {
        var result = await GenerateVideoResultAsync(
            brief,
            planSpec,
            voiceSpec,
            renderSpec,
            systemProfile,
            progress,
            ct,
            jobId,
            correlationId,
            isQuickDemo,
            detailedProgress,
            imageProviderOverride).ConfigureAwait(false);

        return result.OutputPath;
    }

    /// <summary>
    /// Generates a complete video from the provided brief and specifications (legacy pipeline)
    /// and returns the detailed generation result.
    /// </summary>
    public async Task<VideoGenerationResult> GenerateVideoResultAsync(
        Brief brief,
        PlanSpec planSpec,
        VoiceSpec voiceSpec,
        RenderSpec renderSpec,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        string? jobId = null,
        string? correlationId = null,
        bool isQuickDemo = false,
        IProgress<GenerationProgress>? detailedProgress = null,
        IImageProvider? imageProviderOverride = null)
    {
        ArgumentNullException.ThrowIfNull(brief);
        ArgumentNullException.ThrowIfNull(planSpec);
        ArgumentNullException.ThrowIfNull(voiceSpec);
        ArgumentNullException.ThrowIfNull(renderSpec);

        try
        {
            // Pre-generation validation
            progress?.Report("Validating system readiness...");
            var validationResult = await _preGenerationValidator.ValidateSystemReadyAsync(brief, planSpec, progress: null, ct).ConfigureAwait(false);
            if (!validationResult.IsValid)
            {
                var issues = string.Join("\n", validationResult.Issues);
                _logger.LogError("Pre-generation validation failed: {Issues}", issues);
                throw new ValidationException("System validation failed", validationResult.Issues);
            }
            _logger.LogInformation("Pre-generation validation passed");

            // If pipeline orchestration engine is available, use intelligent orchestration
            if (_pipelineEngine != null)
            {
                _logger.LogInformation("Using intelligent pipeline orchestration for generation");
                return await GenerateVideoWithPipelineAsync(brief, planSpec, voiceSpec, renderSpec, progress, ct).ConfigureAwait(false);
            }

            progress?.Report("Starting video generation pipeline...");

            // Stage 0: Brief processing (capture initial brief validation)
            if (jobId != null && correlationId != null)
            {
                var briefBuilder = Telemetry.TelemetryBuilder.Start(jobId, correlationId, Telemetry.RunStage.Brief);
                var briefTelemetry = briefBuilder
                    .WithStatus(Telemetry.ResultStatus.Ok, message: $"Brief processed: {brief.Topic}")
                    .AddMetadata("topic", brief.Topic ?? "Untitled")
                    .AddMetadata("audience", brief.Audience ?? "General")
                    .AddMetadata("goal", brief.Goal ?? "Not specified")
                    .Build();
                _telemetryCollector.Record(briefTelemetry);
            }

            // Stage 1: Script generation with fallback for Quick Demo
            progress?.Report("Stage 1/5: Generating script...");
            _logger.LogInformation("Generating script for topic: {Topic}, IsQuickDemo: {IsQuickDemo}", brief.Topic, isQuickDemo);

            var scriptBuilder = jobId != null && correlationId != null
                ? Telemetry.TelemetryBuilder.Start(jobId, correlationId, Telemetry.RunStage.Script)
                : null;

            Brief enhancedBrief = brief;
            Aura.Core.Models.RAG.RagContext? ragContext = null;

            if (_ragScriptEnhancer != null && brief.RagConfiguration?.Enabled == true)
            {
                _logger.LogInformation("RAG is enabled, enhancing brief with retrieved context");
                progress?.Report("Retrieving relevant documents from RAG index...");

                var (enhanced, context) = await _ragScriptEnhancer.EnhanceBriefWithRagAsync(brief, ct).ConfigureAwait(false);
                enhancedBrief = enhanced;
                ragContext = context;

                if (ragContext != null && ragContext.Chunks.Count > 0)
                {
                    _logger.LogInformation("RAG context retrieved: {ChunkCount} chunks, {TokenCount} tokens",
                        ragContext.Chunks.Count, ragContext.TotalTokens);
                }
            }

            string script;
            bool usedFallback = false;

            try
            {
                script = await _retryWrapper.ExecuteWithRetryAsync(
                    async (ctRetry) =>
                    {
                        var generatedScript = await _llmProvider.DraftScriptAsync(enhancedBrief, planSpec, ctRetry).ConfigureAwait(false);

                        // Validate script structure and content
                        var structuralValidation = _scriptValidator.Validate(generatedScript, planSpec);
                        var contentValidation = _llmValidator.ValidateScriptContent(generatedScript, planSpec);

                        if (!structuralValidation.IsValid || !contentValidation.IsValid)
                        {
                            var allIssues = structuralValidation.Issues.Concat(contentValidation.Issues).ToList();
                            _logger.LogWarning("Script validation failed: {Issues}", string.Join(", ", allIssues));
                            throw new ValidationException("Script quality validation failed", allIssues);
                        }

                        return generatedScript;
                    },
                    "Script Generation",
                    ct,
                    maxRetries: 2
                ).ConfigureAwait(false);
            }
            catch (ValidationException vex) when (isQuickDemo)
            {
                // For Quick Demo, use safe fallback script instead of failing
                _logger.LogWarning("Script validation failed for Quick Demo: {Message}. Using safe fallback script.", vex.Message);
                progress?.Report("Using safe fallback script...");

                script = GenerateSafeFallbackScript(brief, planSpec);
                usedFallback = true;

                _logger.LogInformation("Safe fallback script generated: {Length} characters", script.Length);
            }

            if (_ragScriptEnhancer != null && brief.RagConfiguration?.TightenClaims == true && ragContext != null && !usedFallback)
            {
                _logger.LogInformation("Performing 'tighten claims' validation pass");
                var (enhancedScript, warnings) = await _ragScriptEnhancer.TightenClaimsAsync(script, ragContext, ct).ConfigureAwait(false);

                foreach (var warning in warnings)
                {
                    _logger.LogWarning("RAG claim validation: {Warning}", warning);
                }

                script = enhancedScript;
            }

            _logger.LogInformation("Script generated and validated: {Length} characters, UsedFallback: {UsedFallback}",
                script.Length, usedFallback);

            // Record script generation telemetry
            if (scriptBuilder != null)
            {
                var scriptTelemetry = scriptBuilder
                    .WithModel(_llmProvider.GetType().Name, _llmProvider.GetType().Name)
                    .WithStatus(Telemetry.ResultStatus.Ok, message: $"Script generated: {script.Length} characters")
                    .AddMetadata("script_length", script.Length)
                    .AddMetadata("rag_enabled", brief.RagConfiguration?.Enabled == true)
                    .AddMetadata("used_fallback", usedFallback)
                    .Build();
                _telemetryCollector.Record(scriptTelemetry);
            }

            // Stage 2: Parse script into scenes
            progress?.Report("Stage 2/5: Parsing scenes...");
            var scenes = ParseScriptIntoScenes(script, planSpec.TargetDuration);
            _logger.LogInformation("Parsed {SceneCount} scenes", scenes.Count);

            // Record plan/scene parsing telemetry
            if (jobId != null && correlationId != null)
            {
                var planBuilder = Telemetry.TelemetryBuilder.Start(jobId, correlationId, Telemetry.RunStage.Plan);
                var planTelemetry = planBuilder
                    .WithStatus(Telemetry.ResultStatus.Ok, message: $"Script parsed into {scenes.Count} scenes")
                    .AddMetadata("scene_count", scenes.Count)
                    .AddMetadata("target_duration_seconds", planSpec.TargetDuration.TotalSeconds)
                    .Build();
                _telemetryCollector.Record(planTelemetry);
            }

            // Optional: Apply pacing optimization if enabled
            if (_providerSettings.GetEnablePacingOptimization() &&
                _pacingOptimizer != null &&
                _pacingApplicationService != null &&
                _providerSettings.GetAutoApplyPacingSuggestions())
            {
                try
                {
                    progress?.Report("Analyzing and optimizing pacing...");
                    _logger.LogInformation("Pacing optimization enabled, analyzing scenes");

                    var startTime = DateTime.UtcNow;
                    var pacingResult = await _pacingOptimizer.OptimizePacingAsync(
                        scenes, brief, _llmProvider, true, PacingProfile.BalancedDocumentary, ct).ConfigureAwait(false);

                    var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
                    _logger.LogInformation(
                        "Pacing analysis completed in {Elapsed:F2}s. Confidence: {Confidence:F1}%, Retention: {Retention:F1}%",
                        elapsed, pacingResult.ConfidenceScore, pacingResult.PredictedRetentionRate);

                    // Validate suggestions
                    var optimizationLevel = Enum.Parse<Aura.Core.Models.Settings.OptimizationLevel>(
                        _providerSettings.GetPacingOptimizationLevel(), ignoreCase: true);
                    var minConfidence = _providerSettings.GetMinimumConfidenceThreshold();

                    var validation = _pacingApplicationService.ValidateSuggestions(
                        pacingResult, scenes, planSpec.TargetDuration, minConfidence);

                    if (validation.IsValid)
                    {
                        // Apply pacing suggestions
                        scenes = _timelineBuilder.ApplyPacingSuggestions(
                            scenes, pacingResult, optimizationLevel, minConfidence).ToList();

                        _logger.LogInformation(
                            "Applied pacing suggestions. New total duration: {Duration}",
                            TimeSpan.FromSeconds(scenes.Sum(s => s.Duration.TotalSeconds)));
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Pacing suggestions validation failed: {Issues}",
                            string.Join("; ", validation.Issues));
                    }

                    // Log warnings if any
                    foreach (var warning in validation.Warnings)
                    {
                        _logger.LogWarning("Pacing warning: {Warning}", warning);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Pacing optimization failed, continuing with original timings");
                    // Continue with original scenes if pacing fails
                }
            }
            else
            {
                _logger.LogDebug("Pacing optimization disabled or not available");
            }

            // Stage 3: Generate narration
            progress?.Report("Stage 3/5: Generating narration...");
            var ttsBuilder = jobId != null && correlationId != null
                ? Telemetry.TelemetryBuilder.Start(jobId, correlationId, Telemetry.RunStage.Tts)
                : null;

            var scriptLines = ConvertScenesToScriptLines(scenes);

            // Optimize narration for TTS if service is available
            if (_narrationOptimizationService != null)
            {
                try
                {
                    _logger.LogInformation("Optimizing narration for TTS synthesis");
                    var optimizationConfig = new NarrationOptimizationConfig();
                    var optimizationResult = await _narrationOptimizationService.OptimizeForTtsAsync(
                        scriptLines,
                        voiceSpec,
                        null,
                        optimizationConfig,
                        ct
                    ).ConfigureAwait(false);

                    _logger.LogInformation(
                        "Narration optimized. Score: {Score:F1}, Optimizations: {Count}",
                        optimizationResult.OptimizationScore,
                        optimizationResult.OptimizationsApplied
                    );

                    // Use optimized script lines for TTS
                    scriptLines = optimizationResult.OptimizedLines
                        .Select(ol => new ScriptLine(
                            ol.SceneIndex,
                            ol.OptimizedText,
                            ol.Start,
                            ol.Duration
                        ))
                        .ToList();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Narration optimization failed, using original text");
                    // Continue with original script lines if optimization fails
                }
            }
            else
            {
                _logger.LogDebug("Narration optimization service not available");
            }

            string narrationPath = await _retryWrapper.ExecuteWithRetryAsync(
                async (ctRetry) =>
                {
                    var audioPath = await _ttsProvider.SynthesizeAsync(scriptLines, voiceSpec, ctRetry).ConfigureAwait(false);

                    // Validate audio output
                    var minDuration = TimeSpan.FromSeconds(Math.Max(5, planSpec.TargetDuration.TotalSeconds * 0.3));
                    var audioValidation = _ttsValidator.ValidateAudioFile(audioPath, minDuration);

                    if (!audioValidation.IsValid)
                    {
                        _logger.LogWarning("Audio validation failed: {Issues}", string.Join(", ", audioValidation.Issues));
                        throw new ValidationException("Audio quality validation failed", audioValidation.Issues);
                    }

                    _cleanupManager.RegisterTempFile(audioPath);
                    return audioPath;
                },
                "Audio Generation",
                ct
            ).ConfigureAwait(false);

            _logger.LogInformation("Narration generated and validated at: {Path}", narrationPath);

            // Record TTS telemetry
            if (ttsBuilder != null)
            {
                var totalCharacters = scriptLines.Sum(sl => sl.Text?.Length ?? 0);
                var ttsTelemetry = ttsBuilder
                    .WithModel(voiceSpec.VoiceName, _ttsProvider.GetType().Name)
                    .WithStatus(Telemetry.ResultStatus.Ok, message: "Narration generated successfully")
                    .AddMetadata("total_characters", totalCharacters)
                    .AddMetadata("scene_count", scriptLines.Count)
                    .AddMetadata("voice_name", voiceSpec.VoiceName ?? "default")
                    .Build();
                _telemetryCollector.Record(ttsTelemetry);
            }

            // Audio Intelligence: Enhance narration if service is available
            string enhancedNarrationPath = narrationPath;
            if (_voiceEnhancementService != null)
            {
                try
                {
                    _logger.LogInformation("Applying voice enhancement to narration");
                    progress?.Report("Enhancing voice audio...");

                    var enhanceOptions = _voiceEnhancementService.GetPreset(
                        Services.AudioIntelligence.VoiceEnhancementPreset.VideoNarration);
                    var enhancedPath = Path.Combine(
                        Path.GetDirectoryName(narrationPath) ?? Path.GetTempPath(),
                        $"enhanced_{Path.GetFileName(narrationPath)}");

                    var enhanceResult = await _voiceEnhancementService.EnhanceVoiceAsync(
                        narrationPath,
                        enhancedPath,
                        enhanceOptions,
                        ct
                    ).ConfigureAwait(false);

                    if (enhanceResult.Success)
                    {
                        enhancedNarrationPath = enhanceResult.OutputPath;
                        _cleanupManager.RegisterTempFile(enhancedPath);
                        _logger.LogInformation(
                            "Voice enhancement applied. Before: {BeforeLUFS:F1} LUFS, After: {AfterLUFS:F1} LUFS",
                            enhanceResult.BeforeAnalysis.IntegratedLoudness,
                            enhanceResult.AfterAnalysis?.IntegratedLoudness ?? 0);
                    }
                    else
                    {
                        _logger.LogWarning("Voice enhancement failed: {Error}, using original narration",
                            enhanceResult.ErrorMessage);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Voice enhancement failed, using original narration");
                }
            }

            // Stage 4: Build timeline (placeholder for music/assets)
            progress?.Report("Stage 4/5: Building timeline...");
            var timeline = new Providers.Timeline(
                Scenes: scenes,
                SceneAssets: new Dictionary<int, IReadOnlyList<Asset>>(),
                NarrationPath: enhancedNarrationPath,
                MusicPath: string.Empty,
                SubtitlesPath: null
            );

            // Stage 5: Render video
            progress?.Report("Stage 5/5: Rendering video...");

            // Explicit logging at stage transition (70% mark) for debugging hangs
            _logger.LogInformation(
                "[Stage Transition] Starting video render at 70%% mark. " +
                "Scenes: {SceneCount}, NarrationPath: {NarrationPath}, JobId: {JobId}",
                scenes.Count, enhancedNarrationPath, jobId ?? "N/A");

            var renderBuilder = jobId != null && correlationId != null
                ? Telemetry.TelemetryBuilder.Start(jobId, correlationId, Telemetry.RunStage.Render)
                : null;

            // Track render progress with detailed logging
            var lastProgressTime = DateTime.UtcNow;
            var lastProgressPercent = 0f;
            var renderProgress = new Progress<RenderProgress>(p =>
            {
                progress?.Report($"Rendering: {p.Percentage:F1}% - {p.CurrentStage}");

                // Log at Information level for visibility
                _logger.LogInformation(
                    "[Render Progress] {Percentage:F1}% - Stage: {Stage}, Elapsed: {Elapsed}",
                    p.Percentage, p.CurrentStage, p.Elapsed);

                var now = DateTime.UtcNow;
                var timeSinceLastProgress = now - lastProgressTime;

                // Detect potential stalls
                if (Math.Abs(p.Percentage - lastProgressPercent) < StallProgressThreshold && timeSinceLastProgress.TotalSeconds > StallTimeoutSeconds)
                {
                    _logger.LogWarning(
                        "[Render Stall Warning] No progress change for {Seconds:F0}s at {Percentage:F1}%",
                        timeSinceLastProgress.TotalSeconds, p.Percentage);
                }
                else if (Math.Abs(p.Percentage - lastProgressPercent) >= StallProgressThreshold)
                {
                    lastProgressTime = now;
                    lastProgressPercent = p.Percentage;
                }
            });

            _logger.LogInformation("[Render Start] Beginning FFmpeg render operation for job {JobId}", jobId ?? "N/A");

            string outputPath = await _videoComposer.RenderAsync(timeline, renderSpec, renderProgress, ct).ConfigureAwait(false);
            _logger.LogInformation("[Render Complete] Video rendered to: {Path}", outputPath);

            // Record render telemetry
            if (renderBuilder != null)
            {
                var renderTelemetry = renderBuilder
                    .WithModel("FFmpeg", "VideoComposer")
                    .WithStatus(Telemetry.ResultStatus.Ok, message: "Video rendered successfully")
                    .AddMetadata("output_path", outputPath)
                    .AddMetadata("resolution", $"{renderSpec.Res.Width}x{renderSpec.Res.Height}")
                    .AddMetadata("fps", renderSpec.Fps)
                    .AddMetadata("codec", renderSpec.Codec)
                    .Build();
                _telemetryCollector.Record(renderTelemetry);
            }

            // Stage 6: Post-processing completion
            if (jobId != null && correlationId != null)
            {
                var postBuilder = Telemetry.TelemetryBuilder.Start(jobId, correlationId, Telemetry.RunStage.Post);
                var postTelemetry = postBuilder
                    .WithStatus(Telemetry.ResultStatus.Ok, message: "Post-processing completed")
                    .AddMetadata("final_output", outputPath)
                    .Build();
                _telemetryCollector.Record(postTelemetry);
            }

            progress?.Report("Video generation complete!");
            return new VideoGenerationResult(outputPath, timeline, ConvertToEditableTimeline(timeline), narrationPath, timeline.SubtitlesPath, correlationId);
        }
        catch (ValidationException vex)
        {
            // Record validation failure telemetry
            if (jobId != null && correlationId != null)
            {
                var errorBuilder = Telemetry.TelemetryBuilder.Start(jobId, correlationId, Telemetry.RunStage.Post);
                var errorTelemetry = errorBuilder
                    .WithStatus(Telemetry.ResultStatus.Error, "VALIDATION_ERROR", vex.Message)
                    .Build();
                _telemetryCollector.Record(errorTelemetry);
            }
            // Re-throw validation exceptions without wrapping
            throw;
        }
        catch (Exception ex)
        {
            // Record general failure telemetry
            if (jobId != null && correlationId != null)
            {
                var errorBuilder = Telemetry.TelemetryBuilder.Start(jobId, correlationId, Telemetry.RunStage.Post);
                var errorTelemetry = errorBuilder
                    .WithStatus(Telemetry.ResultStatus.Error, "GENERATION_ERROR", ex.Message)
                    .Build();
                _telemetryCollector.Record(errorTelemetry);
            }
            _logger.LogError(ex, "Error during video generation");
            throw;
        }
        finally
        {
            // Clean up temporary resources on completion or failure
            _cleanupManager.CleanupAll();
        }
    }

    /// <summary>
    /// Generates a complete video using the intelligent pipeline orchestration engine.
    /// Uses dependency-aware service ordering, parallel execution, and smart caching.
    /// </summary>
    private async Task<VideoGenerationResult> GenerateVideoWithPipelineAsync(
        Brief brief,
        PlanSpec planSpec,
        VoiceSpec voiceSpec,
        RenderSpec renderSpec,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        if (_pipelineEngine == null)
            throw new InvalidOperationException("Pipeline engine not available");

        progress?.Report("Initializing intelligent pipeline orchestration...");
        _logger.LogInformation("Starting pipeline-based video generation for topic: {Topic}", brief.Topic);

        var systemProfile = new SystemProfile
        {
            Tier = HardwareTier.B,
            LogicalCores = Environment.ProcessorCount,
            PhysicalCores = Math.Max(1, Environment.ProcessorCount / 2),
            RamGB = (int)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024.0 * 1024 * 1024))
        };

        var pipelineContext = new PipelineExecutionContext
        {
            Brief = brief,
            PlanSpec = planSpec,
            VoiceSpec = voiceSpec,
            RenderSpec = renderSpec,
            SystemProfile = systemProfile
        };

        var pipelineConfig = new Services.Orchestration.PipelineConfiguration
        {
            MaxConcurrentLlmCalls = Math.Max(1, Environment.ProcessorCount / 2),
            EnableCaching = true,
            CacheTtl = TimeSpan.FromHours(1),
            ContinueOnOptionalFailure = true,
            EnableParallelExecution = true
        };

        var pipelineProgress = new Progress<PipelineProgress>(p =>
        {
            var stagePercent = p.PercentComplete;
            progress?.Report($"{p.CurrentStage}: {stagePercent:F1}% ({p.CompletedServices}/{p.TotalServices} services)");
        });

        var result = await _pipelineEngine.ExecutePipelineAsync(
            pipelineContext, pipelineConfig, pipelineProgress, ct
        ).ConfigureAwait(false);

        if (!result.Success)
        {
            var errors = string.Join("; ", result.Errors);
            _logger.LogError("Pipeline orchestration failed: {Errors}", errors);
            throw new InvalidOperationException($"Pipeline generation failed: {errors}");
        }

        _logger.LogInformation(
            "Pipeline orchestration completed successfully. Duration: {Duration}s, Cache hits: {CacheHits}, Parallel executions: {ParallelExecutions}",
            result.TotalExecutionTime.TotalSeconds,
            result.CacheHits,
            result.ParallelExecutions
        );

        if (result.Warnings.Count > 0)
        {
            _logger.LogWarning("Pipeline completed with warnings: {Warnings}", string.Join("; ", result.Warnings));
        }

        string script = pipelineContext.GeneratedScript
            ?? throw new InvalidOperationException("Pipeline did not generate script");

        progress?.Report("Stage 3/5: Generating narration...");
        var scenes = ParseScriptIntoScenes(script, planSpec.TargetDuration);
        var scriptLines = ConvertScenesToScriptLines(scenes);

        string narrationPath = await _retryWrapper.ExecuteWithRetryAsync(
            async (ctRetry) =>
            {
                var audioPath = await _ttsProvider.SynthesizeAsync(scriptLines, voiceSpec, ctRetry).ConfigureAwait(false);

                var minDuration = TimeSpan.FromSeconds(Math.Max(5, planSpec.TargetDuration.TotalSeconds * 0.3));
                var audioValidation = _ttsValidator.ValidateAudioFile(audioPath, minDuration);

                if (!audioValidation.IsValid)
                {
                    _logger.LogWarning("Audio validation failed: {Issues}", string.Join(", ", audioValidation.Issues));
                    throw new ValidationException("Audio quality validation failed", audioValidation.Issues);
                }

                _cleanupManager.RegisterTempFile(audioPath);
                return audioPath;
            },
            "Audio Generation",
            ct
        ).ConfigureAwait(false);

        _logger.LogInformation("Narration generated at: {Path}", narrationPath);

        progress?.Report("Stage 4/5: Building timeline...");
        var timeline = new Providers.Timeline(
            Scenes: scenes,
            SceneAssets: new Dictionary<int, IReadOnlyList<Asset>>(),
            NarrationPath: narrationPath,
            MusicPath: string.Empty,
            SubtitlesPath: null
        );

        progress?.Report("Stage 5/5: Rendering video...");

        // Explicit logging at stage transition (70% mark) for debugging hangs
        _logger.LogInformation(
            "[Stage Transition] Starting video render at 70%% mark. " +
            "Scenes: {SceneCount}, NarrationPath: {NarrationPath}",
            scenes.Count, narrationPath);

        // Track render progress with detailed logging
        var lastProgressTime = DateTime.UtcNow;
        var lastProgressPercent = 0f;
        var renderProgress = new Progress<RenderProgress>(p =>
        {
            progress?.Report($"Rendering: {p.Percentage:F1}% - {p.CurrentStage}");

            _logger.LogInformation(
                "[Render Progress] {Percentage:F1}% - Stage: {Stage}",
                p.Percentage, p.CurrentStage);

            var now = DateTime.UtcNow;
            var timeSinceLastProgress = now - lastProgressTime;

            // Detect potential stalls
            if (Math.Abs(p.Percentage - lastProgressPercent) < StallProgressThreshold && timeSinceLastProgress.TotalSeconds > StallTimeoutSeconds)
            {
                _logger.LogWarning(
                    "[Render Stall Warning] No progress change for {Seconds:F0}s at {Percentage:F1}%",
                    timeSinceLastProgress.TotalSeconds, p.Percentage);
            }
            else if (Math.Abs(p.Percentage - lastProgressPercent) >= StallProgressThreshold)
            {
                lastProgressTime = now;
                lastProgressPercent = p.Percentage;
            }
        });

        _logger.LogInformation("[Render Start] Beginning FFmpeg render operation");

        string outputPath = await _videoComposer.RenderAsync(timeline, renderSpec, renderProgress, ct).ConfigureAwait(false);
        _logger.LogInformation("[Render Complete] Video rendered to: {Path}", outputPath);

        progress?.Report("Video generation complete!");
        return new VideoGenerationResult(outputPath, timeline, ConvertToEditableTimeline(timeline), narrationPath, timeline.SubtitlesPath, null);
    }

    /// <summary>
    /// Generates a video using the legacy pipeline and returns only the output path
    /// (legacy compatibility wrapper).
    /// </summary>
    public async Task<string> GenerateVideoAsync(
        Brief brief,
        PlanSpec planSpec,
        VoiceSpec voiceSpec,
        RenderSpec renderSpec,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        string? jobId = null,
        string? correlationId = null,
        bool isQuickDemo = false,
        IProgress<GenerationProgress>? detailedProgress = null,
        IImageProvider? imageProviderOverride = null)
    {
        var result = await GenerateVideoResultAsync(
            brief,
            planSpec,
            voiceSpec,
            renderSpec,
            progress,
            ct,
            jobId,
            correlationId,
            isQuickDemo,
            detailedProgress,
            imageProviderOverride).ConfigureAwait(false);

        return result.OutputPath;
    }

    /// <summary>
    /// Parses the generated script text into Scene objects with timings.
    /// </summary>
    private List<Scene> ParseScriptIntoScenes(string script, TimeSpan targetDuration)
    {
        var scenes = new List<Scene>();
        var lines = script.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        string? currentHeading = null;
        var currentScriptLines = new List<string>();
        int sceneIndex = 0;

        foreach (var line in lines)
        {
            if (line.StartsWith("## "))
            {
                // Found a new scene heading
                if (currentHeading != null && currentScriptLines.Count > 0)
                {
                    // Save the previous scene with cleaned narration
                    var sceneScript = string.Join("\n", currentScriptLines);
                    var cleanedScript = LlmScriptCleanup.CleanNarration(sceneScript);
                    scenes.Add(new Scene(sceneIndex++, currentHeading, cleanedScript, TimeSpan.Zero, TimeSpan.Zero));
                    currentScriptLines.Clear();
                }
                currentHeading = line.Substring(3).Trim();
            }
            else if (!line.StartsWith("#") && !string.IsNullOrWhiteSpace(line))
            {
                // Skip metadata lines - only add actual narrative content
                var trimmedLine = line.Trim();
                if (!LlmScriptCleanup.IsMetadataLine(trimmedLine) &&
                    !LlmScriptCleanup.IsLlmMetaCommentary(trimmedLine))
                {
                    currentScriptLines.Add(line);
                }
            }
        }

        // Add the last scene with cleaned narration
        if (currentHeading != null && currentScriptLines.Count > 0)
        {
            var sceneScript = string.Join("\n", currentScriptLines);
            var cleanedScript = LlmScriptCleanup.CleanNarration(sceneScript);
            scenes.Add(new Scene(sceneIndex++, currentHeading, cleanedScript, TimeSpan.Zero, TimeSpan.Zero));
        }

        // Calculate timings based on word count distribution
        int totalWords = scenes.Sum(s => CountWords(s.Script));
        TimeSpan currentStart = TimeSpan.Zero;

        for (int i = 0; i < scenes.Count; i++)
        {
            int sceneWords = CountWords(scenes[i].Script);
            double proportion = totalWords > 0 ? (double)sceneWords / totalWords : 1.0 / scenes.Count;
            TimeSpan duration = TimeSpan.FromSeconds(targetDuration.TotalSeconds * proportion);

            scenes[i] = scenes[i] with
            {
                Start = currentStart,
                Duration = duration
            };

            currentStart += duration;
        }

        return scenes;
    }

    /// <summary>
    /// Converts scenes into script lines for TTS synthesis.
    /// Applies final cleanup to ensure no metadata leaks through to TTS.
    /// </summary>
    private List<ScriptLine> ConvertScenesToScriptLines(List<Scene> scenes)
    {
        var scriptLines = new List<ScriptLine>();

        foreach (var scene in scenes)
        {
            // Apply final cleanup to ensure no metadata remains
            var cleanedScript = LlmScriptCleanup.CleanNarration(scene.Script);

            scriptLines.Add(new ScriptLine(
                SceneIndex: scene.Index,
                Text: cleanedScript,
                Start: scene.Start,
                Duration: scene.Duration
            ));
        }

        return scriptLines;
    }

    private static EditableTimeline ConvertToEditableTimeline(Providers.Timeline timeline)
    {
        var editable = new EditableTimeline
        {
            BackgroundMusicPath = string.IsNullOrWhiteSpace(timeline.MusicPath) ? null : timeline.MusicPath,
            Subtitles = string.IsNullOrWhiteSpace(timeline.SubtitlesPath)
                ? new SubtitleTrack()
                : new SubtitleTrack(
                    Enabled: true,
                    FilePath: timeline.SubtitlesPath)
        };

        foreach (var scene in timeline.Scenes)
        {
            var assets = timeline.SceneAssets.TryGetValue(scene.Index, out var sceneAssets) && sceneAssets != null
                ? sceneAssets.Select((asset, assetIndex) => ConvertAsset(asset, scene, assetIndex)).ToList()
                : new List<TimelineAsset>();

            editable.Scenes.Add(new TimelineScene(
                Index: scene.Index,
                Heading: string.IsNullOrWhiteSpace(scene.Heading) ? $"Scene {scene.Index + 1}" : scene.Heading,
                Script: scene.Script,
                Start: scene.Start,
                Duration: scene.Duration,
                NarrationAudioPath: timeline.NarrationPath,
                VisualAssets: assets));
        }

        return editable;
    }

    private static TimelineAsset ConvertAsset(Asset asset, Scene scene, int order)
    {
        var type = asset.Kind?.ToLowerInvariant() switch
        {
            "video" => AssetType.Video,
            "audio" => AssetType.Audio,
            _ => AssetType.Image
        };

        return new TimelineAsset(
            Id: $"{scene.Index}_{order}_{type}",
            Type: type,
            FilePath: asset.PathOrUrl,
            Start: scene.Start,
            Duration: scene.Duration,
            Position: new Position(0, 0, 100, 100),
            ZIndex: order);
    }

    /// <summary>
    /// Attempts to select assets from the library using semantic matching via AssetTaggingService.
    /// Falls back to the image provider if no matching assets are found or the service is unavailable.
    /// </summary>
    /// <param name="scene">The scene to match assets for</param>
    /// <param name="visualSpec">Visual specification for the scene</param>
    /// <param name="preferredType">The preferred asset type to match</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of matching assets, or empty list if none found</returns>
    private async Task<IReadOnlyList<Asset>> SelectAssetsFromLibraryAsync(
        Scene scene,
        VisualSpec visualSpec,
        LibraryAssetType preferredType,
        CancellationToken ct)
    {
        if (_assetTaggingService == null)
        {
            _logger.LogDebug("AssetTaggingService not available, skipping library search");
            return Array.Empty<Asset>();
        }

        try
        {
            // Combine scene heading and script for semantic matching
            var sceneDescription = $"{scene.Heading}. {scene.Script}";

            // Use the tagging service to find matching assets
            var matches = await _assetTaggingService.MatchAssetsToSceneAsync(
                scene.Heading,
                scene.Script,
                preferredType,
                maxResults: 3,
                ct
            ).ConfigureAwait(false);

            if (matches.Count == 0)
            {
                _logger.LogDebug("No matching assets found in library for scene {SceneIndex}", scene.Index);
                return Array.Empty<Asset>();
            }

            _logger.LogInformation(
                "Found {Count} matching assets in library for scene {SceneIndex} with scores: {Scores}",
                matches.Count,
                scene.Index,
                string.Join(", ", matches.Select(m => $"{m.Score:F2}")));

            // The AssetTaggingService returns library asset IDs, but we need file paths
            // In a full implementation, this would query the asset repository for paths
            // For now, we log the matches and return empty (let the image provider handle generation)
            _logger.LogDebug("Asset library matching available but path resolution not implemented yet");
            return Array.Empty<Asset>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error selecting assets from library for scene {SceneIndex}", scene.Index);
            return Array.Empty<Asset>();
        }
    }

    /// <summary>
    /// Counts the number of words in a text string.
    /// </summary>
    private int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        return text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    /// <summary>
    /// Creates a task executor function that maps generation tasks to the appropriate providers.
    /// </summary>
    private TaskExecutorState CreateTaskExecutor(
        Brief brief,
        PlanSpec planSpec,
        VoiceSpec voiceSpec,
        RenderSpec renderSpec,
        CancellationToken outerCt,
        bool isQuickDemo = false,
        IProgress<string>? progress = null,
        IProgress<GenerationProgress>? detailedProgress = null,
        string? correlationId = null,
        IImageProvider? imageProvider = null)
    {
        var state = new TaskExecutorState();
        // Shared state for task results
        var recoveryResults = state.RecoveryResults;
        string? generatedScript = null;
        List<Scene>? parsedScenes = null;
        string? narrationPath = null;
        Dictionary<int, IReadOnlyList<Asset>> sceneAssets = new();
        var resolvedImageProvider = imageProvider ?? _imageProvider;

        state.Executor = async (node, ct) =>
        {
            _logger.LogDebug("Executing task: {TaskId} ({TaskType})", node.TaskId, node.TaskType);

            switch (node.TaskType)
            {
                case GenerationTaskType.ScriptGeneration:
                    // Generate script using LLM with retry logic and fallback for Quick Demo
                    // bool usedFallback = false; // Currently unused - reserved for fallback tracking

                    try
                    {
                        generatedScript = await _retryWrapper.ExecuteWithRetryAsync(
                            async (ctRetry) =>
                            {
                                var script = await _llmProvider.DraftScriptAsync(brief, planSpec, ctRetry).ConfigureAwait(false);

                                // Validate script structure and content
                                var structuralValidation = _scriptValidator.Validate(script, planSpec);
                                var contentValidation = _llmValidator.ValidateScriptContent(script, planSpec);

                                if (!structuralValidation.IsValid || !contentValidation.IsValid)
                                {
                                    var allIssues = structuralValidation.Issues.Concat(contentValidation.Issues).ToList();
                                    _logger.LogWarning("Script validation failed: {Issues}", string.Join(", ", allIssues));
                                    throw new ValidationException("Script quality validation failed", allIssues);
                                }

                                return script;
                            },
                            "Script Generation",
                            ct,
                            maxRetries: 2
                        ).ConfigureAwait(false);
                    }
                    catch (ValidationException vex) when (isQuickDemo)
                    {
                        // For Quick Demo, use safe fallback script instead of failing
                        _logger.LogWarning("Script validation failed for Quick Demo: {Message}. Using safe fallback script.", vex.Message);

                        generatedScript = GenerateSafeFallbackScript(brief, planSpec);
                        // usedFallback = true; // Fallback tracking currently unused

                        _logger.LogInformation("Safe fallback script generated: {Length} characters", generatedScript.Length);
                    }

                    _logger.LogInformation("Script generated and validated: {Length} characters", generatedScript.Length);

                    // Parse scenes immediately for downstream tasks
                    parsedScenes = ParseScriptIntoScenes(generatedScript, planSpec.TargetDuration);
                    _logger.LogInformation("Parsed {SceneCount} scenes", parsedScenes.Count);

                    // Optional: Apply pacing optimization if enabled
                    if (_providerSettings.GetEnablePacingOptimization() &&
                        _pacingOptimizer != null &&
                        _pacingApplicationService != null &&
                        _providerSettings.GetAutoApplyPacingSuggestions())
                    {
                        try
                        {
                            _logger.LogInformation("Pacing optimization enabled, analyzing scenes");

                            var startTime = DateTime.UtcNow;
                            var pacingResult = await _pacingOptimizer.OptimizePacingAsync(
                                parsedScenes, brief, _llmProvider, true, PacingProfile.BalancedDocumentary, ct).ConfigureAwait(false);

                            var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
                            _logger.LogInformation(
                                "Pacing analysis completed in {Elapsed:F2}s. Confidence: {Confidence:F1}%",
                                elapsed, pacingResult.ConfidenceScore);

                            // Validate and apply
                            var optimizationLevel = Enum.Parse<Aura.Core.Models.Settings.OptimizationLevel>(
                                _providerSettings.GetPacingOptimizationLevel(), ignoreCase: true);
                            var minConfidence = _providerSettings.GetMinimumConfidenceThreshold();

                            var validation = _pacingApplicationService.ValidateSuggestions(
                                pacingResult, parsedScenes, planSpec.TargetDuration, minConfidence);

                            if (validation.IsValid)
                            {
                                parsedScenes = _timelineBuilder.ApplyPacingSuggestions(
                                    parsedScenes, pacingResult, optimizationLevel, minConfidence).ToList();

                                _logger.LogInformation(
                                    "Applied pacing suggestions. New total duration: {Duration}",
                                    TimeSpan.FromSeconds(parsedScenes.Sum(s => s.Duration.TotalSeconds)));
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Pacing optimization failed, continuing with original timings");
                        }
                    }

                    return generatedScript;

                case GenerationTaskType.AudioGeneration:
                    // Generate audio from parsed scenes
                    if (parsedScenes == null || generatedScript == null)
                    {
                        throw new InvalidOperationException("Script must be generated before audio");
                    }

                    var scriptLines = ConvertScenesToScriptLines(parsedScenes);

                    // Generate audio with retry logic and validation
                    narrationPath = await _retryWrapper.ExecuteWithRetryAsync(
                        async (ctRetry) =>
                        {
                            var audioPath = await _ttsProvider.SynthesizeAsync(scriptLines, voiceSpec, ctRetry).ConfigureAwait(false);

                            // Validate audio output
                            var minDuration = TimeSpan.FromSeconds(Math.Max(5, planSpec.TargetDuration.TotalSeconds * 0.3));
                            var audioValidation = _ttsValidator.ValidateAudioFile(audioPath, minDuration);

                            if (!audioValidation.IsValid)
                            {
                                _logger.LogWarning("Audio validation failed: {Issues}", string.Join(", ", audioValidation.Issues));
                                throw new ValidationException("Audio quality validation failed", audioValidation.Issues);
                            }

                            // Register for cleanup (will be promoted to artifact later)
                            _cleanupManager.RegisterTempFile(audioPath);

                        return audioPath;
                        },
                        "Audio Generation",
                        ct
                    ).ConfigureAwait(false);

                    _logger.LogInformation("Narration generated and validated at: {Path}", narrationPath);
                    state.NarrationPath = narrationPath;
                    return narrationPath;

                case GenerationTaskType.ImageGeneration:
                    // Generate images for specific scene
                    if (parsedScenes == null || resolvedImageProvider == null)
                    {
                        // Return empty asset list if no image provider available
                        return Array.Empty<Asset>();
                    }

                    // Extract scene index from task ID (e.g., "visual_0" -> 0)
                    var parts = node.TaskId.Split('_');
                    if (parts.Length < 2 || !int.TryParse(parts[^1], out int sceneIndex))
                    {
                        throw new InvalidOperationException($"Invalid image task ID: {node.TaskId}");
                    }

                    if (sceneIndex >= parsedScenes.Count)
                    {
                        _logger.LogWarning("Scene index {Index} out of range, skipping", sceneIndex);
                        return Array.Empty<Asset>();
                    }

                    var scene = parsedScenes[sceneIndex];
                    var visualSpec = new VisualSpec(planSpec.Style, brief.Aspect, Array.Empty<string>());

                    // Generate images with retry logic and validation
                    var assets = await _retryWrapper.ExecuteWithRetryAsync(
                        async (ctRetry) =>
                        {
                            var generatedAssets = await resolvedImageProvider.FetchOrGenerateAsync(scene, visualSpec, ctRetry).ConfigureAwait(false);

                            // Validate image assets
                            var imageValidation = _imageValidator.ValidateImageAssets(generatedAssets, expectedMinCount: 1);

                            if (!imageValidation.IsValid)
                            {
                                _logger.LogWarning("Image validation failed for scene {SceneIndex}: {Issues}",
                                    sceneIndex, string.Join(", ", imageValidation.Issues));

                                // For image generation, we can be more lenient and return empty if validation fails
                                // This prevents the entire pipeline from failing due to missing stock images
                                if (generatedAssets.Count == 0)
                                {
                                    _logger.LogWarning("No assets generated for scene {SceneIndex}, continuing with empty asset list", sceneIndex);
                                    return Array.Empty<Asset>();
                                }
                            }

                            // Register image files for cleanup (skip URLs)
                            foreach (var asset in generatedAssets)
                            {
                                if (!string.IsNullOrEmpty(asset.PathOrUrl) &&
                                    !asset.PathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                                    !asset.PathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                                {
                                    _cleanupManager.RegisterTempFile(asset.PathOrUrl);
                                }
                            }

                            return generatedAssets;
                        },
                        $"Image Generation (Scene {sceneIndex})",
                        ct
                    ).ConfigureAwait(false);

                    sceneAssets[sceneIndex] = assets;
                    _logger.LogDebug("Generated and validated {Count} assets for scene {Index}", assets.Count, sceneIndex);
                    return assets;

                case GenerationTaskType.VideoComposition:
                    // Final render combining all assets
                    if (parsedScenes == null)
                    {
                        throw new InvalidOperationException("Script and audio must be generated before composition");
                    }

                    var compositionNarrationPath = state.NarrationPath
                        ?? (recoveryResults.TryGetValue("audio", out var recoveredAudio) ? recoveredAudio as string : null)
                        ?? narrationPath;

                    var recoveryAudio = recoveryResults.TryGetValue("audio", out var recoveryAudioValue)
                        ? recoveryAudioValue
                        : RecoveryAudioNotFound;

                    _logger.LogInformation("[Composition] Narration path resolution: state.NarrationPath={StateNarration}, RecoveryResults={Recovery}, Closure={Closure}, Final={Final}",
                        state.NarrationPath ?? "NULL",
                        recoveryAudio,
                        narrationPath ?? "NULL",
                        compositionNarrationPath ?? "NULL");

                    if (string.IsNullOrEmpty(compositionNarrationPath))
                    {
                        throw new InvalidOperationException(
                            "Cannot render video: No narration audio available. " +
                            "TTS failed and no recovery audio was generated.  " +
                            "Check TTS provider configuration or enable silent audio fallback.");
                    }

                    if (!File.Exists(compositionNarrationPath))
                    {
                        throw new InvalidOperationException(
                            $"Cannot render video: Narration file not found at '{compositionNarrationPath}'. " +
                            "The audio file may have been cleaned up prematurely.");
                    }

                    // Report progress BEFORE starting render - this ensures frontend sees the transition
                    var renderStartMsg = "Starting video composition and rendering...";
                    _logger.LogInformation("[Stage Transition] {Message}", renderStartMsg);
                    progress?.Report(renderStartMsg);
                    detailedProgress?.Report(ProgressBuilder.CreateRenderProgress(0, renderStartMsg, correlationId: correlationId));

                    // Explicit logging at stage transition (70% mark) - helps diagnose hangs
                    _logger.LogInformation(
                        "[Stage Transition] Starting video composition at 70%% mark. " +
                        "Scenes: {SceneCount}, Assets: {AssetCount}, NarrationPath: {NarrationPath}",
                        parsedScenes.Count,
                        sceneAssets.Values.Sum(assets => assets.Count),
                        compositionNarrationPath);

                    var timeline = new Providers.Timeline(
                        Scenes: parsedScenes,
                        SceneAssets: sceneAssets,
                        NarrationPath: compositionNarrationPath,
                        MusicPath: string.Empty,
                        SubtitlesPath: null
                    );
                    state.Timeline = timeline;
                    state.NarrationPath = compositionNarrationPath;

                    // Report timeline creation progress
                    var timelineCreatedMsg = "Timeline created, preparing FFmpeg render...";
                    _logger.LogInformation("[Render Prep] {Message}", timelineCreatedMsg);
                    progress?.Report(timelineCreatedMsg);
                    detailedProgress?.Report(ProgressBuilder.CreateRenderProgress(10, timelineCreatedMsg, correlationId: correlationId));

                    // Track render progress with explicit logging and report to main progress
                    var renderStartTime = DateTime.UtcNow;
                    var lastProgressTime = DateTime.UtcNow;
                    var lastProgressPercent = 0f;
                    var renderProgress = new Progress<RenderProgress>(p =>
                    {
                        var now = DateTime.UtcNow;
                        var timeSinceLastProgress = now - lastProgressTime;

                        // Log at Information level (not Debug) so progress is visible
                        _logger.LogInformation(
                            "[Render Progress] {Percentage:F1}% - Stage: {Stage}, Elapsed: {Elapsed}, Remaining: {Remaining}",
                            p.Percentage, p.CurrentStage, p.Elapsed, p.Remaining);

                        // Report to main progress reporters so frontend sees updates
                        var renderProgressMsg = $"Rendering: {p.Percentage:F1}% - {p.CurrentStage}";
                        progress?.Report(renderProgressMsg);
                        detailedProgress?.Report(ProgressBuilder.CreateRenderProgress(
                            p.Percentage,
                            p.CurrentStage,
                            elapsed: now - renderStartTime,
                            remaining: p.Remaining,
                            correlationId: correlationId));

                        // Detect potential stalls
                        if (Math.Abs(p.Percentage - lastProgressPercent) < StallProgressThreshold && timeSinceLastProgress.TotalSeconds > StallTimeoutSeconds)
                        {
                            _logger.LogWarning(
                                "[Render Stall Warning] No progress change detected for {Seconds:F0} seconds at {Percentage:F1}%",
                                timeSinceLastProgress.TotalSeconds, p.Percentage);
                        }
                        else if (Math.Abs(p.Percentage - lastProgressPercent) >= StallProgressThreshold)
                        {
                            lastProgressTime = now;
                            lastProgressPercent = p.Percentage;
                        }
                    });

                    _logger.LogInformation("[Render Start] Beginning FFmpeg render operation");
                    progress?.Report("Executing FFmpeg render...");

                    try
                    {
                        var outputPath = await _videoComposer.RenderAsync(timeline, renderSpec, renderProgress, ct).ConfigureAwait(false);

                        _logger.LogInformation("[Render Complete] Video rendered successfully to: {Path}", outputPath);
                        progress?.Report("Video rendering complete");
                        detailedProgress?.Report(ProgressBuilder.CreateRenderProgress(100, "Video rendering complete", correlationId: correlationId));

                        // Store final video path in state for reliable fallback extraction
                        state.FinalVideoPath = outputPath;
                        state.RenderOutput = outputPath;

                        // CRITICAL FIX: Log the output path being returned to ensure it's captured
                        // The return value should automatically be stored in TaskResults["composition"]
                        _logger.LogInformation(
                            "[Composition Complete] Output path: {Path}, Exists: {Exists}, Length: {Length} bytes",
                            outputPath, File.Exists(outputPath), File.Exists(outputPath) ? new FileInfo(outputPath).Length : 0);

                        return outputPath;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[Composition] RenderAsync FAILED for job {JobId}:  {Message}", renderSpec.JobId ?? "unknown", ex.Message);

                        state.FinalVideoPath = null;
                        state.RenderOutput = null;
                        state.RenderError = ex.Message;

                        throw new InvalidOperationException(
                            $"Video composition failed: {ex.Message}. " +
                            "Check FFmpeg logs and asset validation errors.", ex);
                    }

                default:
                    throw new NotSupportedException($"Task type {node.TaskType} not supported");
            }
        };

        return state;
    }

    private sealed class TaskExecutorState
    {
        public Func<GenerationNode, CancellationToken, Task<object>> Executor { get; set; } = default!;
        public Providers.Timeline? Timeline { get; set; }
        public string? NarrationPath { get; set; }
        public string? FinalVideoPath { get; set; }
        public Dictionary<string, object> RecoveryResults { get; } = new();
        public string? RenderOutput { get; set; }
        public string? RenderError { get; set; }
    }

    /// <summary>
    /// Generates a topic-aware fallback script for Quick Demo when LLM generation fails.
    /// Uses the TopicAwareFallbackGenerator to create contextually appropriate content
    /// based on the topic category (technology, business, education, health, or default).
    /// </summary>
    private string GenerateSafeFallbackScript(Brief brief, PlanSpec planSpec)
    {
        var topic = brief.Topic ?? "Welcome to Aura Video Studio";
        _logger.LogInformation("Generating topic-aware fallback script for topic: {Topic}, duration: {Duration}s",
            topic, planSpec.TargetDuration.TotalSeconds);

        var script = _fallbackGenerator.GenerateFallbackScript(
            topic: topic,
            goal: brief.Goal,
            audience: brief.Audience,
            targetDuration: planSpec.TargetDuration,
            sceneCount: EstimateSceneCount(planSpec.TargetDuration));

        return script;
    }

    /// <summary>
    /// Estimates an appropriate scene count based on target duration.
    /// Uses approximately 30 seconds per scene as a baseline.
    /// </summary>
    private static int EstimateSceneCount(TimeSpan duration)
    {
        if (duration == default || duration.TotalSeconds < 30)
            return 3;

        var estimated = (int)(duration.TotalSeconds / 30);
        return Math.Max(3, Math.Min(estimated, 8));
    }

    /// <summary>
    /// Maps OrchestrationProgress from smart orchestrator to detailed GenerationProgress
    /// </summary>
    private static GenerationProgress MapOrchestrationProgressToDetailed(
        OrchestrationProgress orchestrationProgress,
        int currentItem,
        int totalItems,
        string? correlationId)
    {
        var currentStage = orchestrationProgress.CurrentStage.ToLowerInvariant();

        // Map stage name to GenerationProgress stage format
        // Note: Order matters! More specific checks must come before generic ones.
        // The "batch" check must come before "completed/complete" to avoid
        // incorrectly mapping "Batch completed (6/8 tasks done)" to PostProcess.
        var stage = currentStage switch
        {
            var s when s.Contains("starting") || s.Contains("analyzing") || s.Contains("task graph") => "Brief",
            var s when s.Contains("script") => "Script",
            var s when s.Contains("audio") || s.Contains("tts") || s.Contains("narration") => "TTS",
            var s when s.Contains("image") || s.Contains("visual") || s.Contains("asset") => "Images",
            var s when s.Contains("render") || s.Contains("compose") || s.Contains("composition") => "Rendering",
            // Check batch BEFORE completed/complete to handle "Batch completed (X/Y tasks done)"
            var s when s.Contains("batch") => DetermineStageFromBatchProgress(s, currentItem, totalItems, orchestrationProgress.ProgressPercentage),
            // Only transition to PostProcess when explicitly post-processing or truly complete
            var s when s.Contains("post") => "PostProcess",
            // "completed" or "complete" without "batch" - check if all tasks are done
            var s when (s.Contains("completed") || s.Contains("complete")) && totalItems > 0 && currentItem >= totalItems => "PostProcess",
            var s when s.Contains("completed") || s.Contains("complete") => DetermineStageFromProgress(orchestrationProgress.ProgressPercentage),
            _ => "Processing"
        };

        // Calculate stage-specific percent based on completed/total tasks
        double stagePercent;
        double overallPercent;

        if (totalItems > 0)
        {
            // Use task completion ratio for progress
            overallPercent = 5.0 + (double)currentItem / totalItems * 90.0; // 5% to 95%
            stagePercent = (double)currentItem / totalItems * 100.0;
        }
        else
        {
            // Fallback to orchestration progress
            stagePercent = orchestrationProgress.ProgressPercentage;
            overallPercent = StageWeights.CalculateOverallProgress(stage, stagePercent);
        }

        return new GenerationProgress
        {
            Stage = stage,
            OverallPercent = overallPercent,
            StagePercent = stagePercent,
            Message = orchestrationProgress.CurrentStage,
            CurrentItem = currentItem,
            TotalItems = totalItems,
            ElapsedTime = orchestrationProgress.ElapsedTime,
            CorrelationId = correlationId
        };
    }

    /// <summary>
    /// Determines the current stage based on overall progress percentage
    /// </summary>
    private static string DetermineStageFromProgress(double progressPercent)
    {
        return progressPercent switch
        {
            < 20 => "Script",
            < 50 => "TTS",
            < 70 => "Images",
            < 95 => "Rendering",
            _ => "PostProcess"
        };
    }

    /// <summary>
    /// Determines stage from batch progress message.
    /// Only transitions to PostProcess when ALL batch tasks are complete.
    /// </summary>
    /// <param name="stageMessage">The original stage message (currently unused, reserved for future parsing)</param>
    /// <param name="currentItem">Number of completed tasks</param>
    /// <param name="totalItems">Total number of tasks</param>
    /// <param name="progressPercent">Overall progress percentage</param>
    /// <returns>The appropriate stage name</returns>
    private static string DetermineStageFromBatchProgress(
        string stageMessage,
        int currentItem,
        int totalItems,
        double progressPercent)
    {
        // stageMessage is kept for potential future use (e.g., extracting task type from message)
        _ = stageMessage;

        // If we have valid task counts and all tasks are complete, allow PostProcess
        if (totalItems > 0 && currentItem >= totalItems)
        {
            return "PostProcess";
        }

        // Otherwise, determine stage based on progress percentage
        // This prevents premature transition to PostProcess when batch tasks are incomplete
        return DetermineStageFromProgress(progressPercent);
    }
}
