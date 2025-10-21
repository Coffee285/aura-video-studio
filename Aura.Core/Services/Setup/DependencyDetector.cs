using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Aura.Core.Dependencies;
using Microsoft.Extensions.Logging;

namespace Aura.Core.Services.Setup;

public enum RecommendedTier
{
    Free,
    Balanced,
    Pro
}

public record DependencyStatus(
    bool FFmpegInstalled,
    string? FFmpegVersion,
    bool PiperTtsInstalled,
    string? PiperTtsPath,
    bool OllamaInstalled,
    string? OllamaVersion,
    bool NvidiaDriversInstalled,
    string? NvidiaDriverVersion,
    double DiskSpaceGB,
    bool InternetConnected,
    bool FFmpegInstallationRequired,
    bool PiperTtsInstallationRequired,
    bool OllamaInstallationRequired,
    RecommendedTier FFmpegRecommendedTier,
    RecommendedTier PiperTtsRecommendedTier,
    RecommendedTier OllamaRecommendedTier
);

public class DependencyDetector
{
    private readonly ILogger<DependencyDetector> _logger;
    private readonly FfmpegLocator? _ffmpegLocator;
    private readonly HttpClient? _httpClient;

    public DependencyDetector(
        ILogger<DependencyDetector> logger,
        FfmpegLocator? ffmpegLocator = null,
        HttpClient? httpClient = null)
    {
        _logger = logger;
        _ffmpegLocator = ffmpegLocator;
        _httpClient = httpClient;
    }

    public async Task<DependencyStatus> DetectAllDependenciesAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Starting dependency detection");

        // Detect FFmpeg
        var (ffmpegInstalled, ffmpegVersion) = await DetectFFmpegAsync(ct).ConfigureAwait(false);

        // Detect Piper TTS
        var (piperInstalled, piperPath) = DetectPiperTts();

        // Detect Ollama
        var (ollamaInstalled, ollamaVersion) = await DetectOllamaAsync(ct).ConfigureAwait(false);

        // Detect NVIDIA drivers
        var (nvidiaInstalled, nvidiaVersion) = DetectNvidiaDrivers();

        // Check disk space
        var diskSpace = GetAvailableDiskSpaceGB();

        // Check internet connectivity
        var internetConnected = await CheckInternetConnectivityAsync(ct).ConfigureAwait(false);

        return new DependencyStatus(
            FFmpegInstalled: ffmpegInstalled,
            FFmpegVersion: ffmpegVersion,
            PiperTtsInstalled: piperInstalled,
            PiperTtsPath: piperPath,
            OllamaInstalled: ollamaInstalled,
            OllamaVersion: ollamaVersion,
            NvidiaDriversInstalled: nvidiaInstalled,
            NvidiaDriverVersion: nvidiaVersion,
            DiskSpaceGB: diskSpace,
            InternetConnected: internetConnected,
            FFmpegInstallationRequired: !ffmpegInstalled,
            PiperTtsInstallationRequired: !piperInstalled,
            OllamaInstallationRequired: !ollamaInstalled,
            FFmpegRecommendedTier: RecommendedTier.Free, // FFmpeg required for all tiers
            PiperTtsRecommendedTier: RecommendedTier.Balanced, // Piper for Balanced/Local tier
            OllamaRecommendedTier: RecommendedTier.Balanced // Ollama for Balanced/Local tier
        );
    }

    private async Task<(bool installed, string? version)> DetectFFmpegAsync(CancellationToken ct)
    {
        try
        {
            // Use FfmpegLocator if available
            if (_ffmpegLocator != null)
            {
                var result = await _ffmpegLocator.CheckAllCandidatesAsync(null, ct).ConfigureAwait(false);
                if (result.Found)
                {
                    _logger.LogInformation("FFmpeg found via FfmpegLocator: {Path}", result.FfmpegPath);
                    return (true, result.VersionString);
                }
            }

            // Fallback: Try running ffmpeg -version
            var startInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = "-version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return (false, null);
            }

            var output = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                var firstLine = output.Split('\n')[0];
                _logger.LogInformation("FFmpeg detected: {Version}", firstLine);
                return (true, firstLine);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "FFmpeg not found");
        }

        return (false, null);
    }

    private (bool installed, string? path) DetectPiperTts()
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            
            var candidatePaths = new[]
            {
                Path.Combine(localAppData, "Aura", "Tools", "piper", "piper.exe"),
                Path.Combine(localAppData, "Aura", "Tools", "piper", "piper"),
                Path.Combine(localAppData, "Aura", "piper", "piper.exe"),
                Path.Combine(localAppData, "Aura", "piper", "piper"),
                Path.Combine(programFiles, "Piper", "piper.exe"),
                Path.Combine(programFiles, "Piper", "piper"),
            };

            foreach (var path in candidatePaths)
            {
                if (File.Exists(path))
                {
                    _logger.LogInformation("Piper TTS found at: {Path}", path);
                    return (true, path);
                }
            }

            // Check if piper is in PATH
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                var pathDirs = pathEnv.Split(Path.PathSeparator);
                foreach (var dir in pathDirs)
                {
                    var piperExe = Path.Combine(dir, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "piper.exe" : "piper");
                    if (File.Exists(piperExe))
                    {
                        _logger.LogInformation("Piper TTS found in PATH: {Path}", piperExe);
                        return (true, piperExe);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error detecting Piper TTS");
        }

        return (false, null);
    }

    private async Task<(bool installed, string? version)> DetectOllamaAsync(CancellationToken ct)
    {
        try
        {
            if (_httpClient == null)
            {
                return (false, null);
            }

            var response = await _httpClient.GetAsync("http://localhost:11434/api/tags", ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Ollama detected and responding");
                
                // Try to get version from another endpoint
                try
                {
                    var versionResponse = await _httpClient.GetAsync("http://localhost:11434/api/version", ct).ConfigureAwait(false);
                    if (versionResponse.IsSuccessStatusCode)
                    {
                        var version = await versionResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        return (true, version);
                    }
                }
                catch
                {
                    // Version endpoint might not exist in older versions
                }

                return (true, "running");
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Ollama not detected");
        }

        return (false, null);
    }

    private (bool installed, string? version) DetectNvidiaDrivers()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-gpu=driver_version --format=csv,noheader",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return (false, null);
            }

            process.WaitForExit(5000);

            if (process.ExitCode == 0)
            {
                var output = process.StandardOutput.ReadToEnd().Trim();
                if (!string.IsNullOrWhiteSpace(output))
                {
                    _logger.LogInformation("NVIDIA drivers detected: {Version}", output);
                    return (true, output);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "NVIDIA drivers not detected");
        }

        return (false, null);
    }

    private double GetAvailableDiskSpaceGB()
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var driveInfo = new DriveInfo(Path.GetPathRoot(localAppData) ?? "C:\\");
            var availableGB = driveInfo.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
            _logger.LogInformation("Available disk space: {Space:F2} GB", availableGB);
            return availableGB;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not determine disk space");
            return 0;
        }
    }

    private async Task<bool> CheckInternetConnectivityAsync(CancellationToken ct)
    {
        try
        {
            if (_httpClient == null)
            {
                return false;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            var response = await _httpClient.GetAsync("https://github.com", cts.Token).ConfigureAwait(false);
            var connected = response.IsSuccessStatusCode;
            _logger.LogInformation("Internet connectivity: {Connected}", connected);
            return connected;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Internet connectivity check failed");
            return false;
        }
    }
}
