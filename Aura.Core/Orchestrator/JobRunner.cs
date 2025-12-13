using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Aura.Core.Artifacts;
using Aura.Core.Hardware;
using Aura.Core.Models;
using Aura.Core.Models.Events;
using Aura.Core.Providers;
using Aura.Core.Services.Memory;
using Aura.Core.Telemetry;
using Aura.Core.Validation;
using Microsoft.Extensions.Logging;

namespace Aura.Core.Orchestrator;

/// <summary>
/// Manages video generation jobs with background execution and progress tracking.
/// </summary>
public partial class JobRunner
{
    // Compiled regex patterns for performance (used in progress message parsing)
    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*%", RegexOptions.Compiled)]
    private static partial Regex PercentageRegex();

    [GeneratedRegex(@"(\d+)/(\d+)\s*tasks", RegexOptions.Compiled)]
    private static partial Regex TaskProgressRegex();

    private readonly ILogger<JobRunner> _logger;
    private readonly ArtifactManager _artifactManager;
    private readonly VideoOrchestrator _orchestrator;
    private readonly IHardwareDetector _hardwareDetector;
    private readonly Services.CheckpointManager? _checkpointManager;
    private readonly Services.CleanupService? _cleanupService;
    private readonly RunTelemetryCollector _telemetryCollector;
    private readonly Services.JobQueueService? _jobQueueService;
    private readonly Services.ProgressEstimator _progressEstimator;
    private readonly IMemoryPressureMonitor? _memoryMonitor;
    private readonly Dictionary<string, Job> _activeJobs = new();
    private readonly Dictionary<string, CancellationTokenSource> _jobCancellationTokens = new();
    private readonly Dictionary<string, Guid> _jobProjectIds = new();
    private readonly ConcurrentDictionary<string, IImageProvider?> _jobImageProviders = new();
    private readonly Services.ProgressAggregatorService? _progressAggregator;
    private readonly Services.CancellationOrchestrator? _cancellationOrchestrator;

    // Stuck job detection: tracks last progress update for each job
    private readonly Dictionary<string, JobProgressTracking> _jobProgressTracking = new();
    private readonly object _trackingLock = new();

    /// <summary>
    /// Default threshold in seconds after which a job is considered stuck if no progress is made.
    /// Can be configured via OrchestratorOptions.StuckDetectionThresholdSeconds.
    /// </summary>
    private const int DefaultStuckThresholdSeconds = 60;

    public event EventHandler<JobProgressEventArgs>? JobProgress;

    public JobRunner(
        ILogger<JobRunner> logger,
        ArtifactManager artifactManager,
        VideoOrchestrator orchestrator,
        IHardwareDetector hardwareDetector,
        RunTelemetryCollector telemetryCollector,
        Services.CheckpointManager? checkpointManager = null,
        Services.CleanupService? cleanupService = null,
        Services.JobQueueService? jobQueueService = null,
        Services.ProgressEstimator? progressEstimator = null,
        IMemoryPressureMonitor? memoryMonitor = null,
        Services.ProgressAggregatorService? progressAggregator = null,
        Services.CancellationOrchestrator? cancellationOrchestrator = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(artifactManager);
        ArgumentNullException.ThrowIfNull(orchestrator);
        ArgumentNullException.ThrowIfNull(hardwareDetector);
        ArgumentNullException.ThrowIfNull(telemetryCollector);

        _logger = logger;
        _artifactManager = artifactManager;
        _orchestrator = orchestrator;
        _hardwareDetector = hardwareDetector;
        _telemetryCollector = telemetryCollector;
        _checkpointManager = checkpointManager;
        _cleanupService = cleanupService;
        _jobQueueService = jobQueueService;
        _progressEstimator = progressEstimator ?? new Services.ProgressEstimator();
        _memoryMonitor = memoryMonitor;
        _progressAggregator = progressAggregator;
        _cancellationOrchestrator = cancellationOrchestrator;
    }

    /// <summary>
    /// Creates a new job and starts execution.
    /// </summary>
    public async Task<Job> CreateAndStartJobAsync(
        Brief brief,
        PlanSpec planSpec,
        VoiceSpec voiceSpec,
        RenderSpec renderSpec,
        string? correlationId = null,
        bool isQuickDemo = false,
        CancellationToken ct = default,
        IImageProvider? imageProvider = null)
    {
        await Task.CompletedTask;
        ArgumentNullException.ThrowIfNull(brief);
        ArgumentNullException.ThrowIfNull(planSpec);
        ArgumentNullException.ThrowIfNull(voiceSpec);
        ArgumentNullException.ThrowIfNull(renderSpec);

        var jobId = Guid.NewGuid().ToString();
        _logger.LogInformation("Creating new job with ID: {JobId}, Topic: {Topic}, IsQuickDemo: {IsQuickDemo}",
            jobId, brief.Topic, isQuickDemo);

        var nowUtc = DateTime.UtcNow;
        // Ensure render spec carries the job id so downstream components (FFmpeg) can name logs correctly
        var renderSpecWithJobId = renderSpec.JobId == null
            ? renderSpec with { JobId = jobId }
            : renderSpec;

        var job = new Job
        {
            Id = jobId,
            Stage = "Script",
            Status = JobStatus.Queued,
            CorrelationId = correlationId ?? Guid.NewGuid().ToString(),
            Brief = brief,
            PlanSpec = planSpec,
            VoiceSpec = voiceSpec,
            RenderSpec = renderSpecWithJobId,
            CreatedUtc = nowUtc,
            QueuedUtc = nowUtc,
            IsQuickDemo = isQuickDemo
        };

        _activeJobs[job.Id] = job;
        _artifactManager.SaveJob(job);

        if (imageProvider != null)
        {
            _jobImageProviders[job.Id] = imageProvider;
        }

        _logger.LogInformation("Job {JobId} saved to active jobs and artifact storage", jobId);

        // Create a standalone cancellation token source for job cancellation only.
        // Do NOT link to the HTTP request cancellation token (ct) because:
        // - The HTTP request completes immediately after job creation returns
        // - When ct is cancelled (request completes), it would cancel the background job
        // - Jobs should only be cancellable via explicit CancelJob() calls
        var jobCts = new CancellationTokenSource();
        _jobCancellationTokens[job.Id] = jobCts;

        _logger.LogInformation("Starting background execution for job {JobId}", jobId);

        // Start execution in background without linking to HTTP request token
        _ = Task.Run(async () => await ExecuteJobAsync(job.Id, jobCts.Token).ConfigureAwait(false), CancellationToken.None);

        return job;
    }

    /// <summary>
    /// Gets a job by ID.
    /// </summary>
    public Job? GetJob(string jobId)
    {
        if (_activeJobs.TryGetValue(jobId, out var job))
        {
            return job;
        }

        return _artifactManager.LoadJob(jobId);
    }

    /// <summary>
    /// Checks if a job appears to be stuck (no progress for a specified threshold).
    /// Can be used by SSE streaming or monitoring to detect and warn about stalled jobs.
    /// </summary>
    /// <param name="jobId">The ID of the job to check</param>
    /// <param name="thresholdSeconds">Number of seconds without progress to consider stuck (default: 60)</param>
    /// <returns>A tuple of (isStuck, stuckDuration, lastPercent, lastStage)</returns>
    public (bool IsStuck, TimeSpan StuckDuration, int LastPercent, string? LastStage) CheckJobStuckStatus(
        string jobId,
        int thresholdSeconds = DefaultStuckThresholdSeconds)
    {
        var (isStuck, stuckDuration) = CheckIfJobIsStuck(jobId, thresholdSeconds);

        lock (_trackingLock)
        {
            if (_jobProgressTracking.TryGetValue(jobId, out var tracking))
            {
                // Emit warning for stuck job (only once until progress resumes)
                if (isStuck && !tracking.StuckWarningEmitted)
                {
                    _logger.LogWarning(
                        "Job {JobId} appears stuck: no progress for {Duration:F1}s at {Stage} ({Percent}%)",
                        jobId, stuckDuration.TotalSeconds, tracking.LastStage ?? "Unknown", tracking.LastPercent);
                    MarkStuckWarningEmitted(jobId);
                }

                return (isStuck, stuckDuration, tracking.LastPercent, tracking.LastStage);
            }
        }

        return (false, TimeSpan.Zero, 0, null);
    }

    /// <summary>
    /// Lists all recent jobs.
    /// </summary>
    public List<Job> ListJobs(int limit = 50)
    {
        return _artifactManager.ListJobs(limit);
    }

    /// <summary>
    /// Cancels a running job.
    /// </summary>
    /// <param name="jobId">The ID of the job to cancel</param>
    /// <returns>True if the job was found and cancellation was requested, false otherwise</returns>
    public bool CancelJob(string jobId)
    {
        _logger.LogInformation("Cancellation requested for job {JobId}", jobId);

        // Check if job is active and has a cancellation token
        if (_jobCancellationTokens.TryGetValue(jobId, out var cts))
        {
            try
            {
                cts.Cancel();
                _logger.LogInformation("Cancellation token triggered for job {JobId}", jobId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cancelling job {JobId}", jobId);
                return false;
            }
        }

        // Job not found or not active
        var job = GetJob(jobId);
        if (job == null)
        {
            _logger.LogWarning("Cannot cancel job {JobId}: job not found", jobId);
            return false;
        }

        // Job exists but is not running
        if (job.Status != JobStatus.Running && job.Status != JobStatus.Queued)
        {
            _logger.LogWarning("Cannot cancel job {JobId}: job is in status {Status}", jobId, job.Status);
            return false;
        }

        _logger.LogWarning("Job {JobId} is marked as active but has no cancellation token", jobId);
        return false;
    }

    /// <summary>
    /// Forcefully marks a job as failed due to a detected stall and attempts to cancel execution.
    /// </summary>
    /// <param name="jobId">The job identifier</param>
    /// <param name="stage">Stage where the stall was detected</param>
    /// <param name="percent">Last reported percent</param>
    /// <param name="stuckDuration">How long the job has been stuck</param>
    /// <param name="correlationId">Optional correlation id for logging</param>
    /// <returns>The updated job or null if the job was not found</returns>
    public Job? FailJobAsStalled(
        string jobId,
        string? stage,
        int percent,
        TimeSpan stuckDuration,
        string? correlationId = null)
    {
        var job = GetJob(jobId);
        if (job == null)
        {
            _logger.LogWarning("Cannot mark stalled job {JobId}: job not found", jobId);
            return null;
        }

        var effectiveStage = stage ?? job.Stage;
        var effectiveCorrelationId = correlationId ?? job.CorrelationId ?? string.Empty;
        var message = $"Job stalled at {effectiveStage} stage with no progress for {stuckDuration.TotalSeconds:F0}s";

        _logger.LogError("[Job {JobId}] {Message}", jobId, message);

        // Attempt to cancel any running work to free resources
        if (_jobCancellationTokens.TryGetValue(jobId, out var cts))
        {
            try
            {
                cts.Cancel();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cancel stalled job {JobId}", jobId);
            }
        }

        // Stop auxiliary tracking to avoid leaking monitors
        _progressEstimator.ClearHistory(jobId);
        _memoryMonitor?.StopMonitoring(jobId);
        CleanupProgressTracking(jobId);

        var failure = new JobFailure
        {
            Stage = effectiveStage,
            Message = message,
            CorrelationId = effectiveCorrelationId,
            FailedAt = DateTime.UtcNow,
            ErrorCode = "E305-JOB_STALLED",
            SuggestedActions = new[]
            {
                "Retry the render with lower resolution or software encoding",
                "Verify FFmpeg is available and not blocked by antivirus",
                "Check disk space and write permissions on the render output directory"
            }
        };

        var updatedJob = UpdateJob(
            job,
            status: JobStatus.Failed,
            stage: effectiveStage,
            percent: percent,
            errorMessage: message,
            failureDetails: failure,
            progressMessage: message,
            finishedAt: DateTime.UtcNow);

        return updatedJob;
    }

    /// <summary>
    /// Pauses a running job.
    /// </summary>
    /// <param name="jobId">The ID of the job to pause</param>
    /// <returns>True if the job was successfully paused, false otherwise</returns>
    public bool PauseJob(string jobId)
    {
        _logger.LogInformation("Pause requested for job {JobId}", jobId);

        var job = GetJob(jobId);
        if (job == null)
        {
            _logger.LogWarning("Cannot pause job {JobId}: job not found", jobId);
            return false;
        }

        if (job.Status != JobStatus.Running)
        {
            _logger.LogWarning("Cannot pause job {JobId}: job is in status {Status}", jobId, job.Status);
            return false;
        }

        // Update job status to paused
        var updatedJob = job with { Status = JobStatus.Paused };
        _activeJobs[jobId] = updatedJob;
        _artifactManager.SaveJob(updatedJob);

        _logger.LogInformation("Job {JobId} paused successfully", jobId);
        return true;
    }

    /// <summary>
    /// Resumes a paused job.
    /// </summary>
    /// <param name="jobId">The ID of the job to resume</param>
    /// <returns>True if the job was successfully resumed, false otherwise</returns>
    public bool ResumeJob(string jobId)
    {
        _logger.LogInformation("Resume requested for job {JobId}", jobId);

        var job = GetJob(jobId);
        if (job == null)
        {
            _logger.LogWarning("Cannot resume job {JobId}: job not found", jobId);
            return false;
        }

        if (job.Status != JobStatus.Paused)
        {
            _logger.LogWarning("Cannot resume job {JobId}: job is in status {Status}", jobId, job.Status);
            return false;
        }

        // Update job status back to running
        var updatedJob = job with { Status = JobStatus.Running };
        _activeJobs[jobId] = updatedJob;
        _artifactManager.SaveJob(updatedJob);

        _logger.LogInformation("Job {JobId} resumed successfully", jobId);
        return true;
    }

    /// <summary>
    /// Retries a failed job with exponential backoff
    /// </summary>
    public async Task<bool> RetryJobAsync(string jobId, CancellationToken ct = default)
    {
        var job = GetJob(jobId);
        if (job == null)
        {
            _logger.LogWarning("Cannot retry job {JobId}: job not found", jobId);
            return false;
        }

        if (job.Status != JobStatus.Failed)
        {
            _logger.LogWarning("Cannot retry job {JobId}: job has not failed (status: {Status})",
                jobId, job.Status);
            return false;
        }

        // Check if retry is allowed
        if (_jobQueueService != null && !_jobQueueService.CanRetryJob(jobId))
        {
            _logger.LogWarning("Cannot retry job {JobId}: max retry count reached or backoff not elapsed",
                jobId);
            return false;
        }

        _logger.LogInformation("Retrying job {JobId}", jobId);

        // Create a new job with same parameters
        if (job.Brief != null && job.PlanSpec != null && job.VoiceSpec != null && job.RenderSpec != null)
        {
            var retriedJob = await CreateAndStartJobAsync(
                job.Brief,
                job.PlanSpec,
                job.VoiceSpec,
                job.RenderSpec,
                job.CorrelationId,
                job.IsQuickDemo,
                ct
            ).ConfigureAwait(false);

            // Record retry in queue service
            if (_jobQueueService != null)
            {
                await _jobQueueService.RetryJobAsync(jobId, priority: 3, ct).ConfigureAwait(false);
            }

            return true;
        }

        _logger.LogWarning("Cannot retry job {JobId}: missing job parameters", jobId);
        return false;
    }

    /// <summary>
    /// Executes a job through all stages.
    /// </summary>
    private async Task ExecuteJobAsync(string jobId, CancellationToken ct)
    {
        CancellationTokenSource? stuckMonitorCts = null;
        Task? stuckMonitor = null;

        try
        {
            var job = GetJob(jobId);
            if (job == null)
            {
                _logger.LogError("Job {JobId} not found", jobId);
                return;
            }

            _logger.LogInformation("Starting job {JobId}", jobId);

            // Start memory monitoring for this job
            _memoryMonitor?.StartMonitoring(jobId);

            // Start telemetry collection for this job
            _telemetryCollector.StartCollection(jobId, job.CorrelationId ?? Guid.NewGuid().ToString());
            _logger.LogInformation("Started telemetry collection for job {JobId}", jobId);

            // Create project state for checkpointing if checkpoint manager is available
            Guid? projectId = null;
            if (_checkpointManager != null && job.Brief != null && job.PlanSpec != null &&
                job.VoiceSpec != null && job.RenderSpec != null)
            {
                try
                {
                    projectId = await _checkpointManager.CreateProjectStateAsync(
                        job.Brief.Topic ?? "Untitled Project",
                        jobId,
                        job.Brief,
                        job.PlanSpec,
                        job.VoiceSpec,
                        job.RenderSpec,
                        ct).ConfigureAwait(false);

                    _jobProjectIds[jobId] = projectId.Value;
                    _logger.LogInformation("Created project state {ProjectId} for job {JobId}", projectId, jobId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to create project state for job {JobId}, continuing without checkpoints", jobId);
                }
            }

            // Update to running status with initialization message
            job = UpdateJob(job,
                status: JobStatus.Running,
                percent: 0,
                stage: "Initialization",
                progressMessage: "Initializing job execution",
                startedUtc: DateTime.UtcNow);

            // Start a background monitor to detect stalled jobs (e.g., render hang around 70-80%)
            stuckMonitorCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            stuckMonitor = Task.Run(async () =>
            {
                while (!stuckMonitorCts.Token.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), stuckMonitorCts.Token).ConfigureAwait(false);

                    var (isStuck, stuckDuration, lastPercent, lastStage) = CheckJobStuckStatus(jobId, thresholdSeconds: 90);
                    if (!isStuck)
                    {
                        continue;
                    }

                    var ffmpegLog = TryReadFfmpegLog(jobId);
                    var message = $"Job appears stuck for {stuckDuration.TotalSeconds:F0}s at {lastPercent}% ({lastStage ?? "Unknown"}).";
                    _logger.LogError("[Job {JobId}] {Message}", jobId, message);

                    var failure = new JobFailure
                    {
                        Stage = lastStage ?? job.Stage,
                        Message = message,
                        CorrelationId = job.CorrelationId ?? string.Empty,
                        FailedAt = DateTime.UtcNow,
                        ErrorCode = "E305-JOB_STALLED",
                        StderrSnippet = ffmpegLog,
                        SuggestedActions = new[]
                        {
                            "Retry the render with different settings (e.g., software encoding)",
                            "Verify FFmpeg is accessible and not blocked by antivirus",
                            "Check GPU/CPU utilization to ensure the process is not paused"
                        }
                    };

                    job = UpdateJob(
                        job,
                        status: JobStatus.Failed,
                        progressMessage: message,
                        errorMessage: message,
                        failureDetails: failure,
                        finishedAt: DateTime.UtcNow);

                    stuckMonitorCts.Cancel();
                }
            }, stuckMonitorCts.Token);

            // Detect system profile for orchestration
            _logger.LogInformation("[Job {JobId}] Detecting system hardware...", jobId);
            var systemProfile = await _hardwareDetector.DetectSystemAsync().ConfigureAwait(false);
            _logger.LogInformation("[Job {JobId}] System Profile - Tier: {Tier}, CPU: {Cores} cores, RAM: {Ram}GB, GPU: {Gpu}",
                jobId, systemProfile.Tier, systemProfile.LogicalCores, systemProfile.RamGB,
                systemProfile.Gpu?.Model ?? "None");

            // Create progress reporter with detailed stage tracking and ETA estimation
            var progress = new Progress<string>(message =>
            {
                _logger.LogInformation("[Job {JobId}] {Message}", jobId, message);

                // Determine stage and progress from message
                var (stage, percent, progressMsg) = ParseProgressMessage(message, job.Stage, job.Percent);

                // Record progress for ETA calculation
                _progressEstimator.RecordProgress(jobId, percent, DateTime.UtcNow);

                // Update stuck task detection tracking
                UpdateProgressTracking(jobId, percent, stage);

                // Update peak memory tracking
                _memoryMonitor?.UpdatePeakMemory(jobId);

                // Check for memory pressure and force collection if needed
                _memoryMonitor?.ForceCollectionIfNeeded();

                // Calculate ETA and elapsed time
                var eta = _progressEstimator.EstimateTimeRemaining(jobId, percent);
                var elapsed = _progressEstimator.CalculateElapsedTime(jobId);

                job = UpdateJob(job,
                    stage: stage,
                    percent: percent,
                    logs: new List<string>(job.Logs) { $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}" },
                    progressMessage: progressMsg,
                    eta: eta);
            });

            // Create detailed progress reporter
            var detailedProgress = new Progress<GenerationProgress>(generationProgress =>
            {
                _logger.LogInformation("[Job {JobId}] Stage: {Stage}, Overall: {Overall}%, {Message}",
                    jobId, generationProgress.Stage, generationProgress.OverallPercent, generationProgress.Message);

                // Update job with detailed progress
                job = UpdateJobWithProgress(job, generationProgress);

                // Update stuck task detection tracking
                UpdateProgressTracking(jobId, (int)Math.Round(generationProgress.OverallPercent), generationProgress.Stage);

                // Raise event for SSE streaming
                JobProgress?.Invoke(this, new JobProgressEventArgs
                {
                    JobId = jobId,
                    Stage = generationProgress.Stage,
                    Progress = (int)Math.Round(generationProgress.OverallPercent),
                    Status = job.Status,
                    Message = generationProgress.Message,
                    CorrelationId = job.CorrelationId ?? string.Empty
                });
            });

            // Execute orchestrator with system profile and detailed progress
            _jobImageProviders.TryRemove(jobId, out var imageProviderOverride);

            var generationResult = await _orchestrator.GenerateVideoResultAsync(
                job.Brief!,
                job.PlanSpec!,
                job.VoiceSpec!,
                job.RenderSpec!,
                systemProfile,
                progress,
                ct,
                jobId,
                job.CorrelationId,
                job.IsQuickDemo,
            detailedProgress,
            imageProviderOverride
            ).ConfigureAwait(false);

            if (string.IsNullOrEmpty(generationResult.OutputPath))
            {
                var errorMsg = "Video generation completed but no output file was produced.  " +
                               "This typically indicates FFmpeg never executed due to missing timeline prerequisites. ";

                _logger.LogError("[Job {JobId}] {Error}", jobId, errorMsg);

                job = UpdateJob(job,
                    status: JobStatus.Failed,
                    percent: job.Percent,
                    errorMessage: errorMsg,
                    failureDetails: new JobFailure
                    {
                        ErrorCode = "E305-OUTPUT_NULL",
                        Message = errorMsg,
                        Stage = "Composition",
                        SuggestedActions = new[]
                        {
                            "Check if TTS provider is configured and working",
                            "Verify image generation completed successfully",
                            "Check FFmpeg logs at C:\\Users\\User\\AppData\\Local\\Aura\\logs",
                            "Ensure FFmpeg path is correctly configured"
                        }
                    });

                throw new InvalidOperationException(errorMsg);
            }

            // Add final artifact
            var artifact = _artifactManager.CreateArtifact(jobId, "video.mp4", generationResult.OutputPath, "video/mp4");
            var artifacts = new List<JobArtifact>(job.Artifacts) { artifact };

            if (generationResult.EditableTimeline != null)
            {
                await _artifactManager.SaveTimelineAsync(jobId, generationResult.EditableTimeline, ct).ConfigureAwait(false);
            }

            // Mark project as completed if checkpoint manager is available
            if (_checkpointManager != null && _jobProjectIds.TryGetValue(jobId, out var completedProjectId))
            {
                try
                {
                    await _checkpointManager.CompleteProjectAsync(completedProjectId, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to mark project {ProjectId} as completed", completedProjectId);
                }
            }

            // End telemetry collection and persist to disk
            var telemetryPath = _telemetryCollector.EndCollection();
            if (telemetryPath != null)
            {
                _logger.LogInformation("Telemetry data persisted to {Path}", telemetryPath);
            }

            // Stop memory monitoring and log statistics
            if (_memoryMonitor != null)
            {
                var memStats = _memoryMonitor.StopMonitoring(jobId);
                _logger.LogInformation(
                    "Job {JobId} memory usage: Start={StartMb:F1}MB, Peak={PeakMb:F1}MB, End={EndMb:F1}MB, Delta={DeltaMb:+0.0;-0.0}MB, GC(G0={G0},G1={G1},G2={G2})",
                    jobId, memStats.StartMemoryMb, memStats.PeakMemoryMb, memStats.EndMemoryMb,
                    memStats.MemoryDeltaMb, memStats.Gen0Collections, memStats.Gen1Collections, memStats.Gen2Collections);
            }

            // Clear progress estimation history
            _progressEstimator.ClearHistory(jobId);

            // Clear retry state if job queue service is available
            _jobQueueService?.ClearRetryState(jobId);

            // Mark as done with output path
            job = UpdateJob(job,
                status: JobStatus.Done,
                percent: 100,
                stage: "Complete",
                artifacts: artifacts,
                outputPath: generationResult.OutputPath,
                finishedAt: DateTime.UtcNow,
                completedUtc: DateTime.UtcNow);

            _logger.LogInformation("Job {JobId} completed successfully. Output: {OutputPath}", jobId, generationResult.OutputPath);

            if (stuckMonitorCts != null)
            {
                stuckMonitorCts.Cancel();
                if (stuckMonitor != null)
                {
                    await Task.WhenAny(stuckMonitor, Task.Delay(50, CancellationToken.None)).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Job {JobId} was cancelled", jobId);
            var job = GetJob(jobId);
            if (job != null)
            {
                _logger.LogInformation("Beginning cleanup for cancelled job {JobId} (Stage: {Stage}, Percent: {Percent}%)",
                    jobId, job.Stage, job.Percent);

                // Clean up temporary files and proxies
                if (_cleanupService != null)
                {
                    _logger.LogInformation("Cleaning up temporary files and proxies for cancelled job {JobId}", jobId);
                    _cleanupService.CleanupJob(jobId);
                    _logger.LogInformation("Cleanup completed for job {JobId}", jobId);
                }
                else
                {
                    _logger.LogWarning("CleanupService not available, temporary files may remain for job {JobId}", jobId);
                }

                // Mark project as cancelled if checkpoint manager is available
                if (_checkpointManager != null && _jobProjectIds.TryGetValue(jobId, out var cancelledProjectId))
                {
                    try
                    {
                        await _checkpointManager.CancelProjectAsync(cancelledProjectId, default).ConfigureAwait(false);
                        _logger.LogInformation("Project {ProjectId} marked as cancelled", cancelledProjectId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to mark project {ProjectId} as cancelled", cancelledProjectId);
                    }
                }

                // End telemetry collection for cancelled job
                var telemetryPath = _telemetryCollector.EndCollection();
                if (telemetryPath != null)
                {
                    _logger.LogInformation("Telemetry data persisted for cancelled job {JobId} to {Path}", jobId, telemetryPath);
                }

                // Stop memory monitoring and log statistics
                _memoryMonitor?.StopMonitoring(jobId);

                // Clear progress estimation history
                _progressEstimator.ClearHistory(jobId);

                // Add cancellation message to logs so it's visible in UI
                var cancelLog = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Job was cancelled by user - cleanup completed";
                var updatedLogs = new List<string>(job.Logs) { cancelLog };

                UpdateJob(job,
                    status: JobStatus.Canceled,
                    errorMessage: "Job was cancelled by user",
                    logs: updatedLogs,
                    finishedAt: DateTime.UtcNow,
                    canceledUtc: DateTime.UtcNow);

                _logger.LogInformation("Job {JobId} marked as Canceled with cleanup complete", jobId);
            }
        }
        catch (ValidationException vex)
        {
            _logger.LogError("Job {JobId} failed validation: {Message}", jobId, vex.Message);
            var job = GetJob(jobId);
            if (job != null)
            {
                // Mark project as failed if checkpoint manager is available
                if (_checkpointManager != null && _jobProjectIds.TryGetValue(jobId, out var failedProjectId))
                {
                    try
                    {
                        await _checkpointManager.FailProjectAsync(failedProjectId, vex.Message, default).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to mark project {ProjectId} as failed", failedProjectId);
                    }
                }

                var failureDetails = new JobFailure
                {
                    Stage = job.Stage,
                    Message = vex.Message,
                    CorrelationId = job.CorrelationId ?? string.Empty,
                    FailedAt = DateTime.UtcNow,
                    ErrorCode = "E400-VALIDATION",
                    SuggestedActions = new[]
                    {
                        "Review the validation errors and adjust your input",
                        "Try simplifying your brief or reducing the duration",
                        "Check that all required providers are configured",
                        "Verify system resources are sufficient"
                    }
                };

                // End telemetry collection for failed validation
                var telemetryPath = _telemetryCollector.EndCollection();
                if (telemetryPath != null)
                {
                    _logger.LogInformation("Telemetry data persisted for failed job {JobId} to {Path}", jobId, telemetryPath);
                }

                // Stop memory monitoring
                _memoryMonitor?.StopMonitoring(jobId);

                // Clear progress estimation history
                _progressEstimator.ClearHistory(jobId);

                // Add detailed validation errors to logs
                var errorLog = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] VALIDATION ERROR: {vex.Message}";
                var updatedLogs = new List<string>(job.Logs) { errorLog };

                if (vex.Issues.Count != 0)
                {
                    foreach (var issue in vex.Issues)
                    {
                        updatedLogs.Add($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]   - {issue}");
                    }
                }

                UpdateJob(job,
                    status: JobStatus.Failed,
                    errorMessage: vex.Message,
                    failureDetails: failureDetails,
                    logs: updatedLogs,
                    finishedAt: DateTime.UtcNow);

                _artifactManager.SaveJob(job);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} failed: {Message}\nStack Trace: {StackTrace}",
                jobId, ex.Message, ex.StackTrace);
            var job = GetJob(jobId);
            if (job != null)
            {
                // Mark project as failed if checkpoint manager is available
                if (_checkpointManager != null && _jobProjectIds.TryGetValue(jobId, out var failedProjectId))
                {
                    try
                    {
                        await _checkpointManager.FailProjectAsync(failedProjectId, ex.Message, default).ConfigureAwait(false);
                    }
                    catch (Exception checkpointEx)
                    {
                        _logger.LogWarning(checkpointEx, "Failed to mark project {ProjectId} as failed", failedProjectId);
                    }
                }

                // End telemetry collection for failed job
                var telemetryPath = _telemetryCollector.EndCollection();
                if (telemetryPath != null)
                {
                    _logger.LogInformation("Telemetry data persisted for failed job {JobId} to {Path}", jobId, telemetryPath);
                }

                // Stop memory monitoring
                _memoryMonitor?.StopMonitoring(jobId);

                // Clear progress estimation history
                _progressEstimator.ClearHistory(jobId);

                var failureDetails = CreateFailureDetails(job, ex);

                // Add error to logs so it's visible in UI
                var errorLog = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR: {GetFriendlyErrorMessage(ex)}";
                var updatedLogs = new List<string>(job.Logs) { errorLog };

                // Add inner exception details if available
                if (ex.InnerException != null)
                {
                    updatedLogs.Add($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Inner Exception: {ex.InnerException.Message}");
                }

                // Add stack trace for debugging (truncated)
                var stackLines = ex.StackTrace?.Split('\n').Take(5) ?? Array.Empty<string>();
                foreach (var line in stackLines)
                {
                    updatedLogs.Add($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]   {line.Trim()}");
                }

                UpdateJob(job,
                    status: JobStatus.Failed,
                    errorMessage: GetFriendlyErrorMessage(ex),
                    failureDetails: failureDetails,
                    logs: updatedLogs,
                    finishedAt: DateTime.UtcNow);

                // Save job state to artifact manager
                _artifactManager.SaveJob(job);
            }
        }
        finally
        {
            if (stuckMonitorCts != null)
            {
                stuckMonitorCts.Cancel();
                if (stuckMonitor != null)
                {
                    await Task.WhenAny(stuckMonitor, Task.Delay(50, CancellationToken.None)).ConfigureAwait(false);
                }
            }

            _activeJobs.Remove(jobId);

            // Clean up cancellation token
            if (_jobCancellationTokens.TryGetValue(jobId, out var cts))
            {
                cts.Dispose();
                _jobCancellationTokens.Remove(jobId);
            }

            // Clean up progress tracking for stuck detection
            CleanupProgressTracking(jobId);
        }
    }

    /// <summary>
    /// Updates a job with detailed progress information and persists changes.
    /// </summary>
    private Job UpdateJobWithProgress(
        Job job,
        GenerationProgress generationProgress)
    {
        // Add to progress history
        var updatedHistory = new List<GenerationProgress>(job.ProgressHistory) { generationProgress };

        // Calculate percent from overall progress
        var percent = (int)Math.Round(generationProgress.OverallPercent);

        // Build log message from progress
        var logMessage = generationProgress.SubstageDetail != null
            ? $"{generationProgress.Message} - {generationProgress.SubstageDetail}"
            : generationProgress.Message;
        var logs = new List<string>(job.Logs)
        {
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {logMessage}"
        };

        // Update using existing method
        var updatedJob = UpdateJob(
            job,
            stage: generationProgress.Stage,
            percent: percent,
            logs: logs,
            progressMessage: generationProgress.Message,
            eta: generationProgress.EstimatedTimeRemaining
        );

        // Add progress history and current progress
        var finalJob = updatedJob with
        {
            ProgressHistory = updatedHistory,
            CurrentProgress = generationProgress
        };

        // Update active jobs and persist
        _activeJobs[finalJob.Id] = finalJob;
        _artifactManager.SaveJob(finalJob);

        return finalJob;
    }

    /// <summary>
    /// Updates a job and persists changes with state validation and monotonic progress.
    /// </summary>
    private Job UpdateJob(
        Job job,
        string? stage = null,
        JobStatus? status = null,
        int? percent = null,
        TimeSpan? eta = null,
        List<JobArtifact>? artifacts = null,
        List<string>? logs = null,
        DateTime? finishedAt = null,
        string? errorMessage = null,
        JobFailure? failureDetails = null,
        string? progressMessage = null,
        DateTime? startedUtc = null,
        DateTime? completedUtc = null,
        DateTime? canceledUtc = null,
        string? outputPath = null)
    {
        // Validate state transition if status is changing
        var newStatus = status ?? job.Status;
        if (newStatus != job.Status && !job.CanTransitionTo(newStatus))
        {
            _logger.LogWarning("[Job {JobId}] Invalid state transition from {OldStatus} to {NewStatus}, transition rejected",
                job.Id, job.Status, newStatus);
            newStatus = job.Status; // Keep old status
        }

        // Enforce monotonic progress (never decrease)
        var newPercent = percent ?? job.Percent;
        if (newPercent < job.Percent)
        {
            _logger.LogDebug("[Job {JobId}] Progress would decrease from {OldPercent}% to {NewPercent}%, keeping old value",
                job.Id, job.Percent, newPercent);
            newPercent = job.Percent;
        }

        // Ensure EndedUtc is set for terminal states
        DateTime? endedUtc = job.EndedUtc;
        if (IsTerminalStatus(newStatus) && endedUtc == null)
        {
            endedUtc = completedUtc ?? canceledUtc ?? DateTime.UtcNow;
        }

        var updated = job with
        {
            Stage = stage ?? job.Stage,
            Status = newStatus,
            Percent = Math.Clamp(newPercent, 0, 100),
            Eta = eta ?? job.Eta,
            Artifacts = artifacts ?? job.Artifacts,
            Logs = logs ?? job.Logs,
            FinishedAt = finishedAt ?? job.FinishedAt,
            ErrorMessage = errorMessage ?? job.ErrorMessage,
            FailureDetails = failureDetails ?? job.FailureDetails,
            StartedUtc = startedUtc ?? job.StartedUtc,
            CompletedUtc = completedUtc ?? job.CompletedUtc,
            CanceledUtc = canceledUtc ?? job.CanceledUtc,
            EndedUtc = endedUtc,
            OutputPath = outputPath ?? job.OutputPath
        };

        _activeJobs[job.Id] = updated;
        _artifactManager.SaveJob(updated);

        // Log job state changes for debugging (include OutputPath for terminal states)
        if (IsTerminalStatus(updated.Status))
        {
            _logger.LogInformation(
                "[Job {JobId}] Terminal state: Status={Status}, Stage={Stage}, Percent={Percent}%, OutputPath={OutputPath}, Artifacts={ArtifactCount}",
                updated.Id, updated.Status, updated.Stage, updated.Percent,
                updated.OutputPath ?? "NULL", updated.Artifacts?.Count ?? 0);
        }
        else
        {
            _logger.LogInformation("[Job {JobId}] Updated: Status={Status}, Stage={Stage}, Percent={Percent}%",
                updated.Id, updated.Status, updated.Stage, updated.Percent);
        }

        // Raise progress event with detailed information
        var eventArgs = new JobProgressEventArgs(
            jobId: updated.Id,
            progress: updated.Percent,
            status: updated.Status,
            stage: updated.Stage,
            message: progressMessage ?? GetProgressMessage(updated),
            correlationId: updated.CorrelationId ?? string.Empty,
            eta: updated.Eta
        );

        JobProgress?.Invoke(this, eventArgs);

        return updated;
    }

    /// <summary>
    /// Checks if a status is terminal (no further transitions allowed)
    /// </summary>
    private static bool IsTerminalStatus(JobStatus status)
    {
        return status is JobStatus.Done or JobStatus.Succeeded or JobStatus.Failed or JobStatus.Canceled;
    }

    /// <summary>
    /// Gets a human-readable progress message for a job
    /// </summary>
    private string GetProgressMessage(Job job)
    {
        if (!string.IsNullOrEmpty(job.ErrorMessage) && job.Status == JobStatus.Failed)
        {
            return job.ErrorMessage;
        }

        return job.Status switch
        {
            JobStatus.Queued => "Job is queued for execution",
            JobStatus.Running => $"Processing: {job.Stage}",
            JobStatus.Done => "Job completed successfully",
            JobStatus.Failed => "Job failed",
            JobStatus.Canceled => "Job was canceled",
            _ => $"Status: {job.Status}"
        };
    }

    /// <summary>
    /// Creates detailed failure information from an exception
    /// </summary>
    private JobFailure CreateFailureDetails(Job job, Exception ex)
    {
        var failureDetails = new JobFailure
        {
            Stage = job.Stage,
            Message = GetFriendlyErrorMessage(ex),
            CorrelationId = job.CorrelationId ?? string.Empty,
            FailedAt = DateTime.UtcNow
        };

        // Check if this is an FFmpeg-related error
        if (ex.Message.Contains("FFmpeg") || ex.Message.Contains("render failed"))
        {
            failureDetails = failureDetails with
            {
                ErrorCode = "E304-FFMPEG_RUNTIME",
                StderrSnippet = TryReadFfmpegLog(job.Id),
                LogPath = GetFfmpegLogPath(job.Id),
                SuggestedActions = new[]
                {
                    "Try using software encoder (x264) instead of hardware acceleration",
                    "Verify FFmpeg is properly installed using Dependencies page",
                    "Check the full log for detailed error information",
                    "Retry the render with different settings"
                }
            };
        }
        else
        {
            failureDetails = failureDetails with
            {
                SuggestedActions = new[]
                {
                    "Check the logs for more details",
                    "Verify all dependencies are installed",
                    "Retry the operation",
                    "Contact support if the issue persists"
                }
            };
        }

        return failureDetails;
    }

    /// <summary>
    /// Attempts to read the last 16KB of FFmpeg log for a job
    /// </summary>
    private string? TryReadFfmpegLog(string jobId)
    {
        try
        {
            var logPath = GetFfmpegLogPath(jobId);
            if (!File.Exists(logPath))
            {
                return null;
            }

            // Read last 16KB of the log file
            var fileInfo = new FileInfo(logPath);
            var maxBytes = 16 * 1024; // 16KB

            using var fileStream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            if (fileInfo.Length <= maxBytes)
            {
                // Read entire file if smaller than max
                using var reader = new StreamReader(fileStream);
                return reader.ReadToEnd();
            }
            else
            {
                // Read last 16KB
                fileStream.Seek(-maxBytes, SeekOrigin.End);
                using var reader = new StreamReader(fileStream);
                var snippet = reader.ReadToEnd();
                return $"... (showing last 16KB)\n{snippet}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read FFmpeg log for job {JobId}", jobId);
            return null;
        }
    }

    /// <summary>
    /// Gets the path to the FFmpeg log file for a job
    /// </summary>
    private string GetFfmpegLogPath(string jobId)
    {
        var logsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Aura", "Logs", "ffmpeg");
        return Path.Combine(logsDir, $"{jobId}.log");
    }

    /// <summary>
    /// Extracts a friendly error message from an exception
    /// </summary>
    private string GetFriendlyErrorMessage(Exception ex)
    {
        // Extract the first line of the exception message for a cleaner display
        var message = ex.Message.Split('\n')[0];

        // Try to extract a user-friendly portion
        if (message.Contains("FFmpeg render failed"))
        {
            return "Render failed due to FFmpeg error";
        }

        return message.Length > 200 ? string.Concat(message.AsSpan(0, 197), "...") : message;
    }

    /// <summary>
    /// Parses a progress message to extract stage, progress percentage, and formatted message
    /// </summary>
    private (string stage, int percent, string message) ParseProgressMessage(string message, string currentStage, int currentPercent)
    {
        var stage = currentStage;
        var percent = currentPercent;
        var formattedMessage = message;

        // Parse stage transitions from orchestrator messages with more granular progress
        if (message.Contains("Validating system", StringComparison.OrdinalIgnoreCase))
        {
            stage = "Initialization";
            percent = 2;
            formattedMessage = "Validating system readiness";
        }
        else if (message.Contains("Starting orchestration", StringComparison.OrdinalIgnoreCase))
        {
            stage = "Initialization";
            percent = 3;
            formattedMessage = "Starting video generation orchestration";
        }
        else if (message.Contains("Analyzing dependencies", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("building task graph", StringComparison.OrdinalIgnoreCase))
        {
            stage = "Initialization";
            percent = 4;
            formattedMessage = "Analyzing dependencies and building task graph";
        }
        else if (message.Contains("Task graph ready", StringComparison.OrdinalIgnoreCase))
        {
            stage = "Initialization";
            percent = 5;
            formattedMessage = "Task graph ready, starting execution";
        }
        else if (message.Contains("Script generation", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("Executing: Script", StringComparison.OrdinalIgnoreCase))
        {
            stage = "Script";
            percent = 10;
            formattedMessage = "Generating video script with AI";
        }
        else if (message.Contains("Completed: Script", StringComparison.OrdinalIgnoreCase))
        {
            stage = "Script";
            percent = 20;
            formattedMessage = "Script generation complete";
        }
        else if (message.Contains("Stage 1/5", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("Generating script", StringComparison.OrdinalIgnoreCase))
        {
            stage = "Script";
            percent = 15;
            formattedMessage = "Generating video script with AI";
        }
        else if (message.Contains("Stage 2/5", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("Parsing scenes", StringComparison.OrdinalIgnoreCase))
        {
            stage = "Script";
            percent = 25;
            formattedMessage = "Parsing script into scenes";
        }
        else if (message.Contains("Audio generation", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("Executing: Audio", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("TTS", StringComparison.OrdinalIgnoreCase))
        {
            stage = "Voice";
            percent = 30;
            formattedMessage = "Generating voice narration (TTS)";
        }
        else if (message.Contains("Completed: Audio", StringComparison.OrdinalIgnoreCase))
        {
            stage = "Voice";
            percent = 45;
            formattedMessage = "Voice narration complete";
        }
        else if (message.Contains("Stage 3/5", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("Generating narration", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("voice", StringComparison.OrdinalIgnoreCase))
        {
            stage = "Voice";
            percent = 40;
            formattedMessage = "Generating voice narration";
        }
        else if (message.Contains("Image generation", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("Executing: Image", StringComparison.OrdinalIgnoreCase))
        {
            stage = "Visuals";
            percent = 50;
            formattedMessage = "Generating visual assets";
        }
        else if (message.Contains("Completed: Image", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("Visual generation complete", StringComparison.OrdinalIgnoreCase))
        {
            // Image generation complete should be at 80% (end of Images stage: 55% + 25% = 80%)
            // Immediately transition to Rendering stage
            stage = "Rendering";
            percent = 80;
            formattedMessage = "Visual assets complete, starting video rendering";
        }
        // Removed - Stage 4/5 is now handled above as transition to Rendering
        else if (message.Contains("visual", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("image", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("asset", StringComparison.OrdinalIgnoreCase))
        {
            stage = "Visuals";
            percent = 65;
            formattedMessage = "Generating visual assets";
        }
        else if (message.Contains("Starting video composition", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("Video composition", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("Executing: Video composition", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("Building timeline", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("Stage 4/5", StringComparison.OrdinalIgnoreCase))
        {
            // Transition from Images to Rendering - this happens after image generation
            stage = "Rendering";
            percent = 80;
            formattedMessage = "Preparing video composition";
        }
        else if (message.Contains("Timeline created", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("preparing FFmpeg", StringComparison.OrdinalIgnoreCase))
        {
            // Timeline has been created, about to start FFmpeg
            stage = "Rendering";
            percent = 81;
            formattedMessage = "Timeline ready, preparing FFmpeg render";
        }
        else if (message.Contains("Executing FFmpeg", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("Initializing video render", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("Starting video encoding", StringComparison.OrdinalIgnoreCase))
        {
            // FFmpeg is about to start
            stage = "Rendering";
            percent = 82;
            formattedMessage = "FFmpeg render starting...";
        }
        else if (message.Contains("Validating FFmpeg", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("Validating audio", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("Building FFmpeg command", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("Locating FFmpeg", StringComparison.OrdinalIgnoreCase))
        {
            // FFmpeg validation/preparation steps
            stage = "Rendering";
            percent = 81;
            formattedMessage = message;
        }
        else if (message.Contains("Completed: Video composition", StringComparison.OrdinalIgnoreCase))
        {
            stage = "Rendering";
            percent = 95;
            formattedMessage = "Video composition complete";
        }
        else if (message.Contains("Stage 5/5", StringComparison.OrdinalIgnoreCase) ||
                 (message.Contains("render", StringComparison.OrdinalIgnoreCase) &&
                  !message.Contains("Rendering:") &&
                  !message.Contains("composing")))
        {
            stage = "Rendering";
            percent = 80;
            formattedMessage = "Starting video rendering";
        }
        else if (message.Contains("Rendering:", StringComparison.OrdinalIgnoreCase))
        {
            // Extract percentage from render progress if available
            stage = "Rendering";
            // Try to parse "Rendering: X%" from message using compiled regex
            var percentMatch = PercentageRegex().Match(message);
            if (percentMatch.Success && double.TryParse(percentMatch.Groups[1].Value, out double renderPercent))
            {
                // Map render progress (0-100%) to overall progress (80-95%)
                percent = 80 + (int)(renderPercent * 0.15);
            }
            else
            {
                percent = 85;
            }
            formattedMessage = message;
        }
        else if (message.Contains("Render complete", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("Rendering complete", StringComparison.OrdinalIgnoreCase))
        {
            // FFmpeg render has finished - transition to Complete stage
            stage = "Complete";
            percent = 100;
            formattedMessage = "Video rendering complete";
        }
        else if (message.Contains("Processing batch", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("Batch completed", StringComparison.OrdinalIgnoreCase))
        {
            // Extract batch progress from message using compiled regex
            var match = TaskProgressRegex().Match(message);
            if (match.Success &&
                int.TryParse(match.Groups[1].Value, out int completed) &&
                int.TryParse(match.Groups[2].Value, out int total) &&
                total > 0)
            {
                // Map task progress (0/N to N/N) to overall progress (5-95%)
                percent = 5 + (int)((double)completed / total * 90);

                // When all batch tasks are complete, transition to PostProcess stage
                if (completed == total)
                {
                    stage = "PostProcess";
                    percent = 95;
                    formattedMessage = "All tasks completed, finalizing";
                }
                else
                {
                    // Important: If batch tasks are NOT complete, ensure we're NOT in PostProcess stage.
                    // This fixes the bug where stage prematurely transitions to PostProcess.
                    if (stage == "PostProcess")
                    {
                        // Determine appropriate stage based on progress
                        // Note: This mapping mirrors VideoOrchestrator.DetermineStageFromProgress
                        // to maintain consistency. If changing thresholds, update both locations.
                        stage = percent switch
                        {
                            < 20 => "Script",
                            < 45 => "Voice",
                            < 70 => "Visuals",
                            _ => "Rendering"
                        };
                        _logger.LogDebug(
                            "[Progress] Correcting premature PostProcess stage to {Stage} (batch {Completed}/{Total})",
                            stage, completed, total);
                    }
                    formattedMessage = $"Processing: {completed}/{total} tasks completed";
                }
            }
            else
            {
                formattedMessage = message;
            }
        }
        else if (message.Contains("Orchestration completed", StringComparison.OrdinalIgnoreCase))
        {
            stage = "Complete";
            percent = 98;
            formattedMessage = "Orchestration completed, finalizing";
        }
        else if (message.Contains("complete", StringComparison.OrdinalIgnoreCase))
        {
            stage = "Complete";
            percent = 100;
            formattedMessage = "Video generation complete";
        }

        // Ensure progress never goes backwards
        if (percent < currentPercent)
        {
            percent = currentPercent;
        }

        return (stage, percent, formattedMessage);
    }

    /// <summary>
    /// Updates stuck task detection tracking when progress is made.
    /// Called by progress handlers to track when tasks make progress.
    /// </summary>
    private void UpdateProgressTracking(string jobId, int percent, string stage)
    {
        lock (_trackingLock)
        {
            if (!_jobProgressTracking.TryGetValue(jobId, out var tracking))
            {
                tracking = new JobProgressTracking(percent, stage, DateTime.UtcNow, false);
                _jobProgressTracking[jobId] = tracking;
                return;
            }

            // Only update if progress or stage has changed
            if (tracking.LastPercent != percent || tracking.LastStage != stage)
            {
                _jobProgressTracking[jobId] = tracking with
                {
                    LastPercent = percent,
                    LastStage = stage,
                    LastProgressTime = DateTime.UtcNow,
                    StuckWarningEmitted = false
                };
            }
        }
    }

    /// <summary>
    /// Checks if a job is stuck based on time since last progress.
    /// Returns true if stuck, along with the duration stuck.
    /// </summary>
    private (bool IsStuck, TimeSpan StuckDuration) CheckIfJobIsStuck(string jobId, int thresholdSeconds = DefaultStuckThresholdSeconds)
    {
        lock (_trackingLock)
        {
            if (!_jobProgressTracking.TryGetValue(jobId, out var tracking))
            {
                return (false, TimeSpan.Zero);
            }

            var stuckDuration = DateTime.UtcNow - tracking.LastProgressTime;
            var isStuck = stuckDuration.TotalSeconds >= thresholdSeconds;

            return (isStuck, stuckDuration);
        }
    }

    /// <summary>
    /// Marks that a stuck warning has been emitted for a job to avoid duplicate warnings.
    /// </summary>
    private void MarkStuckWarningEmitted(string jobId)
    {
        lock (_trackingLock)
        {
            if (_jobProgressTracking.TryGetValue(jobId, out var tracking))
            {
                _jobProgressTracking[jobId] = tracking with { StuckWarningEmitted = true };
            }
        }
    }

    /// <summary>
    /// Cleans up progress tracking for a completed job.
    /// </summary>
    private void CleanupProgressTracking(string jobId)
    {
        lock (_trackingLock)
        {
            _jobProgressTracking.Remove(jobId);
        }
    }
}

/// <summary>
/// Tracks progress for stuck task detection.
/// </summary>
internal sealed record JobProgressTracking(
    int LastPercent,
    string? LastStage,
    DateTime LastProgressTime,
    bool StuckWarningEmitted);
