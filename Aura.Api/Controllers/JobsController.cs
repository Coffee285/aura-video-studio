using Aura.Core.Artifacts;
using Aura.Core.Models;
using Aura.Core.Orchestrator;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System.Text;
using System.Text.Json;

namespace Aura.Api.Controllers;

/// <summary>
/// Controller for managing video generation jobs
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class JobsController : ControllerBase
{
    private readonly JobRunner _jobRunner;
    private readonly ArtifactManager _artifactManager;

    public JobsController(JobRunner jobRunner, ArtifactManager artifactManager)
    {
        _jobRunner = jobRunner;
        _artifactManager = artifactManager;
    }

    /// <summary>
    /// Create and start a new video generation job
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateJob(
        [FromBody] CreateJobRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var correlationId = HttpContext.TraceIdentifier;
            Log.Information("[{CorrelationId}] Creating new job for topic: {Topic}", correlationId, request.Brief.Topic);

            var brief = new Brief(
                Topic: request.Brief.Topic,
                Audience: request.Brief.Audience,
                Goal: request.Brief.Goal,
                Tone: request.Brief.Tone,
                Language: request.Brief.Language,
                Aspect: request.Brief.Aspect
            );

            var planSpec = new PlanSpec(
                TargetDuration: request.PlanSpec.TargetDuration,
                Pacing: request.PlanSpec.Pacing,
                Density: request.PlanSpec.Density,
                Style: request.PlanSpec.Style
            );

            var voiceSpec = new VoiceSpec(
                VoiceName: request.VoiceSpec.VoiceName,
                Rate: request.VoiceSpec.Rate,
                Pitch: request.VoiceSpec.Pitch,
                Pause: request.VoiceSpec.Pause
            );

            var renderSpec = new RenderSpec(
                Res: request.RenderSpec.Res,
                Container: request.RenderSpec.Container,
                VideoBitrateK: request.RenderSpec.VideoBitrateK,
                AudioBitrateK: request.RenderSpec.AudioBitrateK,
                Fps: request.RenderSpec.Fps,
                Codec: request.RenderSpec.Codec,
                QualityLevel: request.RenderSpec.QualityLevel,
                EnableSceneCut: request.RenderSpec.EnableSceneCut
            );

            var job = await _jobRunner.CreateAndStartJobAsync(
                brief, 
                planSpec, 
                voiceSpec, 
                renderSpec,
                correlationId,
                ct
            );

            return Ok(new { jobId = job.Id, status = job.Status, stage = job.Stage, correlationId });
        }
        catch (Exception ex)
        {
            var correlationId = HttpContext.TraceIdentifier;
            Log.Error(ex, "[{CorrelationId}] Error creating job", correlationId);
            
            // Return structured ProblemDetails with correlation ID
            return StatusCode(500, new
            {
                type = "https://docs.aura.studio/errors/E203",
                title = "Job Creation Failed",
                status = 500,
                detail = $"Failed to create job: {ex.Message}",
                correlationId,
                traceId = HttpContext.TraceIdentifier
            });
        }
    }

    /// <summary>
    /// Get job status and progress
    /// </summary>
    [HttpGet("{jobId}")]
    public IActionResult GetJob(string jobId)
    {
        try
        {
            var correlationId = HttpContext.TraceIdentifier;
            
            var job = _jobRunner.GetJob(jobId);
            if (job == null)
            {
                Log.Warning("[{CorrelationId}] Job not found: {JobId}", correlationId, jobId);
                return NotFound(new 
                { 
                    type = "https://docs.aura.studio/errors/E404",
                    title = "Job Not Found", 
                    status = 404,
                    detail = $"Job {jobId} not found",
                    correlationId
                });
            }

            return Ok(job);
        }
        catch (Exception ex)
        {
            var correlationId = HttpContext.TraceIdentifier;
            Log.Error(ex, "[{CorrelationId}] Error retrieving job {JobId}", correlationId, jobId);
            
            return StatusCode(500, new
            {
                type = "https://docs.aura.studio/errors/E500",
                title = "Error Retrieving Job",
                status = 500,
                detail = $"Failed to retrieve job: {ex.Message}",
                correlationId
            });
        }
    }

    /// <summary>
    /// List all recent jobs
    /// </summary>
    [HttpGet]
    public IActionResult ListJobs([FromQuery] int limit = 50)
    {
        try
        {
            var correlationId = HttpContext.TraceIdentifier;
            
            var jobs = _jobRunner.ListJobs(limit);
            return Ok(new { jobs = jobs, count = jobs.Count, correlationId });
        }
        catch (Exception ex)
        {
            var correlationId = HttpContext.TraceIdentifier;
            Log.Error(ex, "[{CorrelationId}] Error listing jobs", correlationId);
            
            return StatusCode(500, new
            {
                type = "https://docs.aura.studio/errors/E500",
                title = "Error Listing Jobs",
                status = 500,
                detail = $"Failed to list jobs: {ex.Message}",
                correlationId
            });
        }
    }

    /// <summary>
    /// Get detailed failure information for a failed job
    /// </summary>
    [HttpGet("{jobId}/failure-details")]
    public IActionResult GetJobFailureDetails(string jobId)
    {
        try
        {
            var correlationId = HttpContext.TraceIdentifier;
            
            var job = _jobRunner.GetJob(jobId);
            if (job == null)
            {
                Log.Warning("[{CorrelationId}] Job not found: {JobId}", correlationId, jobId);
                return NotFound(new 
                { 
                    type = "https://docs.aura.studio/errors/E404",
                    title = "Job Not Found", 
                    status = 404,
                    detail = $"Job {jobId} not found",
                    correlationId
                });
            }

            if (job.Status != JobStatus.Failed)
            {
                return BadRequest(new
                {
                    type = "https://docs.aura.studio/errors/E400",
                    title = "Job Not Failed",
                    status = 400,
                    detail = $"Job {jobId} has not failed (status: {job.Status})",
                    correlationId
                });
            }

            if (job.FailureDetails == null)
            {
                // Return basic failure info if detailed info not available
                return Ok(new
                {
                    stage = job.Stage,
                    message = job.ErrorMessage ?? "Job failed",
                    correlationId = job.CorrelationId ?? correlationId,
                    suggestedActions = new[] { "Check logs for details", "Retry the operation" },
                    failedAt = job.FinishedAt ?? DateTime.UtcNow
                });
            }

            return Ok(job.FailureDetails);
        }
        catch (Exception ex)
        {
            var correlationId = HttpContext.TraceIdentifier;
            Log.Error(ex, "[{CorrelationId}] Error retrieving failure details for job {JobId}", correlationId, jobId);
            
            return StatusCode(500, new
            {
                type = "https://docs.aura.studio/errors/E500",
                title = "Error Retrieving Failure Details",
                status = 500,
                detail = $"Failed to retrieve failure details: {ex.Message}",
                correlationId
            });
        }
    }

    /// <summary>
    /// Get latest artifacts from recent jobs
    /// Does NOT attempt to resolve providers - simply returns persisted artifacts
    /// </summary>
    [HttpGet("recent-artifacts")]
    public IActionResult GetRecentArtifacts([FromQuery] int limit = 5)
    {
        try
        {
            var correlationId = HttpContext.TraceIdentifier;
            
            // Get jobs from storage - does not trigger provider resolution
            var jobs = _jobRunner.ListJobs(50); // Get more jobs to find ones with artifacts
            var artifacts = jobs
                .Where(j => j.Status == JobStatus.Done && j.Artifacts.Count > 0)
                .OrderByDescending(j => j.FinishedAt ?? j.StartedAt)
                .Take(limit)
                .Select(j => new
                {
                    jobId = j.Id,
                    correlationId = j.CorrelationId,
                    stage = j.Stage,
                    finishedAt = j.FinishedAt,
                    artifacts = j.Artifacts
                })
                .ToList();

            return Ok(new { artifacts = artifacts, count = artifacts.Count, correlationId });
        }
        catch (Exception ex)
        {
            var correlationId = HttpContext.TraceIdentifier;
            Log.Error(ex, "[{CorrelationId}] Error getting recent artifacts", correlationId);
            
            // Return best-effort empty list rather than throwing
            // This ensures the endpoint never crashes the API
            return Ok(new
            {
                artifacts = new List<object>(),
                count = 0,
                correlationId,
                warning = "Failed to retrieve artifacts, returning empty list",
                detail = ex.Message
            });
        }
    }
    
    /// <summary>
    /// Retry a failed job with specific remediation strategy
    /// </summary>
    [HttpPost("{jobId}/retry")]
    public async Task<IActionResult> RetryJob(
        string jobId,
        [FromQuery] string? strategy = null,
        CancellationToken ct = default)
    {
        try
        {
            var correlationId = HttpContext.TraceIdentifier;
            Log.Information("[{CorrelationId}] Retry request for job {JobId} with strategy {Strategy}", 
                correlationId, jobId, strategy ?? "default");
            
            // Get the job
            var job = _jobRunner.GetJob(jobId);
            if (job == null)
            {
                return NotFound(new
                {
                    type = "https://docs.aura.studio/errors/E404",
                    title = "Job Not Found",
                    status = 404,
                    detail = $"Job {jobId} not found",
                    correlationId
                });
            }
            
            // For now, return guidance on retry strategies
            // Full retry implementation would require job state management
            return Ok(new
            {
                jobId,
                currentStatus = job.Status,
                currentStage = job.Stage,
                strategy = strategy ?? "manual",
                message = "Job retry not yet fully implemented. Please create a new job with adjusted settings.",
                suggestedActions = new[]
                {
                    "Re-generate with different TTS provider if narration failed",
                    "Use software encoder (x264) if hardware encoding failed",
                    "Check FFmpeg installation if render failed",
                    "Verify input files are valid if validation failed"
                },
                correlationId
            });
        }
        catch (Exception ex)
        {
            var correlationId = HttpContext.TraceIdentifier;
            Log.Error(ex, "[{CorrelationId}] Error retrying job {JobId}", correlationId, jobId);
            
            return StatusCode(500, new
            {
                type = "https://docs.aura.studio/errors/E500",
                title = "Retry Failed",
                status = 500,
                detail = $"Failed to retry job: {ex.Message}",
                correlationId
            });
        }
    }
    
    /// <summary>
    /// Stream Server-Sent Events for job progress updates
    /// </summary>
    [HttpGet("{jobId}/events")]
    public async Task GetJobEvents(string jobId, CancellationToken ct = default)
    {
        var correlationId = HttpContext.TraceIdentifier;
        Log.Information("[{CorrelationId}] SSE stream requested for job {JobId}", correlationId, jobId);
        
        // Set headers for SSE
        Response.Headers.Add("Content-Type", "text/event-stream");
        Response.Headers.Add("Cache-Control", "no-cache");
        Response.Headers.Add("Connection", "keep-alive");
        Response.Headers.Add("X-Accel-Buffering", "no"); // Disable nginx buffering
        
        try
        {
            var job = _jobRunner.GetJob(jobId);
            if (job == null)
            {
                await SendSseEvent("error", new { message = "Job not found", jobId, correlationId });
                return;
            }
            
            // Send initial job status
            await SendSseEvent("job-status", new { status = job.Status.ToString(), correlationId });
            
            // Poll for updates and stream them
            var lastStatus = job.Status;
            var lastStage = job.Stage;
            var lastPercent = job.Percent;
            
            while (!ct.IsCancellationRequested && job.Status != JobStatus.Done && job.Status != JobStatus.Failed && job.Status != JobStatus.Canceled)
            {
                await Task.Delay(1000, ct); // Poll every second
                
                job = _jobRunner.GetJob(jobId);
                if (job == null) break;
                
                // Send status change events
                if (job.Status != lastStatus)
                {
                    await SendSseEvent("job-status", new { status = job.Status.ToString(), correlationId });
                    lastStatus = job.Status;
                }
                
                // Send stage change events
                if (job.Stage != lastStage)
                {
                    await SendSseEvent("step-status", new { step = job.Stage, status = "Running", correlationId });
                    lastStage = job.Stage;
                }
                
                // Send progress updates
                if (job.Percent != lastPercent)
                {
                    await SendSseEvent("step-progress", new { step = job.Stage, progressPct = job.Percent, correlationId });
                    lastPercent = job.Percent;
                }
                
                await Response.Body.FlushAsync(ct);
            }
            
            // Send final event
            if (job?.Status == JobStatus.Done || job?.Status == JobStatus.Succeeded)
            {
                var outputPath = job.Artifacts.FirstOrDefault(a => a.Type == "video")?.Path ?? "";
                var sizeBytes = job.Artifacts.FirstOrDefault(a => a.Type == "video")?.SizeBytes ?? 0;
                
                await SendSseEvent("job-completed", new 
                { 
                    status = "Succeeded", 
                    output = new { videoPath = outputPath, sizeBytes },
                    correlationId 
                });
            }
            else if (job?.Status == JobStatus.Failed)
            {
                var errors = job.Errors.Any() 
                    ? job.Errors.ToArray() 
                    : new[] { new JobStepError { Code = "UnknownError", Message = job.ErrorMessage ?? "Job failed", Remediation = "Check logs for details" } };
                    
                await SendSseEvent("job-failed", new 
                { 
                    status = "Failed", 
                    errors,
                    correlationId 
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected, this is normal
            Log.Debug("[{CorrelationId}] SSE stream canceled for job {JobId}", correlationId, jobId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[{CorrelationId}] Error streaming events for job {JobId}", correlationId, jobId);
            await SendSseEvent("error", new { message = ex.Message, correlationId });
        }
    }
    
    /// <summary>
    /// Cancel a running job
    /// </summary>
    [HttpPost("{jobId}/cancel")]
    public IActionResult CancelJob(string jobId)
    {
        try
        {
            var correlationId = HttpContext.TraceIdentifier;
            Log.Information("[{CorrelationId}] Cancel request for job {JobId}", correlationId, jobId);
            
            var job = _jobRunner.GetJob(jobId);
            if (job == null)
            {
                return NotFound(new
                {
                    type = "https://docs.aura.studio/errors/E404",
                    title = "Job Not Found",
                    status = 404,
                    detail = $"Job {jobId} not found",
                    correlationId
                });
            }

            // Check if job is in a cancellable state
            if (job.Status != JobStatus.Running && job.Status != JobStatus.Queued)
            {
                return BadRequest(new
                {
                    type = "https://docs.aura.studio/errors/E400",
                    title = "Job Not Cancellable",
                    status = 400,
                    detail = $"Job is in {job.Status} status and cannot be cancelled",
                    currentStatus = job.Status,
                    correlationId
                });
            }
            
            // Attempt to cancel the job
            bool cancelled = _jobRunner.CancelJob(jobId);
            
            if (cancelled)
            {
                Log.Information("[{CorrelationId}] Successfully cancelled job {JobId}", correlationId, jobId);
                return Accepted(new
                {
                    jobId,
                    message = "Job cancellation triggered successfully",
                    currentStatus = job.Status,
                    correlationId
                });
            }
            else
            {
                Log.Warning("[{CorrelationId}] Failed to cancel job {JobId}", correlationId, jobId);
                return StatusCode(500, new
                {
                    type = "https://docs.aura.studio/errors/E500",
                    title = "Cancellation Failed",
                    status = 500,
                    detail = "Job could not be cancelled. It may have already completed or been cancelled.",
                    currentStatus = job.Status,
                    correlationId
                });
            }
        }
        catch (Exception ex)
        {
            var correlationId = HttpContext.TraceIdentifier;
            Log.Error(ex, "[{CorrelationId}] Error canceling job {JobId}", correlationId, jobId);
            
            return StatusCode(500, new
            {
                type = "https://docs.aura.studio/errors/E500",
                title = "Cancel Failed",
                status = 500,
                detail = $"Failed to cancel job: {ex.Message}",
                correlationId
            });
        }
    }
    
    private async Task SendSseEvent(string eventType, object data)
    {
        var json = JsonSerializer.Serialize(data);
        var message = $"event: {eventType}\ndata: {json}\n\n";
        var bytes = Encoding.UTF8.GetBytes(message);
        await Response.Body.WriteAsync(bytes);
    }

    /// <summary>
    /// Get job progress information for status bar updates
    /// </summary>
    [HttpGet("{jobId}/progress")]
    public IActionResult GetJobProgress(string jobId)
    {
        try
        {
            var correlationId = HttpContext.TraceIdentifier;
            
            var job = _jobRunner.GetJob(jobId);
            if (job == null)
            {
                Log.Warning("[{CorrelationId}] Job not found: {JobId}", correlationId, jobId);
                return NotFound(new 
                { 
                    type = "https://docs.aura.studio/errors/E404",
                    title = "Job Not Found", 
                    status = 404,
                    detail = $"Job {jobId} not found",
                    correlationId
                });
            }

            // Map job status to string for UI
            var statusString = job.Status switch
            {
                JobStatus.Running => "running",
                JobStatus.Done or JobStatus.Succeeded => "completed",
                JobStatus.Failed => "failed",
                _ => "idle"
            };

            return Ok(new
            {
                jobId = job.Id,
                status = statusString,
                progress = job.Percent,
                currentStage = job.Stage,
                startedAt = job.StartedAt,
                completedAt = job.FinishedAt,
                correlationId
            });
        }
        catch (Exception ex)
        {
            var correlationId = HttpContext.TraceIdentifier;
            Log.Error(ex, "[{CorrelationId}] Error retrieving job progress {JobId}", correlationId, jobId);
            
            return StatusCode(500, new
            {
                type = "https://docs.aura.studio/errors/E500",
                title = "Error Retrieving Job Progress",
                status = 500,
                detail = $"Failed to retrieve job progress: {ex.Message}",
                correlationId
            });
        }
    }
}

/// <summary>
/// Request model for creating a new job
/// </summary>
public record CreateJobRequest(
    Brief Brief,
    PlanSpec PlanSpec,
    VoiceSpec VoiceSpec,
    RenderSpec RenderSpec
);
