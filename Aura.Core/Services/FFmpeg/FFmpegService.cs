using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aura.Core.Dependencies;
using Aura.Core.Errors;
using Microsoft.Extensions.Logging;

namespace Aura.Core.Services.FFmpeg;

/// <summary>
/// FFmpeg execution result
/// </summary>
public record FFmpegResult
{
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string StandardOutput { get; init; } = string.Empty;
    public string StandardError { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Progress information from FFmpeg execution
/// </summary>
public record FFmpegProgress
{
    public TimeSpan ProcessedDuration { get; init; }
    public double Fps { get; init; }
    public double Bitrate { get; init; }
    public long Size { get; init; }
    public int Frame { get; init; }
    public double Speed { get; init; }
    public double PercentComplete { get; init; }
}

/// <summary>
/// Service for executing FFmpeg commands with progress tracking
/// </summary>
public interface IFFmpegService
{
    /// <summary>
    /// Execute an FFmpeg command
    /// </summary>
    Task<FFmpegResult> ExecuteAsync(
        string arguments,
        Action<FFmpegProgress>? progressCallback = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get the FFmpeg version
    /// </summary>
    Task<string> GetVersionAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Check if FFmpeg is available and working
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get information about a video file
    /// </summary>
    Task<VideoInfo> GetVideoInfoAsync(string filePath, CancellationToken cancellationToken = default);
}

/// <summary>
/// Video file information
/// </summary>
public record VideoInfo
{
    public TimeSpan Duration { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public double FrameRate { get; init; }
    public string VideoCodec { get; init; } = string.Empty;
    public string AudioCodec { get; init; } = string.Empty;
    public long BitRate { get; init; }
    public long FileSize { get; init; }
}

/// <summary>
/// Implementation of FFmpeg service
/// </summary>
public class FFmpegService : IFFmpegService
{
    private readonly IFfmpegLocator _ffmpegLocator;
    private readonly ILogger<FFmpegService> _logger;
    private string? _cachedFfmpegPath;

    public FFmpegService(IFfmpegLocator ffmpegLocator, ILogger<FFmpegService> logger)
    {
        _ffmpegLocator = ffmpegLocator ?? throw new ArgumentNullException(nameof(ffmpegLocator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<FFmpegResult> ExecuteAsync(
        string arguments,
        Action<FFmpegProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        var ffmpegPath = await GetFfmpegPathAsync(cancellationToken);
        
        _logger.LogInformation("Executing FFmpeg: {Arguments}", arguments);
        
        var startTime = DateTime.UtcNow;
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();
        
        var processStartInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            using var process = new Process { StartInfo = processStartInfo };
            
            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    outputBuilder.AppendLine(e.Data);
                }
            };
            
            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    errorBuilder.AppendLine(e.Data);
                    
                    // Parse progress information from stderr
                    if (progressCallback != null)
                    {
                        var progress = ParseProgress(e.Data);
                        if (progress != null)
                        {
                            progressCallback(progress);
                        }
                    }
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);
            
            var duration = DateTime.UtcNow - startTime;
            var success = process.ExitCode == 0;
            
            if (!success)
            {
                _logger.LogWarning("FFmpeg exited with code {ExitCode}", process.ExitCode);
            }

            return new FFmpegResult
            {
                Success = success,
                ExitCode = process.ExitCode,
                StandardOutput = outputBuilder.ToString(),
                StandardError = errorBuilder.ToString(),
                Duration = duration,
                ErrorMessage = success ? null : $"FFmpeg exited with code {process.ExitCode}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing FFmpeg command");
            
            return new FFmpegResult
            {
                Success = false,
                ExitCode = -1,
                StandardOutput = outputBuilder.ToString(),
                StandardError = errorBuilder.ToString(),
                Duration = DateTime.UtcNow - startTime,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync("-version", cancellationToken: cancellationToken);
        
        if (!result.Success)
        {
            throw FfmpegException.FromProcessFailure(
                result.ExitCode, 
                result.StandardError, 
                correlationId: "GetVersion");
        }
        
        // Parse version from output (first line typically contains version)
        var lines = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return lines.Length > 0 ? lines[0] : "Unknown";
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var ffmpegPath = await GetFfmpegPathAsync(cancellationToken);
            return !string.IsNullOrEmpty(ffmpegPath) && File.Exists(ffmpegPath);
        }
        catch
        {
            return false;
        }
    }

    public async Task<VideoInfo> GetVideoInfoAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Video file not found", filePath);
        }

        var arguments = $"-i \"{filePath}\" -hide_banner";
        var result = await ExecuteAsync(arguments, cancellationToken: cancellationToken);
        
        // FFmpeg outputs file info to stderr
        var info = ParseVideoInfo(result.StandardError, filePath);
        return info;
    }

    private async Task<string> GetFfmpegPathAsync(CancellationToken cancellationToken)
    {
        if (_cachedFfmpegPath != null)
        {
            return _cachedFfmpegPath;
        }

        _cachedFfmpegPath = await _ffmpegLocator.GetEffectiveFfmpegPathAsync(ct: cancellationToken);
        return _cachedFfmpegPath;
    }

    private FFmpegProgress? ParseProgress(string line)
    {
        // FFmpeg progress format: frame=  123 fps= 45 q=28.0 size=    1024kB time=00:00:05.12 bitrate=1638.4kbits/s speed=1.5x
        
        if (!line.Contains("frame=") || !line.Contains("time="))
        {
            return null;
        }

        try
        {
            var progress = new FFmpegProgress();
            
            // Parse frame
            var frameMatch = System.Text.RegularExpressions.Regex.Match(line, @"frame=\s*(\d+)");
            if (frameMatch.Success)
            {
                progress = progress with { Frame = int.Parse(frameMatch.Groups[1].Value) };
            }
            
            // Parse fps
            var fpsMatch = System.Text.RegularExpressions.Regex.Match(line, @"fps=\s*([\d.]+)");
            if (fpsMatch.Success)
            {
                progress = progress with { Fps = double.Parse(fpsMatch.Groups[1].Value) };
            }
            
            // Parse time
            var timeMatch = System.Text.RegularExpressions.Regex.Match(line, @"time=(\d+):(\d+):(\d+\.\d+)");
            if (timeMatch.Success)
            {
                var hours = int.Parse(timeMatch.Groups[1].Value);
                var minutes = int.Parse(timeMatch.Groups[2].Value);
                var seconds = double.Parse(timeMatch.Groups[3].Value);
                progress = progress with 
                { 
                    ProcessedDuration = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds) 
                };
            }
            
            // Parse speed
            var speedMatch = System.Text.RegularExpressions.Regex.Match(line, @"speed=\s*([\d.]+)x");
            if (speedMatch.Success)
            {
                progress = progress with { Speed = double.Parse(speedMatch.Groups[1].Value) };
            }
            
            return progress;
        }
        catch
        {
            return null;
        }
    }

    private VideoInfo ParseVideoInfo(string ffmpegOutput, string filePath)
    {
        var info = new VideoInfo();
        
        try
        {
            // Parse duration
            var durationMatch = System.Text.RegularExpressions.Regex.Match(ffmpegOutput, @"Duration: (\d+):(\d+):(\d+\.\d+)");
            if (durationMatch.Success)
            {
                var hours = int.Parse(durationMatch.Groups[1].Value);
                var minutes = int.Parse(durationMatch.Groups[2].Value);
                var seconds = double.Parse(durationMatch.Groups[3].Value);
                info = info with 
                { 
                    Duration = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds) 
                };
            }
            
            // Parse video stream info (resolution, codec, fps)
            var videoMatch = System.Text.RegularExpressions.Regex.Match(
                ffmpegOutput, 
                @"Stream.*Video: (\w+).*?(\d+)x(\d+).*?([\d.]+) fps"
            );
            if (videoMatch.Success)
            {
                info = info with
                {
                    VideoCodec = videoMatch.Groups[1].Value,
                    Width = int.Parse(videoMatch.Groups[2].Value),
                    Height = int.Parse(videoMatch.Groups[3].Value),
                    FrameRate = double.Parse(videoMatch.Groups[4].Value)
                };
            }
            
            // Parse audio codec
            var audioMatch = System.Text.RegularExpressions.Regex.Match(ffmpegOutput, @"Stream.*Audio: (\w+)");
            if (audioMatch.Success)
            {
                info = info with { AudioCodec = audioMatch.Groups[1].Value };
            }
            
            // Parse bitrate
            var bitrateMatch = System.Text.RegularExpressions.Regex.Match(ffmpegOutput, @"bitrate: (\d+) kb/s");
            if (bitrateMatch.Success)
            {
                info = info with { BitRate = long.Parse(bitrateMatch.Groups[1].Value) * 1000 };
            }
            
            // Get file size
            if (File.Exists(filePath))
            {
                info = info with { FileSize = new FileInfo(filePath).Length };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error parsing video info from FFmpeg output");
        }
        
        return info;
    }
}
