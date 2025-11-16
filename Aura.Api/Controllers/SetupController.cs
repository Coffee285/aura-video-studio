using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Aura.Core.Data;

namespace Aura.Api.Controllers;

/// <summary>
/// Controller for handling desktop setup operations like dependency installation
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SetupController : ControllerBase
{
    private readonly ILogger<SetupController> _logger;
    private readonly IWebHostEnvironment _environment;
    private readonly HttpClient _httpClient;
    private readonly AuraDbContext _dbContext;

    public SetupController(
        ILogger<SetupController> logger,
        IWebHostEnvironment environment,
        IHttpClientFactory httpClientFactory,
        AuraDbContext dbContext)
    {
        _logger = logger;
        _environment = environment;
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.Timeout = TimeSpan.FromMinutes(10); // Long timeout for downloads
        _dbContext = dbContext;
    }

    /// <summary>
    /// Download and install FFmpeg for the current platform
    /// </summary>
    [HttpPost("install-ffmpeg")]
    public async Task<IActionResult> InstallFFmpeg(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting FFmpeg installation for platform: {Platform}", RuntimeInformation.OSDescription);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return await InstallFFmpegWindows(cancellationToken).ConfigureAwait(false);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return Ok(new
                {
                    success = false,
                    message = "Please install FFmpeg using Homebrew: brew install ffmpeg",
                    command = "brew install ffmpeg",
                    url = "https://formulae.brew.sh/formula/ffmpeg"
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return Ok(new
                {
                    success = false,
                    message = "Please install FFmpeg using your package manager",
                    commands = new[]
                    {
                        "Ubuntu/Debian: sudo apt-get install ffmpeg",
                        "Fedora: sudo dnf install ffmpeg",
                        "Arch: sudo pacman -S ffmpeg"
                    },
                    url = "https://ffmpeg.org/download.html#build-linux"
                });
            }

            return BadRequest(new { error = "Unsupported platform" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install FFmpeg");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    private async Task<IActionResult> InstallFFmpegWindows(CancellationToken cancellationToken)
    {
        try
        {
            // Use the official FFmpeg Windows builds from gyan.dev (trusted by the community)
            var downloadUrl = RuntimeInformation.OSArchitecture == Architecture.X64
                ? "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"
                : "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";

            var dataPath = Environment.GetEnvironmentVariable("AURA_DATA_PATH") ?? 
                           Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AuraVideoStudio");
            var ffmpegDir = Path.Combine(dataPath, "ffmpeg");
            var downloadPath = Path.Combine(Path.GetTempPath(), "ffmpeg.zip");

            Directory.CreateDirectory(ffmpegDir);

            _logger.LogInformation("Downloading FFmpeg from {Url}", downloadUrl);

            // Download FFmpeg with progress reporting
            using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                var bytesDownloaded = 0L;

                using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                using (var fileStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    var buffer = new byte[8192];
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
                        bytesDownloaded += bytesRead;

                        if (totalBytes > 0)
                        {
                            var progress = (int)((bytesDownloaded * 100) / totalBytes);
                            _logger.LogInformation("Download progress: {Progress}%", progress);
                        }
                    }
                }
            }

            _logger.LogInformation("Extracting FFmpeg to {Path}", ffmpegDir);

            // Extract ZIP
            ZipFile.ExtractToDirectory(downloadPath, ffmpegDir, overwriteFiles: true);

            // Find the ffmpeg.exe in the extracted directory (it's usually in a subdirectory)
            var ffmpegExePath = Directory.GetFiles(ffmpegDir, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();

            if (ffmpegExePath == null)
            {
                throw new FileNotFoundException("FFmpeg.exe not found in extracted files");
            }

            // Move ffmpeg.exe to the root of ffmpegDir for easier access
            var targetPath = Path.Combine(ffmpegDir, "ffmpeg.exe");
            if (ffmpegExePath != targetPath)
            {
                System.IO.File.Copy(ffmpegExePath, targetPath, overwrite: true);
            }

            // Also copy ffprobe.exe
            var ffprobeExePath = Directory.GetFiles(ffmpegDir, "ffprobe.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (ffprobeExePath != null)
            {
                var ffprobeTargetPath = Path.Combine(ffmpegDir, "ffprobe.exe");
                if (ffprobeExePath != ffprobeTargetPath)
                {
                    System.IO.File.Copy(ffprobeExePath, ffprobeTargetPath, overwrite: true);
                }
            }

            // Cleanup
            System.IO.File.Delete(downloadPath);

            _logger.LogInformation("FFmpeg installed successfully at {Path}", targetPath);

            // Update PATH environment variable for the current process
            var currentPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process) ?? "";
            if (!currentPath.Contains(ffmpegDir))
            {
                Environment.SetEnvironmentVariable("PATH", $"{currentPath};{ffmpegDir}", EnvironmentVariableTarget.Process);
            }

            // Verify installation
            var version = await GetFFmpegVersion(targetPath).ConfigureAwait(false);

            return Ok(new
            {
                success = true,
                message = "FFmpeg installed successfully",
                path = targetPath,
                version = version
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install FFmpeg on Windows");
            throw;
        }
    }

    private async Task<string?> GetFFmpegVersion(string ffmpegPath)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = "-version",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);

            // Parse version from first line (e.g., "ffmpeg version 6.0 Copyright...")
            var firstLine = output.Split('\n').FirstOrDefault() ?? "";
            var versionMatch = System.Text.RegularExpressions.Regex.Match(firstLine, @"ffmpeg version ([\d.]+)");
            return versionMatch.Success ? versionMatch.Groups[1].Value : "unknown";
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Check Ollama availability
    /// </summary>
    [HttpGet("ollama-status")]
    public async Task<IActionResult> GetOllamaStatus(CancellationToken cancellationToken)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var response = await client.GetAsync("http://localhost:11434/api/tags", cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return Ok(new
                {
                    available = true,
                    url = "http://localhost:11434",
                    models = content
                });
            }

            return Ok(new { available = false });
        }
        catch
        {
            return Ok(new { available = false });
        }
    }

    /// <summary>
    /// Get system information for setup wizard
    /// </summary>
    [HttpGet("system-info")]
    public IActionResult GetSystemInfo()
    {
        return Ok(new
        {
            platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows" :
                      RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macOS" :
                      RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "Linux" : "Unknown",
            architecture = RuntimeInformation.OSArchitecture.ToString(),
            frameworkDescription = RuntimeInformation.FrameworkDescription,
            processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            osDescription = RuntimeInformation.OSDescription,
            paths = new
            {
                userData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                temp = Path.GetTempPath()
            }
        });
    }

    /// <summary>
    /// Validate system requirements
    /// </summary>
    [HttpGet("validate-requirements")]
    public IActionResult ValidateRequirements()
    {
        var requirements = new List<object>();

        // Check RAM
        // Note: This is a simplified check. In production, use performance counters
        var totalMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var totalMemoryGB = totalMemory / (1024.0 * 1024.0 * 1024.0);
        requirements.Add(new
        {
            name = "RAM",
            required = "8 GB",
            detected = $"{totalMemoryGB:F1} GB",
            status = totalMemoryGB >= 8 ? "pass" : "warning",
            message = totalMemoryGB < 8 ? "8GB+ RAM recommended for video generation" : null
        });

        // Check disk space
        var dataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var drive = new DriveInfo(Path.GetPathRoot(dataPath) ?? "C:\\");
        var freeSpaceGB = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
        requirements.Add(new
        {
            name = "Disk Space",
            required = "10 GB",
            detected = $"{freeSpaceGB:F1} GB free",
            status = freeSpaceGB >= 10 ? "pass" : "warning",
            message = freeSpaceGB < 10 ? "10GB+ free space recommended" : null
        });

        // Check .NET version
        requirements.Add(new
        {
            name = ".NET Runtime",
            required = ".NET 8.0",
            detected = Environment.Version.ToString(),
            status = "pass"
        });

        var allPass = requirements.All(r => ((dynamic)r).status == "pass");

        return Ok(new
        {
            allRequirementsMet = allPass,
            requirements = requirements
        });
    }

    /// <summary>
    /// Get current system setup status
    /// </summary>
    [HttpGet("system-status")]
    public async Task<IActionResult> GetSystemStatus(CancellationToken cancellationToken)
    {
        try
        {
            // Query the database for setup status (default user)
            var userSetup = await _dbContext.UserSetups
                .FirstOrDefaultAsync(u => u.UserId == "default", cancellationToken).ConfigureAwait(false);

            if (userSetup == null || !userSetup.Completed)
            {
                return Ok(new
                {
                    isComplete = false,
                    ffmpegPath = (string?)null,
                    outputDirectory = (string?)null
                });
            }

            // Parse wizard state JSON for paths if available
            string? ffmpegPath = null;
            string? outputDirectory = null;

            if (!string.IsNullOrEmpty(userSetup.WizardState))
            {
                try
                {
                    var state = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(userSetup.WizardState);
                    if (state != null)
                    {
                        if (state.TryGetValue("ffmpegPath", out var ffPath))
                        {
                            ffmpegPath = ffPath?.ToString();
                        }
                        if (state.TryGetValue("outputDirectory", out var outDir))
                        {
                            outputDirectory = outDir?.ToString();
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse wizard state JSON");
                }
            }

            return Ok(new
            {
                isComplete = true,
                ffmpegPath = ffmpegPath,
                outputDirectory = outputDirectory
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get system status");
            return StatusCode(500, new { error = "Failed to retrieve system status", detail = ex.Message });
        }
    }

    /// <summary>
    /// Complete the first-run setup wizard
    /// </summary>
    [HttpPost("complete")]
    public async Task<IActionResult> CompleteSetup(
        [FromBody] SetupCompleteRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var errors = new List<string>();

            // Validate output directory if provided
            if (!string.IsNullOrEmpty(request.OutputDirectory))
            {
                if (!Directory.Exists(request.OutputDirectory))
                {
                    errors.Add($"Output directory does not exist: {request.OutputDirectory}");
                }
                else
                {
                    try
                    {
                        // Test write permissions
                        var testFile = Path.Combine(request.OutputDirectory, $".aura-test-{Guid.NewGuid()}.tmp");
                        await System.IO.File.WriteAllTextAsync(testFile, "test", cancellationToken).ConfigureAwait(false);
                        System.IO.File.Delete(testFile);
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Output directory is not writable: {ex.Message}");
                    }
                }
            }

            // Validate FFmpeg path if provided (allow null for patience policy)
            if (!string.IsNullOrEmpty(request.FFmpegPath))
            {
                if (!System.IO.File.Exists(request.FFmpegPath))
                {
                    errors.Add($"FFmpeg executable not found at: {request.FFmpegPath}");
                }
                else
                {
                    // Test FFmpeg execution
                    try
                    {
                        var process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = request.FFmpegPath,
                                Arguments = "-version",
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            }
                        };
                        process.Start();
                        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                        
                        if (process.ExitCode != 0)
                        {
                            errors.Add("FFmpeg validation failed: executable returned error");
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"FFmpeg validation failed: {ex.Message}");
                    }
                }
            }

            if (errors.Any())
            {
                return Ok(new { success = false, errors = errors.ToArray() });
            }

            // Find or create user setup record
            var userSetup = await _dbContext.UserSetups
                .FirstOrDefaultAsync(u => u.UserId == "default", cancellationToken).ConfigureAwait(false);

            if (userSetup == null)
            {
                userSetup = new UserSetupEntity
                {
                    UserId = "default",
                    Completed = true,
                    CompletedAt = DateTime.UtcNow,
                    Version = "1.0.0",
                    LastStep = 6, // Final step
                    UpdatedAt = DateTime.UtcNow
                };
                _dbContext.UserSetups.Add(userSetup);
            }
            else
            {
                userSetup.Completed = true;
                userSetup.CompletedAt = DateTime.UtcNow;
                userSetup.Version = "1.0.0";
                userSetup.LastStep = 6;
                userSetup.UpdatedAt = DateTime.UtcNow;
            }

            // Store paths in wizard state JSON
            var wizardState = new Dictionary<string, object?>();
            if (!string.IsNullOrEmpty(request.FFmpegPath))
            {
                wizardState["ffmpegPath"] = request.FFmpegPath;
            }
            if (!string.IsNullOrEmpty(request.OutputDirectory))
            {
                wizardState["outputDirectory"] = request.OutputDirectory;
            }
            userSetup.WizardState = System.Text.Json.JsonSerializer.Serialize(wizardState);

            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("First-run setup completed for user 'default'");

            return Ok(new { success = true, errors = Array.Empty<string>() });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to complete setup");
            return StatusCode(500, new { error = "Failed to complete setup", detail = ex.Message });
        }
    }

    /// <summary>
    /// Check if a directory is valid and writable
    /// </summary>
    [HttpPost("check-directory")]
    public async Task<IActionResult> CheckDirectory(
        [FromBody] DirectoryCheckRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrEmpty(request.Path))
            {
                return Ok(new { isValid = false, error = "Path cannot be empty" });
            }

            if (!Directory.Exists(request.Path))
            {
                return Ok(new { isValid = false, error = "Directory does not exist" });
            }

            // Test write permissions
            try
            {
                var testFile = Path.Combine(request.Path, $".aura-test-{Guid.NewGuid()}.tmp");
                await System.IO.File.WriteAllTextAsync(testFile, "test", cancellationToken).ConfigureAwait(false);
                System.IO.File.Delete(testFile);
            }
            catch (Exception ex)
            {
                return Ok(new { isValid = false, error = $"Directory is not writable: {ex.Message}" });
            }

            return Ok(new { isValid = true, error = (string?)null });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check directory");
            return Ok(new { isValid = false, error = $"Failed to check directory: {ex.Message}" });
        }
    }

    /// <summary>
    /// Save wizard progress (for resume on interruption)
    /// </summary>
    [HttpPost("wizard/save-progress")]
    public async Task<IActionResult> SaveWizardProgress(
        [FromBody] WizardProgressRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Saving wizard progress for user: {UserId}, Step: {Step}, CorrelationId: {CorrelationId}",
                request.UserId ?? "default", request.CurrentStep, request.CorrelationId);

            var userId = request.UserId ?? "default";
            var userSetup = await _dbContext.UserSetups
                .FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken).ConfigureAwait(false);

            if (userSetup == null)
            {
                userSetup = new UserSetupEntity
                {
                    UserId = userId,
                    LastStep = request.CurrentStep,
                    UpdatedAt = DateTime.UtcNow,
                    WizardState = System.Text.Json.JsonSerializer.Serialize(request.State)
                };
                _dbContext.UserSetups.Add(userSetup);
            }
            else
            {
                userSetup.LastStep = request.CurrentStep;
                userSetup.UpdatedAt = DateTime.UtcNow;
                userSetup.WizardState = System.Text.Json.JsonSerializer.Serialize(request.State);
            }

            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return Ok(new
            {
                success = true,
                message = "Wizard progress saved successfully",
                correlationId = request.CorrelationId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save wizard progress, CorrelationId: {CorrelationId}",
                request.CorrelationId);
            return StatusCode(500, new
            {
                success = false,
                error = "Failed to save wizard progress",
                detail = ex.Message,
                correlationId = request.CorrelationId
            });
        }
    }

    /// <summary>
    /// Get wizard status and saved progress
    /// </summary>
    [HttpGet("wizard/status")]
    public async Task<IActionResult> GetWizardStatus(
        [FromQuery] string? userId,
        CancellationToken cancellationToken)
    {
        try
        {
            var effectiveUserId = userId ?? "default";
            var userSetup = await _dbContext.UserSetups
                .FirstOrDefaultAsync(u => u.UserId == effectiveUserId, cancellationToken).ConfigureAwait(false);

            if (userSetup == null)
            {
                return Ok(new
                {
                    completed = false,
                    currentStep = 0,
                    state = (object?)null,
                    canResume = false,
                    lastUpdated = (DateTime?)null
                });
            }

            object? parsedState = null;
            if (!string.IsNullOrEmpty(userSetup.WizardState))
            {
                try
                {
                    parsedState = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(userSetup.WizardState);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse wizard state for user: {UserId}", effectiveUserId);
                }
            }

            return Ok(new
            {
                completed = userSetup.Completed,
                currentStep = userSetup.LastStep,
                state = parsedState,
                canResume = !userSetup.Completed && userSetup.LastStep > 0,
                lastUpdated = userSetup.UpdatedAt,
                completedAt = userSetup.CompletedAt,
                version = userSetup.Version
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get wizard status for user: {UserId}", userId);
            return StatusCode(500, new
            {
                error = "Failed to retrieve wizard status",
                detail = ex.Message
            });
        }
    }

    /// <summary>
    /// Mark wizard as complete
    /// </summary>
    [HttpPost("wizard/complete")]
    public async Task<IActionResult> CompleteWizard(
        [FromBody] WizardCompleteRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Completing wizard for user: {UserId}, CorrelationId: {CorrelationId}",
                request.UserId ?? "default", request.CorrelationId);

            var userId = request.UserId ?? "default";
            var userSetup = await _dbContext.UserSetups
                .FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken).ConfigureAwait(false);

            if (userSetup == null)
            {
                userSetup = new UserSetupEntity
                {
                    UserId = userId,
                    Completed = true,
                    CompletedAt = DateTime.UtcNow,
                    Version = request.Version ?? "1.0.0",
                    LastStep = request.FinalStep,
                    UpdatedAt = DateTime.UtcNow,
                    SelectedTier = request.SelectedTier,
                    WizardState = System.Text.Json.JsonSerializer.Serialize(request.FinalState ?? new())
                };
                _dbContext.UserSetups.Add(userSetup);
            }
            else
            {
                userSetup.Completed = true;
                userSetup.CompletedAt = DateTime.UtcNow;
                userSetup.Version = request.Version ?? "1.0.0";
                userSetup.LastStep = request.FinalStep;
                userSetup.UpdatedAt = DateTime.UtcNow;
                userSetup.SelectedTier = request.SelectedTier;
                userSetup.WizardState = System.Text.Json.JsonSerializer.Serialize(request.FinalState ?? new());
            }

            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Wizard completed successfully for user: {UserId}", userId);

            return Ok(new
            {
                success = true,
                message = "Wizard completed successfully",
                correlationId = request.CorrelationId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to complete wizard, CorrelationId: {CorrelationId}",
                request.CorrelationId);
            return StatusCode(500, new
            {
                success = false,
                error = "Failed to complete wizard",
                detail = ex.Message,
                correlationId = request.CorrelationId
            });
        }
    }

    /// <summary>
    /// Reset wizard state (for testing or re-running wizard)
    /// </summary>
    [HttpPost("wizard/reset")]
    public async Task<IActionResult> ResetWizard(
        [FromBody] WizardResetRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogWarning("Resetting wizard for user: {UserId}, CorrelationId: {CorrelationId}",
                request.UserId ?? "default", request.CorrelationId);

            var userId = request.UserId ?? "default";
            var userSetup = await _dbContext.UserSetups
                .FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken).ConfigureAwait(false);

            if (userSetup != null)
            {
                if (request.PreserveData)
                {
                    userSetup.Completed = false;
                    userSetup.CompletedAt = null;
                    userSetup.LastStep = 0;
                    userSetup.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    _dbContext.UserSetups.Remove(userSetup);
                }

                await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            _logger.LogInformation("Wizard reset successfully for user: {UserId}", userId);

            return Ok(new
            {
                success = true,
                message = "Wizard reset successfully",
                correlationId = request.CorrelationId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset wizard, CorrelationId: {CorrelationId}",
                request.CorrelationId);
            return StatusCode(500, new
            {
                success = false,
                error = "Failed to reset wizard",
                detail = ex.Message,
                correlationId = request.CorrelationId
            });
        }
    }

    /// <summary>
    /// Request models for setup endpoints
    /// </summary>
    public class SetupCompleteRequest
    {
        public string? FFmpegPath { get; set; }
        public string? OutputDirectory { get; set; }
    }

    public class DirectoryCheckRequest
    {
        public required string Path { get; set; }
    }

    public class WizardProgressRequest
    {
        public string? UserId { get; set; }
        public int CurrentStep { get; set; }
        public Dictionary<string, object> State { get; set; } = new();
        public string? CorrelationId { get; set; }
    }

    public class WizardCompleteRequest
    {
        public string? UserId { get; set; }
        public int FinalStep { get; set; }
        public string? Version { get; set; }
        public string? SelectedTier { get; set; }
        public Dictionary<string, object>? FinalState { get; set; }
        public string? CorrelationId { get; set; }
    }

    public class WizardResetRequest
    {
        public string? UserId { get; set; }
        public bool PreserveData { get; set; } = false;
        public string? CorrelationId { get; set; }
    }

    /// <summary>
    /// Get configuration status for the application
    /// </summary>
    [HttpGet("configuration-status")]
    public async Task<IActionResult> GetConfigurationStatus(CancellationToken cancellationToken)
    {
        try
        {
            var correlationId = HttpContext.TraceIdentifier;
            _logger.LogInformation("Getting configuration status, CorrelationId: {CorrelationId}", correlationId);

            var status = new
            {
                isConfigured = false,
                lastChecked = DateTime.UtcNow.ToString("o"),
                checks = new
                {
                    providerConfigured = false,
                    providerValidated = false,
                    workspaceCreated = true,
                    ffmpegDetected = false,
                    apiKeysValid = false
                },
                details = new
                {
                    configuredProviders = new List<string>(),
                    ffmpegPath = (string?)null,
                    ffmpegVersion = (string?)null,
                    workspacePath = (string?)null,
                    diskSpaceAvailable = 0,
                    gpuAvailable = false
                },
                issues = new List<object>()
            };

            await Task.CompletedTask.ConfigureAwait(false);
            return Ok(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting configuration status");
            return StatusCode(500, new ProblemDetails
            {
                Title = "Error getting configuration status",
                Status = 500,
                Detail = ex.Message,
                Extensions = { ["correlationId"] = HttpContext.TraceIdentifier }
            });
        }
    }
}
