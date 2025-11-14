using System;
using System.Threading;
using System.Threading.Tasks;
using Aura.Core.Services.FFmpeg;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aura.Api.Controllers;

/// <summary>
/// System health and status information
/// </summary>
[ApiController]
[Route("api/system")]
public class SystemController : ControllerBase
{
    private readonly ILogger<SystemController> _logger;
    private readonly IFFmpegStatusService _ffmpegStatusService;
    private readonly IHostApplicationLifetime _lifetime;

    public SystemController(
        ILogger<SystemController> logger,
        IFFmpegStatusService ffmpegStatusService,
        IHostApplicationLifetime lifetime)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _ffmpegStatusService = ffmpegStatusService ?? throw new ArgumentNullException(nameof(ffmpegStatusService));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
    }

    /// <summary>
    /// Get comprehensive FFmpeg status including version and hardware acceleration support
    /// </summary>
    /// <remarks>
    /// Returns detailed information about FFmpeg installation:
    /// - Installation status and location
    /// - Version information and requirement compliance
    /// - Hardware acceleration support (NVENC, AMF, QuickSync, VideoToolbox)
    /// - Available hardware encoders
    /// </remarks>
    [HttpGet("ffmpeg/status")]
    public async Task<IActionResult> GetFFmpegStatus(CancellationToken ct)
    {
        var correlationId = HttpContext.TraceIdentifier;

        try
        {
            _logger.LogInformation("[{CorrelationId}] GET /api/system/ffmpeg/status", correlationId);

            var status = await _ffmpegStatusService.GetStatusAsync(ct);

            return Ok(new
            {
                installed = status.Installed,
                valid = status.Valid,
                version = status.Version,
                path = status.Path,
                source = status.Source,
                error = status.Error,
                versionMeetsRequirement = status.VersionMeetsRequirement,
                minimumVersion = status.MinimumVersion,
                hardwareAcceleration = new
                {
                    nvencSupported = status.HardwareAcceleration.NvencSupported,
                    amfSupported = status.HardwareAcceleration.AmfSupported,
                    quickSyncSupported = status.HardwareAcceleration.QuickSyncSupported,
                    videoToolboxSupported = status.HardwareAcceleration.VideoToolboxSupported,
                    availableEncoders = status.HardwareAcceleration.AvailableEncoders
                },
                correlationId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{CorrelationId}] Error getting FFmpeg status", correlationId);

            return StatusCode(500, new
            {
                type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E500",
                title = "FFmpeg Status Error",
                status = 500,
                detail = $"Failed to get FFmpeg status: {ex.Message}",
                correlationId
            });
        }
    }

    /// <summary>
    /// Gracefully shutdown the backend API service
    /// </summary>
    /// <remarks>
    /// Initiates a graceful shutdown of the backend API service.
    /// This endpoint is called by the Electron main process during application shutdown.
    /// The service will:
    /// - Stop accepting new requests
    /// - Complete in-flight requests (with timeout)
    /// - Release resources and close connections
    /// - Exit the process
    /// </remarks>
    [HttpPost("shutdown")]
    public IActionResult Shutdown()
    {
        var correlationId = HttpContext.TraceIdentifier;

        try
        {
            _logger.LogInformation("[{CorrelationId}] POST /api/system/shutdown - Graceful shutdown requested", correlationId);

            // Trigger graceful shutdown asynchronously to allow response to be sent
            Task.Run(async () =>
            {
                try
                {
                    _logger.LogInformation("Initiating graceful shutdown in 500ms...");
                    
                    // Small delay to allow response to be sent back to client
                    await Task.Delay(500);
                    
                    _logger.LogInformation("Stopping application...");
                    _lifetime.StopApplication();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during shutdown sequence");
                }
            });

            return Ok(new
            {
                message = "Shutdown initiated",
                correlationId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{CorrelationId}] Error initiating shutdown", correlationId);

            return StatusCode(500, new
            {
                type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E500",
                title = "Shutdown Error",
                status = 500,
                detail = $"Failed to initiate shutdown: {ex.Message}",
                correlationId
            });
        }
    }
}
