using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aura.Core.Dependencies;
using Aura.Core.Errors;
using Aura.Core.Models;
using Aura.Core.Models.Export;
using Aura.Core.Providers;
using Aura.Core.Runtime;
using Aura.Core.Services.FFmpeg;
using Aura.Core.Services.FFmpeg.Filters;
using Aura.Core.Services.Render;
using Microsoft.Extensions.Logging;

namespace Aura.Providers.Video;

public class FfmpegVideoComposer : IVideoComposer
{
    // Progress milestone constants for render initialization stages
    private const float ProgressInitializing = 0f;
    private const float ProgressLocatingFfmpeg = 1f;
    private const float ProgressValidatingFfmpeg = 2f;
    private const float ProgressValidatingAudio = 3f;
    private const float ProgressBuildingCommand = 4f;
    private const float ProgressStartingEncode = 5f;

    // Default Ken Burns effect settings - subtle zoom from 1.0 to 1.1 for professional look
    private const double DefaultKenBurnsZoomStart = 1.0;
    private const double DefaultKenBurnsZoomEnd = 1.1;

    // Default fade transition duration between scenes (in seconds)
    private const double DefaultFadeTransitionDuration = 0.5;
    
    // Minimum scene duration for safe crossfade transitions (in seconds)
    // Scenes shorter than this will use direct concatenation to prevent invalid FFmpeg filter parameters
    private const double MinSceneDurationForTransition = 0.6;
    
    // Minimum transition duration when scene is too short for default transition
    private const double MinTransitionDuration = 0.2;

    private readonly ILogger<FfmpegVideoComposer> _logger;
    private readonly IFfmpegLocator _ffmpegLocator;
    private readonly string? _configuredFfmpegPath;
    private readonly string _workingDirectory;
    private string _outputDirectory;
    private readonly string _logsDirectory;
    private readonly HardwareEncoder _hardwareEncoder;
    private readonly ProcessRegistry? _processRegistry;
    private readonly ManagedProcessRunner? _processRunner;

    public FfmpegVideoComposer(
        ILogger<FfmpegVideoComposer> logger,
        IFfmpegLocator ffmpegLocator,
        string? configuredFfmpegPath = null,
        string? outputDirectory = null,
        ProcessRegistry? processRegistry = null,
        ManagedProcessRunner? processRunner = null)
    {
        _logger = logger;
        _ffmpegLocator = ffmpegLocator;
        _configuredFfmpegPath = configuredFfmpegPath;
        _processRegistry = processRegistry;
        _processRunner = processRunner;
        _workingDirectory = Path.Combine(Path.GetTempPath(), "AuraVideoStudio", "Render");
        _outputDirectory = outputDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "AuraVideoStudio");
        _logsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Aura", "Logs", "ffmpeg");

        // Ensure working directory exists
        if (!Directory.Exists(_workingDirectory))
        {
            Directory.CreateDirectory(_workingDirectory);
        }

        // Ensure output directory exists
        if (!Directory.Exists(_outputDirectory))
        {
            Directory.CreateDirectory(_outputDirectory);
        }

        // Ensure logs directory exists
        if (!Directory.Exists(_logsDirectory))
        {
            Directory.CreateDirectory(_logsDirectory);
        }

        // Initialize placeholder - actual hardware encoder will be created at render time with proper path
        _hardwareEncoder = new HardwareEncoder(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<HardwareEncoder>.Instance,
            "ffmpeg");
    }

    public async Task<string> RenderAsync(Timeline timeline, RenderSpec spec, IProgress<RenderProgress> progress, CancellationToken ct)
    {
        var jobId = spec.JobId ?? Guid.NewGuid().ToString("N");
        var correlationId = System.Diagnostics.Activity.Current?.Id ?? jobId;

        _logger.LogInformation("Starting FFmpeg render (JobId={JobId}, CorrelationId={CorrelationId}) at {Resolution}p",
            jobId, correlationId, spec.Res.Height);

        // Report initial progress immediately to indicate render has started
        progress.Report(new RenderProgress(ProgressInitializing, TimeSpan.Zero, TimeSpan.Zero, "Initializing video render..."));

        // Set up FFmpeg log file path
        var ffmpegLogPath = Path.Combine(_logsDirectory, $"{jobId}.log");
        StreamWriter? logWriter = null;

        // Open log file early so both managed and manual runners capture output
        try
        {
            logWriter = new StreamWriter(ffmpegLogPath, append: false, encoding: Encoding.UTF8)
            {
                AutoFlush = true
            };
            logWriter.WriteLine("FFmpeg Render Log");
            logWriter.WriteLine($"Job ID: {jobId}");
            logWriter.WriteLine($"Correlation ID: {correlationId}");
            logWriter.WriteLine($"Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            logWriter.WriteLine(new string('-', 80));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create FFmpeg log file at {LogPath}", ffmpegLogPath);
        }

        try
        {
            // Resolve FFmpeg path once at the start - this is the single source of truth for this render job
        string ffmpegPath;
        try
        {
            progress.Report(new RenderProgress(ProgressLocatingFfmpeg, TimeSpan.Zero, TimeSpan.Zero, "Locating FFmpeg..."));
            ffmpegPath = await _ffmpegLocator.GetEffectiveFfmpegPathAsync(_configuredFfmpegPath, ct).ConfigureAwait(false);
            _logger.LogInformation("Resolved FFmpeg path for job {JobId}: {FfmpegPath}", jobId, ffmpegPath);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Failed to resolve FFmpeg path for job {JobId}", jobId);
            throw FfmpegException.NotFound(correlationId: correlationId);
        }

        // Validate FFmpeg binary before starting
        progress.Report(new RenderProgress(ProgressValidatingFfmpeg, TimeSpan.Zero, TimeSpan.Zero, "Validating FFmpeg..."));
        await ValidateFfmpegBinaryAsync(ffmpegPath, jobId, correlationId, ct).ConfigureAwait(false);

        // Pre-validate audio files - pass the resolved ffmpeg path
        progress.Report(new RenderProgress(ProgressValidatingAudio, TimeSpan.Zero, TimeSpan.Zero, "Validating audio files..."));
        await PreValidateAudioAsync(timeline, ffmpegPath, jobId, correlationId, ct).ConfigureAwait(false);

        // VALIDATE INPUT FILES FIRST - fail fast if files are bad
        await ValidateInputFilesAsync(timeline, ffmpegPath, jobId, correlationId, ct).ConfigureAwait(false);

        // Create output file path using configured output directory
        string outputFilePath = Path.Combine(
            _outputDirectory,
            $"AuraVideoStudio_{DateTime.Now:yyyyMMddHHmmss}.{spec.Container}");

        // Build the FFmpeg command with hardware acceleration support
        progress.Report(new RenderProgress(ProgressBuildingCommand, TimeSpan.Zero, TimeSpan.Zero, "Building FFmpeg command..."));
        string ffmpegCommand = await BuildFfmpegCommandAsync(timeline, spec, outputFilePath, ffmpegPath, ct).ConfigureAwait(false);

        _logger.LogInformation("FFmpeg command (JobId={JobId}): {FFmpegPath} {Command}",
            jobId, ffmpegPath, ffmpegCommand);

        // Report that we're about to start encoding
        progress.Report(new RenderProgress(ProgressStartingEncode, TimeSpan.Zero, TimeSpan.Zero, "Starting video encoding..."));

        // Track progress
        var totalDuration = timeline.Scenes.Count > 0
            ? timeline.Scenes[^1].Start + timeline.Scenes[^1].Duration
            : TimeSpan.FromMinutes(1);

        var startTime = DateTime.Now;
        var lastReportTime = DateTime.Now;

        // Use ManagedProcessRunner if available, otherwise fall back to manual process management
        if (_processRunner != null)
        {
            return await RenderWithManagedRunnerAsync(
                ffmpegPath,
                ffmpegCommand,
                ffmpegLogPath,
                logWriter,
                jobId,
                correlationId,
                outputFilePath,
                totalDuration,
                startTime,
                lastReportTime,
                progress,
                ct).ConfigureAwait(false);
        }

        // Fallback to manual process management (original implementation)
        var stderrBuilder = new StringBuilder();
        var stdoutBuilder = new StringBuilder();

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = ffmpegCommand,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                RedirectStandardInput = true  // Required for graceful termination via 'q' command.
            },
            EnableRaisingEvents = true
        };

        // Log run metadata (only if log writer is available)
        logWriter?.WriteLine($"Resolution: {spec.Res.Width}x{spec.Res.Height}");
        logWriter?.WriteLine($"FFmpeg Path: {ffmpegPath}");
        logWriter?.WriteLine($"Command: {ffmpegCommand}");
        logWriter?.WriteLine(new string('-', 80));

        // Track FFmpeg activity for watchdog timer
        DateTime lastFfmpegActivity = DateTime.UtcNow;
        float lastProgressPercent = 0f;
        string? lastStderrLine = null;

        // Watchdog timer to detect FFmpeg hanging (no output for 90 seconds)
        var watchdogTimer = new System.Timers.Timer(10000); // Check every 10 seconds
        watchdogTimer.Elapsed += (sender, args) =>
        {
            if (!process.HasExited)
            {
                var inactivityDuration = DateTime.UtcNow - lastFfmpegActivity;

                if (inactivityDuration.TotalSeconds > 30 && inactivityDuration.TotalSeconds < 90)
                {
                    _logger.LogWarning(
                        "DIAGNOSTIC: FFmpeg no output for {Sec}s at {Prog}%. Memory: {Mem}MB",
                        (int)inactivityDuration.TotalSeconds, lastProgressPercent,
                        process.WorkingSet64 / 1024 / 1024);
                }

                if (inactivityDuration.TotalSeconds > 90)
                {
                    _logger.LogError(
                        "WATCHDOG: No output for {Sec}s at {Prog}%. Last line: {Line}",
                        (int)inactivityDuration.TotalSeconds, lastProgressPercent,
                        lastStderrLine ?? "N/A");

                    try
                    {
                        process.Kill(entireProcessTree: true);
                        watchdogTimer.Stop();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to kill hung FFmpeg process");
                    }
                }
            }
        };
        watchdogTimer.Start();

        // Heartbeat timer for visibility (every 5 seconds)
        var heartbeatTimer = new System.Timers.Timer(5000);
        heartbeatTimer.Elapsed += (sender, args) =>
        {
            if (!process.HasExited)
            {
                try
                {
                    _logger.LogInformation(
                        "FFmpeg HEARTBEAT: JobId={JobId}, Progress={Progress}%, Elapsed={Elapsed}s, " +
                        "ProcessId={Pid}, Memory={MemoryMB}MB, CPU={CpuSeconds}s",
                        jobId, lastProgressPercent, (DateTime.UtcNow - startTime).TotalSeconds,
                        process.Id,
                        process.WorkingSet64 / 1024 / 1024,
                        process.TotalProcessorTime.TotalSeconds);
                }
                catch
                {
                    // Process may have exited, ignore
                }
            }
        };
        heartbeatTimer.Start();

        // Set up output handler to parse progress and capture output
        process.ErrorDataReceived += (sender, args) =>
        {
            if (string.IsNullOrEmpty(args.Data)) return;

            // Reset watchdog timer on any stderr output
            lastFfmpegActivity = DateTime.UtcNow;
            lastStderrLine = args.Data;

            // Capture for error reporting
            stderrBuilder.AppendLine(args.Data);

            // Write to log file
            try
            {
                logWriter?.WriteLine($"[stderr] {args.Data}");
            }
            catch
            {
                // Ignore log write errors
            }

            // Log the output
            _logger.LogTrace("FFmpeg stderr: {Output}", args.Data);

            // Parse progress if it contains time information
            if (args.Data.Contains("time="))
            {
                // Parse the time value (format: time=00:00:12.34)
                var timeMatch = System.Text.RegularExpressions.Regex.Match(args.Data, @"time=(\d{2}:\d{2}:\d{2}\.\d{2})");
                if (timeMatch.Success)
                {
                    var timeStr = timeMatch.Groups[1].Value;
                    if (TimeSpan.TryParse(timeStr, out var currentTime))
                    {
                        var now = DateTime.Now;

                        // Report progress at most once per second
                        if ((now - lastReportTime).TotalSeconds >= 1)
                        {
                            lastReportTime = now;

                            // Calculate progress percentage
                            float percentage = (float)(currentTime.TotalSeconds / totalDuration.TotalSeconds * 100);
                            percentage = Math.Clamp(percentage, 0, 100);
                            lastProgressPercent = percentage; // Track for watchdog

                            // Enhanced diagnostic logging around 72% mark (common hang point)
                            if (percentage >= 70.0f && percentage <= 75.0f)
                            {
                                _logger.LogInformation(
                                    "DIAGNOSTIC: FFmpeg at {Percentage}% (VideoComposition stage). " +
                                    "Memory: {Memory}MB, CPU: {Cpu}s, Elapsed: {Elapsed}s, JobId: {JobId}",
                                    percentage.ToString("F1"),
                                    process.WorkingSet64 / 1024 / 1024,
                                    process.TotalProcessorTime.TotalSeconds,
                                    (now - startTime).TotalSeconds,
                                    jobId);
                            }

                            // Calculate time remaining
                            var elapsed = now - startTime;
                            var estimatedTotal = TimeSpan.FromSeconds(
                                elapsed.TotalSeconds / (percentage / 100));
                            var remaining = estimatedTotal - elapsed;

                            // Report progress
                            progress.Report(new RenderProgress(
                                percentage,
                                elapsed,
                                remaining,
                                "Rendering video"));
                        }
                    }
                }
            }
        };

        // Capture stdout as well
        process.OutputDataReceived += (sender, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
            {
                stdoutBuilder.AppendLine(args.Data);

                // Write to log file
                try
                {
                    logWriter?.WriteLine($"[stdout] {args.Data}");
                }
                catch
                {
                    // Ignore log write errors
                }

                _logger.LogTrace("FFmpeg stdout: {Output}", args.Data);
            }
        };

        // Set up task to wait for process completion
        var tcs = new TaskCompletionSource<bool>();

        process.Exited += (sender, args) =>
        {
            // Stop watchdog and heartbeat timers
            watchdogTimer.Stop();
            watchdogTimer.Dispose();
            heartbeatTimer.Stop();
            heartbeatTimer.Dispose();

            if (process.ExitCode == 0)
            {
                tcs.SetResult(true);
            }
            else
            {
                var stderr = stderrBuilder.ToString();
                var exception = FfmpegException.FromProcessFailure(
                    process.ExitCode,
                    stderr,
                    jobId,
                    correlationId);
                tcs.SetException(exception);
            }
        };

        // Start the process
        process.Start();
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        // Register with process registry for tracking
        if (_processRegistry != null)
        {
            _processRegistry.Register(process, jobId);
        }

        // Register cancellation with graceful termination
        var cancellationRegistration = ct.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    _logger.LogWarning("Cancelling FFmpeg render (JobId={JobId})", jobId);

                    // Try graceful termination first (send 'q' to stdin if possible)
                    try
                    {
                        process.StandardInput?.Write('q');
                        process.StandardInput?.Flush();

                        // Give it 2 seconds to exit gracefully
                        if (!process.WaitForExit(2000))
                        {
                            _logger.LogWarning("Graceful termination timeout, killing process (JobId={JobId})", jobId);
                            process.Kill(entireProcessTree: true);
                        }
                    }
                    catch
                    {
                        // If graceful termination fails, kill immediately
                        process.Kill(entireProcessTree: true);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to terminate FFmpeg process during cancellation");
            }
        });

        // Wait for completion or cancellation with timeout
        try
        {
            // Set a reasonable timeout (30 minutes for renders)
            var timeoutTask = Task.Delay(TimeSpan.FromMinutes(30), ct);
            var completedTask = await Task.WhenAny(tcs.Task, timeoutTask).ConfigureAwait(false);

            if (completedTask == timeoutTask)
            {
                _logger.LogError("FFmpeg render timeout after 30 minutes (JobId={JobId})", jobId);

                // Kill the process
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (Exception killEx)
                {
                    _logger.LogWarning(killEx, "Failed to kill timed-out FFmpeg process");
                }

                throw new FfmpegException(
                    "FFmpeg render operation timed out after 30 minutes",
                    FfmpegErrorCategory.Timeout,
                    jobId: jobId,
                    correlationId: correlationId,
                    suggestedActions: new[]
                    {
                        "Try with shorter content or lower resolution",
                        "Check system resources (CPU, disk space)",
                        "Ensure FFmpeg is not hanging on a corrupted input file"
                    });
            }

            await tcs.Task.ConfigureAwait(false); // This will throw if the process failed
        }
        catch (FfmpegException)
        {
            // Already properly formatted FFmpeg exception
            throw;
        }
        catch (Exception ex)
        {
            // Unexpected error
            _logger.LogError(ex, "Unexpected FFmpeg error (JobId={JobId})", jobId);
            throw new InvalidOperationException($"FFmpeg render failed unexpectedly: {ex.Message} (JobId: {jobId}, CorrelationId: {correlationId})", ex);
        }
        finally
        {
            // Dispose cancellation registration
            cancellationRegistration.Dispose();

            // Stop and dispose timers
            try
            {
                watchdogTimer?.Stop();
                watchdogTimer?.Dispose();
                heartbeatTimer?.Stop();
                heartbeatTimer?.Dispose();
            }
            catch
            {
                // Ignore cleanup errors
            }

            // Ensure process is cleaned up
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }

            // Close log file
            try
            {
                logWriter?.WriteLine(new string('-', 80));
                logWriter?.WriteLine($"Completed: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                logWriter?.WriteLine($"Exit Code: {process.ExitCode}");
                logWriter?.Dispose();
            }
            catch
            {
                // Ignore cleanup errors
            }

            // Dispose process
            process.Dispose();
        }

        // Verify output file exists before returning
        if (!File.Exists(outputFilePath))
        {
            _logger.LogError("Render reported success but output file does not exist: {Path}", outputFilePath);
            throw new InvalidOperationException($"Video render failed: output file not created at {outputFilePath}");
        }

        var fileInfo = new FileInfo(outputFilePath);
        _logger.LogInformation("Render verified: {Path} ({SizeMB:F2} MB, {SizeBytes} bytes)",
            outputFilePath, fileInfo.Length / 1024.0 / 1024.0, fileInfo.Length);

        if (fileInfo.Length == 0)
        {
            _logger.LogError("Render created empty file: {Path}", outputFilePath);
            throw new InvalidOperationException($"Video render failed: output file is empty at {outputFilePath}");
        }

        // Add minimum size check (video should be at least 100KB for even shortest videos)
        if (fileInfo.Length < 100 * 1024)
        {
            _logger.LogWarning(
                "Output file is suspiciously small: {SizeKB} KB. May be corrupted.",
                fileInfo.Length / 1024);
        }

        _logger.LogInformation("Render completed successfully (JobId={JobId}): {OutputPath}", jobId, outputFilePath);
        _logger.LogInformation("FFmpeg log written to: {LogPath}", ffmpegLogPath);

        // Report 100% completion
        progress.Report(new RenderProgress(
            100,
            DateTime.Now - startTime,
            TimeSpan.Zero,
            "Render complete"));

            return outputFilePath;
        }
        finally
        {
            try
            {
                logWriter?.Dispose();
            }
            catch
            {
                // Ignore cleanup errors during finalization
            }
        }
    }

    /// <summary>
    /// Render using ManagedProcessRunner (preferred method with proper tracking and timeout)
    /// </summary>
    private async Task<string> RenderWithManagedRunnerAsync(
        string ffmpegPath,
        string ffmpegCommand,
        string ffmpegLogPath,
        StreamWriter? logWriter,
        string jobId,
        string correlationId,
        string outputFilePath,
        TimeSpan totalDuration,
        DateTime startTime,
        DateTime lastReportTime,
        IProgress<RenderProgress> progress,
        CancellationToken ct)
    {
        var stderrBuilder = new StringBuilder();
        var stdoutBuilder = new StringBuilder();

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = ffmpegCommand,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        try
        {
            var result = await _processRunner!.RunAsync(
                startInfo,
                jobId: jobId,
                timeout: TimeSpan.FromMinutes(30),
                ct: ct,
                onStdOut: (line) =>
                {
                    if (!string.IsNullOrEmpty(line))
                    {
                        stdoutBuilder.AppendLine(line);
                        try
                        {
                            logWriter?.WriteLine($"[stdout] {line}");
                        }
                        catch { }
                        _logger.LogTrace("FFmpeg stdout: {Output}", line);
                    }
                },
                onStdErr: (line) =>
                {
                    if (string.IsNullOrEmpty(line)) return;

                    stderrBuilder.AppendLine(line);

                    try
                    {
                        logWriter?.WriteLine($"[stderr] {line}");
                    }
                    catch { }

                    _logger.LogTrace("FFmpeg stderr: {Output}", line);

                    // Parse progress if it contains time information
                    if (line.Contains("time="))
                    {
                        var timeMatch = System.Text.RegularExpressions.Regex.Match(line, @"time=(\d{2}:\d{2}:\d{2}\.\d{2})");
                        if (timeMatch.Success)
                        {
                            var timeStr = timeMatch.Groups[1].Value;
                            if (TimeSpan.TryParse(timeStr, out var currentTime))
                            {
                                var now = DateTime.Now;

                                // Report progress at most once per second
                                if ((now - lastReportTime).TotalSeconds >= 1)
                                {
                                    lastReportTime = now;

                                    // Calculate progress percentage
                                    float percentage = (float)(currentTime.TotalSeconds / totalDuration.TotalSeconds * 100);
                                    percentage = Math.Clamp(percentage, 0, 100);

                                    // Calculate time remaining
                                    var elapsed = now - startTime;
                                    var estimatedTotal = TimeSpan.FromSeconds(
                                        elapsed.TotalSeconds / (percentage / 100));
                                    var remaining = estimatedTotal - elapsed;

                                    // Report progress
                                    progress.Report(new RenderProgress(
                                        percentage,
                                        elapsed,
                                        remaining,
                                        "Rendering video"));
                                }
                            }
                        }
                    }
                }
            ).ConfigureAwait(false);

            // Close log file
            try
            {
                logWriter?.WriteLine(new string('-', 80));
                logWriter?.WriteLine($"Completed: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                logWriter?.WriteLine($"Exit Code: {result.ExitCode}");
                logWriter?.Dispose();
            }
            catch { }

            if (result.ExitCode != 0)
            {
                var stderr = stderrBuilder.ToString();
                throw FfmpegException.FromProcessFailure(
                    result.ExitCode,
                    stderr,
                    jobId,
                    correlationId);
            }

            // Verify output file exists before returning
            if (!File.Exists(outputFilePath))
            {
                _logger.LogError("Render reported success but output file does not exist: {Path}", outputFilePath);
                throw new InvalidOperationException($"Video render failed: output file not created at {outputFilePath}");
            }

            var fileInfo = new FileInfo(outputFilePath);
            _logger.LogInformation("Render verified: {Path} ({Size} bytes)", outputFilePath, fileInfo.Length);

            if (fileInfo.Length == 0)
            {
                _logger.LogError("Render created empty file: {Path}", outputFilePath);
                throw new InvalidOperationException($"Video render failed: output file is empty at {outputFilePath}");
            }

            _logger.LogInformation("Render completed successfully (JobId={JobId}): {OutputPath}", jobId, outputFilePath);
            _logger.LogInformation("FFmpeg log written to: {LogPath}", ffmpegLogPath);

            // Report 100% completion
            progress.Report(new RenderProgress(
                100,
                DateTime.Now - startTime,
                TimeSpan.Zero,
                "Render complete"));

            return outputFilePath;
        }
        catch (TimeoutException)
        {
            _logger.LogError("FFmpeg render timeout after 30 minutes (JobId={JobId})", jobId);
            throw new FfmpegException(
                "FFmpeg render operation timed out after 30 minutes",
                FfmpegErrorCategory.Timeout,
                jobId: jobId,
                correlationId: correlationId,
                suggestedActions: new[]
                {
                    "Try with shorter content or lower resolution",
                    "Check system resources (CPU, disk space)",
                    "Ensure FFmpeg is not hanging on a corrupted input file"
                });
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("FFmpeg render cancelled (JobId={JobId})", jobId);
            throw;
        }
        catch (FfmpegException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected FFmpeg error (JobId={JobId})", jobId);
            throw new InvalidOperationException(
                $"FFmpeg render failed unexpectedly: {ex.Message} (JobId: {jobId}, CorrelationId: {correlationId})",
                ex);
        }
        finally
        {
            try
            {
                logWriter?.Dispose();
            }
            catch { }
        }
    }

    /// <summary>
    /// Build FFmpeg command using FFmpegCommandBuilder with hardware acceleration support.
    /// Applies Ken Burns effects to static images and fade transitions between scenes by default.
    /// </summary>
    private async Task<string> BuildFfmpegCommandAsync(Timeline timeline, RenderSpec spec, string outputPath, string ffmpegPath, CancellationToken ct)
    {
        _logger.LogInformation("[FFMPEG-BUILD] Building command for {SceneCount} scenes", timeline.Scenes?.Count ?? 0);

        if (timeline.Scenes == null || timeline.Scenes.Count == 0)
        {
            throw new InvalidOperationException(
                "Cannot render: Timeline has zero scenes. " +
                "Script parsing or visual asset generation may have failed.");
        }

        if (string.IsNullOrEmpty(timeline.NarrationPath))
        {
            throw new InvalidOperationException(
                "Cannot render: Timeline has no narration audio path. " +
                "TTS synthesis may have failed without recovery.");
        }

        if (!File.Exists(timeline.NarrationPath))
        {
            throw new InvalidOperationException(
                $"Cannot render: Narration file not found: {timeline.NarrationPath}");
        }

        _logger.LogInformation("Building FFmpeg command for render spec: {Codec} @ {Width}x{Height}, {Fps}fps, {VideoBitrate}kbps",
            spec.Codec, spec.Res.Width, spec.Res.Height, spec.Fps, spec.VideoBitrateK);

        if (!string.IsNullOrEmpty(timeline.MusicPath) && !File.Exists(timeline.MusicPath))
        {
            _logger.LogWarning("Music file not found, will skip music: {Path}", timeline.MusicPath);
        }

        // Collect visual assets from scenes for video composition
        var visualAssets = CollectVisualAssets(timeline);
        _logger.LogInformation("[FFMPEG-BUILD] Collected {AssetCount} visual assets", visualAssets.Count);

        if (visualAssets.Count == 0)
        {
            var sceneAssetSummary = string.Join(", ",
                timeline.Scenes.Select(s =>
                    $"[{s.Index}:{(timeline.SceneAssets.TryGetValue(s.Index, out var assetsForScene) ? assetsForScene.Count : 0)} assets]"));

            throw new InvalidOperationException(
                "Cannot render: No visual assets found in timeline. " +
                "Image generation may have failed or returned placeholder references.  " +
                $"Scenes: {sceneAssetSummary}");
        }

        foreach (var asset in visualAssets)
        {
            if (!File.Exists(asset.Path))
            {
                throw new InvalidOperationException(
                    $"Cannot render: Visual asset not found: {asset.Path}");
            }
        }

        _logger.LogInformation("[FFMPEG-BUILD] All {Count} visual assets validated", visualAssets.Count);

        // Determine aspect ratio from resolution
        var aspectRatio = spec.Res.Width == spec.Res.Height ? AspectRatio.OneByOne :
                         spec.Res.Width > spec.Res.Height ? AspectRatio.SixteenByNine :
                         AspectRatio.NineBySixteen;

        // Create export preset from render spec
        var exportPreset = new ExportPreset(
            Name: "Custom",
            Description: $"Custom render at {spec.Res.Width}x{spec.Res.Height}",
            Platform: Platform.Generic,
            Container: spec.Container,
            VideoCodec: spec.Codec.ToLowerInvariant() switch
            {
                "h264" => "libx264",
                "h265" or "hevc" => "libx265",
                "vp9" => "libvpx-vp9",
                "av1" => "libaom-av1",
                _ => "libx264"
            },
            AudioCodec: "aac",
            Resolution: spec.Res,
            FrameRate: spec.Fps,
            VideoBitrate: spec.VideoBitrateK,
            AudioBitrate: spec.AudioBitrateK,
            PixelFormat: "yuv420p",
            ColorSpace: "bt709",
            AspectRatio: aspectRatio,
            Quality: spec.QualityLevel >= 90 ? QualityLevel.Maximum :
                     spec.QualityLevel >= 75 ? QualityLevel.High :
                     spec.QualityLevel >= 50 ? QualityLevel.Good :
                     QualityLevel.Draft
        );

        // Detect hardware capabilities and select best encoder
        // Create hardware encoder with ffmpeg path (use NullLogger since we log from FfmpegVideoComposer)
        var hardwareEncoderWithPath = new HardwareEncoder(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<HardwareEncoder>.Instance,
            ffmpegPath);
        EncoderConfig encoderConfig;

        try
        {
            encoderConfig = await hardwareEncoderWithPath.SelectBestEncoderAsync(exportPreset, preferHardware: true).ConfigureAwait(false);
            _logger.LogInformation("Selected encoder: {Encoder} (Hardware: {IsHardware})",
                encoderConfig.EncoderName, encoderConfig.IsHardwareAccelerated);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to detect hardware encoder, falling back to software");
            encoderConfig = new EncoderConfig(
                exportPreset.VideoCodec,
                "Software encoder (fallback)",
                false,
                new Dictionary<string, string>());
        }

        // Build command using FFmpegCommandBuilder
        var builder = new FFmpegCommandBuilder()
            .SetOverwrite(true);

        // Add hardware acceleration if available
        if (encoderConfig.IsHardwareAccelerated)
        {
            // Set hwaccel based on encoder type
            if (encoderConfig.EncoderName.Contains("nvenc", StringComparison.OrdinalIgnoreCase))
            {
                builder.SetHardwareAcceleration("cuda");
            }
            else if (encoderConfig.EncoderName.Contains("qsv", StringComparison.OrdinalIgnoreCase))
            {
                builder.SetHardwareAcceleration("qsv");
            }
            else if (encoderConfig.EncoderName.Contains("amf", StringComparison.OrdinalIgnoreCase))
            {
                builder.SetHardwareAcceleration("d3d11va");
            }
        }

        // Track input file indices for complex filter graph
        int inputIndex = 0;

        // Add visual asset inputs first (images/videos for scenes)
        foreach (var asset in visualAssets)
        {
            builder.AddInput(asset.Path);
            inputIndex++;
        }

        // Add narration audio input
        int narrationInputIndex = inputIndex;
        builder.AddInput(timeline.NarrationPath);
        inputIndex++;

        // Add music input if available
        int musicInputIndex = -1;
        bool hasMusicInput = !string.IsNullOrEmpty(timeline.MusicPath) && File.Exists(timeline.MusicPath);
        if (hasMusicInput)
        {
            musicInputIndex = inputIndex;
            builder.AddInput(timeline.MusicPath);
            inputIndex++;
        }

        // Build complex filter graph for visual composition with effects
        if (visualAssets.Count > 0)
        {
            var filterGraph = BuildVisualCompositionFilter(
                visualAssets,
                timeline.Scenes,
                spec.Res.Width,
                spec.Res.Height,
                spec.Fps,
                narrationInputIndex,
                musicInputIndex);

            if (!string.IsNullOrEmpty(filterGraph))
            {
                builder.AddFilter(filterGraph);
                _logger.LogInformation("Applied Ken Burns effects and fade transitions to {Count} visual assets", visualAssets.Count);
            }
        }
        else
        {
            _logger.LogWarning("No visual assets found in timeline - video will be audio-only with black screen");
        }

        // Set video codec (use hardware encoder if available)
        builder.SetVideoCodec(encoderConfig.EncoderName);

        // Apply encoder-specific parameters
        foreach (var param in encoderConfig.Parameters)
        {
            // These would need to be added as options to the command
            _logger.LogDebug("Encoder parameter: {Key}={Value}", param.Key, param.Value);
        }

        // Set encoding preset based on quality
        var preset = exportPreset.Quality switch
        {
            QualityLevel.Draft => "ultrafast",
            QualityLevel.Good => "fast",
            QualityLevel.High => "medium",
            QualityLevel.Maximum => "slow",
            _ => "medium"
        };
        builder.SetPreset(preset);

        // Set CRF for quality (lower is better, 18-28 is good range)
        var crf = spec.QualityLevel >= 90 ? 18 :
                 spec.QualityLevel >= 75 ? 23 :
                 spec.QualityLevel >= 50 ? 28 :
                 33;
        builder.SetCRF(crf);

        // Set resolution and frame rate
        builder.SetResolution(spec.Res.Width, spec.Res.Height);
        builder.SetFrameRate(spec.Fps);

        // Set pixel format
        builder.SetPixelFormat(exportPreset.PixelFormat);

        // Set audio codec and bitrate
        builder.SetAudioCodec(exportPreset.AudioCodec);
        builder.SetAudioBitrate(spec.AudioBitrateK);

        // Set video bitrate
        builder.SetVideoBitrate(spec.VideoBitrateK);

        // Set audio settings for better quality
        builder.SetAudioSampleRate(48000); // Standard sample rate
        builder.SetAudioChannels(2); // Stereo

        // Add metadata
        builder.AddMetadata("title", "Generated by Aura Video Studio");
        builder.AddMetadata("encoder", "Aura Video Studio");
        builder.AddMetadata("creation_time", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));

        // Set output file
        builder.SetOutput(outputPath);

        // Build the command
        var command = builder.Build();

        // Enhanced logging for diagnostics
        _logger.LogInformation("FFmpeg command built successfully: {Length} characters", command.Length);
        _logger.LogDebug("Full command: ffmpeg {Command}", command);

        // Log detailed input file information
        try
        {
            var narrationFileInfo = new FileInfo(timeline.NarrationPath);
            var musicInfo = !string.IsNullOrEmpty(timeline.MusicPath) && File.Exists(timeline.MusicPath)
                ? $"{timeline.MusicPath} ({new FileInfo(timeline.MusicPath).Length / 1024.0 / 1024.0:F2} MB)"
                : "None";

            _logger.LogInformation(
                "FFmpeg input files:\n" +
                "  Narration: {NarrationPath} ({NarrationSizeMB:F2} MB)\n" +
                "  Music: {MusicPath}\n" +
                "  Visual Assets: {AssetCount} files\n" +
                "  Output: {OutputPath}",
                timeline.NarrationPath,
                narrationFileInfo.Length / 1024.0 / 1024.0,
                musicInfo,
                visualAssets.Count,
                outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log input file details");
        }

        return command;
    }

    /// <summary>
    /// Represents a visual asset with its associated scene timing
    /// </summary>
    private record VisualAssetInfo(string Path, int SceneIndex, TimeSpan Start, TimeSpan Duration, bool IsImage);

    /// <summary>
    /// Collects visual assets from the timeline's scene assets dictionary
    /// </summary>
    private List<VisualAssetInfo> CollectVisualAssets(Timeline timeline)
    {
        var assets = new List<VisualAssetInfo>();

        foreach (var scene in timeline.Scenes)
        {
            if (timeline.SceneAssets.TryGetValue(scene.Index, out var sceneAssets) && sceneAssets.Count > 0)
            {
                // Take the first valid asset for each scene
                var primaryAsset = sceneAssets.FirstOrDefault(a =>
                    !string.IsNullOrEmpty(a.PathOrUrl) &&
                    File.Exists(a.PathOrUrl));

                if (primaryAsset != null)
                {
                    var isImage = IsImageFile(primaryAsset.PathOrUrl);
                    assets.Add(new VisualAssetInfo(
                        primaryAsset.PathOrUrl,
                        scene.Index,
                        scene.Start,
                        scene.Duration,
                        isImage));
                }
            }
        }

        return assets;
    }

    /// <summary>
    /// Checks if a file is an image based on extension
    /// </summary>
    private static bool IsImageFile(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension is ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".tiff" or ".tif";
    }

    /// <summary>
    /// Builds a complex filter graph for visual composition with Ken Burns effects and fade transitions.
    /// Applies subtle Ken Burns effect (1.0 to 1.1 zoom) to static images by default.
    /// Applies fade transitions (0.5s) between scenes.
    /// </summary>
    private string BuildVisualCompositionFilter(
        List<VisualAssetInfo> assets,
        IReadOnlyList<Scene> scenes,
        int width,
        int height,
        int fps,
        int narrationInputIndex,
        int musicInputIndex)
    {
        if (assets.Count == 0)
        {
            return string.Empty;
        }

        var filterParts = new List<string>();

        // Phase 1: Process each visual asset (Ken Burns for images, scale for videos)
        for (int i = 0; i < assets.Count; i++)
        {
            var asset = assets[i];
            var durationSeconds = asset.Duration.TotalSeconds;

            if (asset.IsImage)
            {
                var kenBurnsFilter = EffectBuilder.BuildKenBurns(
                    duration: durationSeconds,
                    fps: fps,
                    zoomStart: DefaultKenBurnsZoomStart,
                    zoomEnd: DefaultKenBurnsZoomEnd,
                    panX: 0.0,
                    panY: 0.0,
                    width: width,
                    height: height);

                filterParts.Add($"[{i}:v]{kenBurnsFilter}[v{i}]");
            }
            else
            {
                filterParts.Add($"[{i}:v]scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2,setsar=1[v{i}]");
            }
        }

        // Phase 2: Build transition chain with SAFE offset calculation
        if (assets.Count == 1)
        {
            filterParts.Add("[v0]null[vout]");
        }
        else
        {
            double currentOffset = 0.0;

            for (int i = 0; i < assets.Count - 1; i++)
            {
                var currentSceneDuration = assets[i].Duration.TotalSeconds;
                var nextSceneDuration = assets[i + 1].Duration.TotalSeconds;
                
                // Skip transition if either scene is too short
                // This prevents invalid FFmpeg filter parameters
                if (currentSceneDuration < MinSceneDurationForTransition || nextSceneDuration < MinSceneDurationForTransition)
                {
                    _logger.LogWarning(
                        "Scene {Index} or {NextIndex} duration too short ({CurrentDuration}s, {NextDuration}s). " +
                        "Skipping crossfade transition to prevent FFmpeg hang.",
                        i, i + 1, currentSceneDuration, nextSceneDuration);
                    
                    // Just concatenate without transition
                    var concatInput1 = i == 0 ? "v0" : $"vt{i - 1}";
                    var concatInput2 = $"v{i + 1}";
                    var concatOutput = i == assets.Count - 2 ? "vout" : $"vt{i}";
                    
                    // Use concat filter for direct concatenation
                    filterParts.Add($"[{concatInput1}][{concatInput2}]concat=n=2:v=1:a=0[{concatOutput}]");
                    currentOffset += currentSceneDuration;
                    continue;
                }

                // Accumulate offset BEFORE calculating transition
                currentOffset += currentSceneDuration;

                // Calculate safe transition parameters
                var transitionDuration = DefaultFadeTransitionDuration;
                
                // If scene is shorter than transition, reduce transition duration
                if (currentSceneDuration < transitionDuration)
                {
                    transitionDuration = Math.Max(MinTransitionDuration, currentSceneDuration * 0.5);
                    _logger.LogWarning(
                        "Scene {Index} duration ({Duration}s) shorter than default transition. " +
                        "Reduced transition to {Adjusted}s",
                        i, currentSceneDuration, transitionDuration);
                }
                
                // Ensure transition offset is never negative
                var safeTransitionOffset = Math.Max(0, currentOffset - transitionDuration);

                var fadeTransition = TransitionBuilder.BuildCrossfade(
                    transitionDuration,
                    safeTransitionOffset,
                    TransitionBuilder.TransitionType.Fade);

                var inputLabel1 = i == 0 ? "v0" : $"vt{i - 1}";
                var inputLabel2 = $"v{i + 1}";
                var outputLabel = i == assets.Count - 2 ? "vout" : $"vt{i}";

                filterParts.Add($"[{inputLabel1}][{inputLabel2}]{fadeTransition}[{outputLabel}]");
            }
        }

        // Phase 3: Audio mixing (AFTER video chain is complete)
        if (musicInputIndex >= 0)
        {
            filterParts.Add($"[{narrationInputIndex}:a]volume=1.0[voice];[{musicInputIndex}:a]volume=0.3[music];[voice][music]amix=inputs=2:duration=shortest[aout]");
        }

        var filterGraph = string.Join(";", filterParts);

        // FIX: Log the full filter graph for debugging
        _logger.LogDebug("Generated filter graph ({Length} chars): {Graph}",
            filterGraph.Length,
            filterGraph.Length > 500 ? filterGraph.Substring(0, 500) + "..." : filterGraph);

        return filterGraph;
    }

    /// <summary>
    /// Validate FFmpeg binary before use
    /// </summary>
    private async Task ValidateFfmpegBinaryAsync(string ffmpegPath, string jobId, string correlationId, CancellationToken ct)
    {
        _logger.LogInformation("Validating FFmpeg binary: {Path}", ffmpegPath);

        // For executables in PATH (like "ffmpeg"), File.Exists() will return false
        // So we skip the file existence check and go straight to running the command
        // The command execution will fail if FFmpeg is not found, giving us better error info
        bool isPathExecutable = !Path.IsPathRooted(ffmpegPath) &&
                                !ffmpegPath.Contains(Path.DirectorySeparatorChar) &&
                                !ffmpegPath.Contains(Path.AltDirectorySeparatorChar);

        // Check file exists only if it's an absolute or relative path (not just an executable name)
        if (!isPathExecutable && !File.Exists(ffmpegPath))
        {
            var error = new
            {
                code = "E302-FFMPEG_VALIDATION",
                message = "FFmpeg binary not found",
                ffmpegPath = ffmpegPath,
                correlationId,
                howToFix = new[]
                {
                    "Install FFmpeg via Download Center",
                    "Attach an existing FFmpeg installation",
                    "Check FFmpeg installation path in settings",
                    "Add FFmpeg to system PATH"
                }
            };

            _logger.LogError("FFmpeg validation failed: {Error}", System.Text.Json.JsonSerializer.Serialize(error));
            throw new InvalidOperationException($"FFmpeg binary not found at {ffmpegPath}. " +
                $"Install FFmpeg via Download Center or attach an existing installation. (CorrelationId: {correlationId})");
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = "-version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start FFmpeg process for validation");
            }

            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            var output = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            var stderr = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                var error = new
                {
                    code = "E302-FFMPEG_VALIDATION",
                    message = "FFmpeg validation failed",
                    exitCode = process.ExitCode,
                    stderr = stderr.Length > 1000 ? string.Concat(stderr.AsSpan(0, 1000), "...") : stderr,
                    correlationId,
                    howToFix = new[]
                    {
                        "FFmpeg binary may be corrupted - try reinstalling",
                        "Check system dependencies (Visual C++ Redistributable on Windows)",
                        "Use 'Repair' option in Download Center"
                    }
                };

                _logger.LogError("FFmpeg validation failed: {Error}", System.Text.Json.JsonSerializer.Serialize(error));
                throw new InvalidOperationException($"FFmpeg validation failed with exit code {process.ExitCode}. " +
                    $"See logs for details. (CorrelationId: {correlationId})");
            }

            _logger.LogInformation("FFmpeg validation successful: {Version}",
                output.Split('\n').FirstOrDefault(l => l.Contains("ffmpeg version")) ?? "version unknown");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            var error = new
            {
                code = "E302-FFMPEG_VALIDATION",
                message = "FFmpeg validation exception",
                exception = ex.Message,
                correlationId,
                howToFix = new[]
                {
                    "Ensure FFmpeg has execute permissions",
                    "Check antivirus/security software blocking FFmpeg",
                    "Try attaching a different FFmpeg installation"
                }
            };

            _logger.LogError(ex, "FFmpeg validation exception: {Error}", System.Text.Json.JsonSerializer.Serialize(error));
            throw new InvalidOperationException($"FFmpeg validation failed: {ex.Message} (CorrelationId: {correlationId})", ex);
        }
    }

    /// <summary>
    /// Pre-validate audio files before rendering and attempt remediation if needed
    /// </summary>
    private async Task PreValidateAudioAsync(Timeline timeline, string ffmpegPath, string jobId, string correlationId, CancellationToken ct)
    {
        _logger.LogInformation("Pre-validating audio files (JobId={JobId})", jobId);

        // Get ffprobe path (same directory as ffmpeg)
        var ffmpegDir = Path.GetDirectoryName(ffmpegPath);
        var ffprobePath = ffmpegDir != null ? Path.Combine(ffmpegDir, "ffprobe.exe") : null;
        if (ffprobePath != null && !File.Exists(ffprobePath))
        {
            ffprobePath = null;
        }

        var validator = new Aura.Core.Audio.AudioValidator(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Aura.Core.Audio.AudioValidator>.Instance,
            ffmpegPath,
            ffprobePath);

        // Validate narration
        if (!string.IsNullOrEmpty(timeline.NarrationPath))
        {
            await ValidateAndRemediateAudioFileAsync(
                validator,
                timeline.NarrationPath,
                "narration",
                jobId,
                correlationId,
                ct).ConfigureAwait(false);
        }

        // Validate music if present
        if (!string.IsNullOrEmpty(timeline.MusicPath))
        {
            await ValidateAndRemediateAudioFileAsync(
                validator,
                timeline.MusicPath,
                "music",
                jobId,
                correlationId,
                ct).ConfigureAwait(false);
        }

        _logger.LogInformation("Audio pre-validation complete (JobId={JobId})", jobId);
    }

    /// <summary>
    /// Validate a single audio file and attempt remediation if corrupted
    /// </summary>
    private async Task ValidateAndRemediateAudioFileAsync(
        Aura.Core.Audio.AudioValidator validator,
        string audioPath,
        string audioType,
        string jobId,
        string correlationId,
        CancellationToken ct)
    {
        _logger.LogInformation("Validating {AudioType} audio: {Path} (JobId={JobId})",
            audioType, audioPath, jobId);

        var validation = await validator.ValidateAsync(audioPath, ct).ConfigureAwait(false);

        if (validation.IsValid)
        {
            _logger.LogInformation("{AudioType} audio validation passed (JobId={JobId})", audioType, jobId);
            return;
        }

        // Audio is invalid - attempt remediation
        _logger.LogWarning("{AudioType} audio validation failed: {Error} (JobId={JobId})",
            audioType, validation.ErrorMessage, jobId);

        if (validation.IsCorrupted)
        {
            _logger.LogInformation("Attempting to re-encode corrupted {AudioType} audio (JobId={JobId})",
                audioType, jobId);

            // Try re-encoding to a clean WAV
            var reEncodedPath = Path.Combine(
                Path.GetDirectoryName(audioPath) ?? Path.GetTempPath(),
                $"{Path.GetFileNameWithoutExtension(audioPath)}_reencoded.wav");

            var (success, errorMessage) = await validator.ReencodeAsync(audioPath, reEncodedPath, ct).ConfigureAwait(false);

            if (success)
            {
                _logger.LogInformation("Successfully re-encoded {AudioType} audio (JobId={JobId})",
                    audioType, jobId);

                // Replace the original file with the re-encoded version
                File.Delete(audioPath);
                File.Move(reEncodedPath, audioPath);

                _logger.LogInformation("Replaced corrupted {AudioType} with re-encoded version (JobId={JobId})",
                    audioType, jobId);
                return;
            }

            _logger.LogError("Re-encoding failed: {Error}. Attempting to generate silent fallback (JobId={JobId})",
                errorMessage, jobId);

            // Re-encoding failed - generate silent WAV as fallback
            var (silentSuccess, silentError) = await validator.GenerateSilentWavAsync(
                audioPath + ".silent.wav",
                10.0, // 10 seconds default
                ct).ConfigureAwait(false);

            if (silentSuccess)
            {
                _logger.LogWarning("Generated silent {AudioType} fallback (JobId={JobId})", audioType, jobId);

                // Replace with silent version
                File.Delete(audioPath);
                File.Move(audioPath + ".silent.wav", audioPath);
                return;
            }

            // All remediation attempts failed
            var error = new
            {
                code = "E305-AUDIO_VALIDATION",
                message = $"{audioType} audio file is corrupted and could not be repaired",
                audioPath,
                validationError = validation.ErrorMessage,
                reencodeError = errorMessage,
                silentFallbackError = silentError,
                jobId,
                correlationId,
                howToFix = new[]
                {
                    $"Re-generate the {audioType} audio",
                    "Ensure TTS provider is working correctly",
                    "Check audio file is not in use by another application",
                    "Try using a different TTS provider"
                }
            };

            _logger.LogError("All audio remediation attempts failed: {Error}",
                System.Text.Json.JsonSerializer.Serialize(error));

            throw new InvalidOperationException(
                $"{audioType} audio validation failed and remediation unsuccessful: {validation.ErrorMessage}. " +
                $"CorrelationId: {correlationId}, JobId: {jobId}");
        }

        // Not corrupted but still invalid (e.g., file not found)
        throw new InvalidOperationException(
            $"{audioType} audio validation failed: {validation.ErrorMessage}. " +
            $"CorrelationId: {correlationId}, JobId: {jobId}");
    }

    /// <summary>
    /// Validates all input files before FFmpeg execution to fail fast on corrupted files
    /// </summary>
    private async Task ValidateInputFilesAsync(Timeline timeline, string ffmpegPath, string jobId, string correlationId, CancellationToken ct)
    {
        _logger.LogInformation("Pre-validating input files before FFmpeg execution (JobId={JobId})...", jobId);

        // Validate narration file
        if (string.IsNullOrEmpty(timeline.NarrationPath) || !File.Exists(timeline.NarrationPath))
        {
            throw new InvalidOperationException($"Narration file not found: {timeline.NarrationPath}");
        }

        var narrationInfo = new FileInfo(timeline.NarrationPath);
        if (narrationInfo.Length == 0)
        {
            throw new InvalidOperationException($"Narration file is empty: {timeline.NarrationPath}");
        }

        // Use ffprobe to validate narration is valid audio
        await ValidateMediaFileAsync(timeline.NarrationPath, "audio", ffmpegPath, jobId, correlationId, ct).ConfigureAwait(false);

        // Validate music file if present
        if (!string.IsNullOrEmpty(timeline.MusicPath) && File.Exists(timeline.MusicPath))
        {
            try
            {
                await ValidateMediaFileAsync(timeline.MusicPath, "audio", ffmpegPath, jobId, correlationId, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Music file validation failed, will skip music: {Path}", timeline.MusicPath);
            }
        }

        // Validate visual assets
        var visualAssets = CollectVisualAssets(timeline);
        foreach (var asset in visualAssets)
        {
            if (!File.Exists(asset.Path))
            {
                throw new FileNotFoundException($"Visual asset not found: {asset.Path}");
            }

            var assetInfo = new FileInfo(asset.Path);
            if (assetInfo.Length == 0)
            {
                throw new InvalidOperationException($"Visual asset is empty: {asset.Path}");
            }

            // Determine expected type - be explicit about what we expect
            string expectedType;
            if (asset.IsImage)
            {
                expectedType = "image";
            }
            else
            {
                // For video files, accept both image and video codec types since
                // some video files may also contain image streams
                expectedType = "image|video";
            }

            await ValidateMediaFileAsync(asset.Path, expectedType, ffmpegPath, jobId, correlationId, ct).ConfigureAwait(false);
        }

        _logger.LogInformation("All input files validated successfully (JobId={JobId})", jobId);
    }

    /// <summary>
    /// Validates a media file using ffprobe
    /// </summary>
    private async Task ValidateMediaFileAsync(string filePath, string expectedType, string ffmpegPath, string jobId, string correlationId, CancellationToken ct)
    {
        // Get ffprobe path (same directory as ffmpeg)
        var ffmpegDir = Path.GetDirectoryName(ffmpegPath);
        string ffprobePath;

        if (string.IsNullOrEmpty(ffmpegDir))
        {
            // FFmpeg is in PATH, assume ffprobe is too
            ffprobePath = "ffprobe";
        }
        else
        {
            // FFmpeg has a full path, look for ffprobe in the same directory
            // Check OS to determine correct executable extension
            var ffprobeFileName = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
            ffprobePath = Path.Combine(ffmpegDir, ffprobeFileName);
            
            if (!File.Exists(ffprobePath))
            {
                // Try alternate extension (shouldn't happen with correct OS detection, but fallback)
                var alternateName = OperatingSystem.IsWindows() ? "ffprobe" : "ffprobe.exe";
                var alternatePath = Path.Combine(ffmpegDir, alternateName);
                
                if (File.Exists(alternatePath))
                {
                    ffprobePath = alternatePath;
                }
                else
                {
                    // Fall back to PATH
                    ffprobePath = "ffprobe";
                }
            }
        }

        var args = $"-v error -show_entries stream=codec_type -of default=noprint_wrappers=1:nokey=1 \"{filePath}\"";

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffprobePath,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        try
        {
            process.Start();
            var stdout = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            var stderr = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Media file validation failed for {filePath}: {stderr}");
            }

            var codecTypes = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var isValid = codecTypes.Any(type => expectedType.Contains(type.Trim(), StringComparison.OrdinalIgnoreCase));

            if (!isValid)
            {
                throw new InvalidOperationException(
                    $"Media file {filePath} is not a valid {expectedType} file. " +
                    $"Detected types: {string.Join(", ", codecTypes)}");
            }

            _logger.LogDebug("Media file validated: {Path} (Type: {Type})", filePath, string.Join(", ", codecTypes));
        }
        finally
        {
            process.Dispose();
        }
    }


}
