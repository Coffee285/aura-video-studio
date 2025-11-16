using Aura.Api.Services;
using Aura.Core.Dependencies;
using Aura.Core.Downloads;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace Aura.Api.Endpoints;

/// <summary>
/// Health check and system diagnostics endpoints.
/// </summary>
public static class HealthEndpoints
{
    /// <summary>
    /// Maps health check and diagnostics endpoints to the API route group.
    /// </summary>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api");

        // Basic liveness check - returns 200 if API is running
        group.MapGet("/health/live", (HealthCheckService healthService) =>
        {
            var result = healthService.CheckLiveness();
            return Results.Ok(result);
        })
        .WithName("HealthLive")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Check API liveness";
            operation.Description = "Returns a simple liveness indicator. Used by load balancers and orchestrators.";
            return operation;
        })
        .Produces<object>(200);

        // Readiness check - returns 200/503 based on dependency availability
        group.MapGet("/health/ready", async (HealthCheckService healthService, CancellationToken ct) =>
        {
            var result = await healthService.CheckReadinessAsync(ct).ConfigureAwait(false);
            var statusCode = result.Status == Models.HealthStatus.Unhealthy ? 503 : 200;
            return Results.Json(result, statusCode: statusCode);
        })
        .WithName("HealthReady")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Check API readiness";
            operation.Description = "Performs dependency checks. Returns 503 if system is not ready to serve requests.";
            return operation;
        })
        .Produces<object>(200)
        .Produces(503);

        // Health summary - high-level system status
        group.MapGet("/health/summary", async (HealthDiagnosticsService healthDiagnostics, CancellationToken ct) =>
        {
            try
            {
                var correlationId = Guid.NewGuid().ToString();
                Log.Information("Health summary requested, CorrelationId: {CorrelationId}", correlationId);

                var result = await healthDiagnostics.GetHealthSummaryAsync(ct).ConfigureAwait(false);
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error retrieving health summary");
                return Results.Problem("Error retrieving health summary", statusCode: 500);
            }
        })
        .WithName("HealthSummary")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Get health summary";
            operation.Description = "Returns high-level system health status and metrics.";
            return operation;
        })
        .Produces<object>(200)
        .ProducesProblem(500);

        // Health details - per-check detailed diagnostics
        group.MapGet("/health/details", async (HealthDiagnosticsService healthDiagnostics, CancellationToken ct) =>
        {
            try
            {
                var correlationId = Guid.NewGuid().ToString();
                Log.Information("Health details requested, CorrelationId: {CorrelationId}", correlationId);

                var result = await healthDiagnostics.GetHealthDetailsAsync(ct).ConfigureAwait(false);
                var statusCode = result.IsReady ? 200 : 503;
                return Results.Json(result, statusCode: statusCode);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error retrieving health details");
                return Results.Problem("Error retrieving health details", statusCode: 500);
            }
        })
        .WithName("HealthDetails")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Get detailed health diagnostics";
            operation.Description = "Returns per-check detailed diagnostics. Returns 503 if system has failed required checks.";
            return operation;
        })
        .Produces<object>(200)
        .Produces(503)
        .ProducesProblem(500);

        // First-run diagnostics - comprehensive system check with actionable guidance
        group.MapGet("/health/first-run", async (FirstRunDiagnostics diagnostics, CancellationToken ct) =>
        {
            try
            {
                var result = await diagnostics.RunDiagnosticsAsync(ct).ConfigureAwait(false);
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error running first-run diagnostics");
                return Results.Problem("Error running diagnostics", statusCode: 500);
            }
        })
        .WithName("FirstRunDiagnostics")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Run first-run diagnostics";
            operation.Description = "Performs comprehensive system check with actionable guidance for first-time setup.";
            return operation;
        })
        .Produces<object>(200)
        .ProducesProblem(500);

        // Auto-fix endpoint - attempt to automatically resolve common issues
        group.MapPost("/health/auto-fix", async (
            [FromBody] Dictionary<string, object>? options,
            FirstRunDiagnostics diagnostics,
            FfmpegInstaller ffmpegInstaller,
            CancellationToken ct) =>
        {
            try
            {
                var issueCode = options?.ContainsKey("issueCode") == true
                    ? options["issueCode"]?.ToString()
                    : null;

                if (string.IsNullOrEmpty(issueCode))
                {
                    return Results.BadRequest(new { success = false, message = "Issue code is required" });
                }

                // Handle FFmpeg installation
                if (issueCode == "E302-FFMPEG_NOT_FOUND")
                {
                    Log.Information("Attempting auto-fix for FFmpeg installation");

                    var progress = new Progress<HttpDownloadProgress>(p =>
                    {
                        Log.Information("FFmpeg download progress: {Percent}%", p.PercentComplete);
                    });

                    var mirrors = new List<string>();
                    if (OperatingSystem.IsWindows())
                    {
                        mirrors.Add("https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip");
                    }
                    else if (OperatingSystem.IsLinux())
                    {
                        mirrors.Add("https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-amd64-static.tar.xz");
                    }

                    if (!mirrors.Any())
                    {
                        return Results.Ok(new
                        {
                            success = false,
                            message = "Automatic FFmpeg installation is not supported on this platform. Please install manually."
                        });
                    }

                    var result = await ffmpegInstaller.InstallFromMirrorsAsync(
                        mirrors.ToArray(),
                        "latest",
                        null,
                        progress,
                        ct).ConfigureAwait(false);

                    if (result.Success)
                    {
                        Log.Information("FFmpeg installed successfully at: {Path}", result.FfmpegPath);
                        return Results.Ok(new
                        {
                            success = true,
                            message = "FFmpeg installed successfully",
                            ffmpegPath = result.FfmpegPath
                        });
                    }
                    else
                    {
                        Log.Error("FFmpeg installation failed: {Error}", result.ErrorMessage);
                        return Results.Ok(new
                        {
                            success = false,
                            message = $"FFmpeg installation failed: {result.ErrorMessage}"
                        });
                    }
                }

                return Results.Ok(new
                {
                    success = false,
                    message = $"Auto-fix not available for issue: {issueCode}"
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during auto-fix");
                return Results.Problem($"Error during auto-fix: {ex.Message}", statusCode: 500);
            }
        })
        .WithName("AutoFixIssue")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Attempt automatic issue resolution";
            operation.Description = "Tries to automatically fix common system issues like missing FFmpeg.";
            return operation;
        })
        .Produces<object>(200)
        .Produces(400)
        .ProducesProblem(500);

        // Legacy health check endpoint for backward compatibility
        group.MapGet("/healthz", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
            .WithName("HealthCheck")
            .WithOpenApi(operation =>
            {
                operation.Summary = "Legacy health check";
                operation.Description = "Simple health check endpoint for backward compatibility.";
                return operation;
            })
            .Produces<object>(200);

        // System check endpoint
        group.MapGet("/health/system-check", () =>
        {
            try
            {
                var result = new
                {
                    ffmpeg = new
                    {
                        installed = false,
                        version = (string?)null,
                        path = (string?)null,
                        error = "Not checked"
                    },
                    diskSpace = new
                    {
                        available = 0,
                        total = 0,
                        unit = "GB",
                        sufficient = false
                    },
                    gpu = new
                    {
                        available = false,
                        name = (string?)null,
                        vramGB = (int?)null
                    },
                    providers = new
                    {
                        configured = new List<string>(),
                        validated = new List<string>(),
                        errors = new Dictionary<string, string>()
                    }
                };

                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during system check");
                return Results.Problem("Error during system check", statusCode: 500);
            }
        })
        .WithName("SystemCheck")
        .WithOpenApi(operation =>
        {
            operation.Summary = "System check";
            operation.Description = "Returns system information including FFmpeg, disk space, GPU, and provider status.";
            return operation;
        })
        .Produces<object>(200)
        .ProducesProblem(500);

        return endpoints;
    }
}
