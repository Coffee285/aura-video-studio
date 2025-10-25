using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aura.Api.Models;
using Aura.Api.Services;
using Aura.Core.Services;
using Aura.Core.Services.Setup;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Aura.Api.Controllers;

[ApiController]
[Route("api/setup")]
public class SetupController : ControllerBase
{
    private readonly ILogger<SetupController> _logger;
    private readonly DependencyDetector _detector;
    private readonly DependencyInstaller _installer;
    private readonly SseService _sseService;
    private readonly HttpClient _httpClient;
    private readonly ApiKeyValidationService _validationService;
    private readonly ISecureStorageService _secureStorage;

    public SetupController(
        ILogger<SetupController> logger,
        DependencyDetector detector,
        DependencyInstaller installer,
        SseService sseService,
        HttpClient httpClient,
        ApiKeyValidationService validationService,
        ISecureStorageService secureStorage)
    {
        _logger = logger;
        _detector = detector;
        _installer = installer;
        _sseService = sseService;
        _httpClient = httpClient;
        _validationService = validationService;
        _secureStorage = secureStorage;
    }

    [HttpGet("detect")]
    public async Task<IActionResult> Detect(CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Detecting dependencies");
            var status = await _detector.DetectAllDependenciesAsync(ct).ConfigureAwait(false);
            return Ok(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to detect dependencies");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("install/ffmpeg")]
    public async Task InstallFFmpeg(CancellationToken ct)
    {
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        try
        {
            _logger.LogInformation("Starting FFmpeg installation via SSE");

            var progress = new Progress<InstallProgress>(p =>
            {
                var json = JsonSerializer.Serialize(p);
                Response.WriteAsync($"event: progress\ndata: {json}\n\n", ct).GetAwaiter().GetResult();
                Response.Body.FlushAsync(ct).GetAwaiter().GetResult();
            });

            var success = await _installer.InstallFFmpegAsync(progress, ct).ConfigureAwait(false);

            var completionData = JsonSerializer.Serialize(new { success });
            await Response.WriteAsync($"event: complete\ndata: {completionData}\n\n", ct).ConfigureAwait(false);
            await Response.Body.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FFmpeg installation failed");
            var errorData = JsonSerializer.Serialize(new { error = ex.Message });
            await Response.WriteAsync($"event: error\ndata: {errorData}\n\n", ct).ConfigureAwait(false);
            await Response.Body.FlushAsync(ct).ConfigureAwait(false);
        }
    }

    [HttpPost("install/piper")]
    public async Task InstallPiper(CancellationToken ct)
    {
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        try
        {
            _logger.LogInformation("Starting Piper TTS installation via SSE");

            var progress = new Progress<InstallProgress>(p =>
            {
                var json = JsonSerializer.Serialize(p);
                Response.WriteAsync($"event: progress\ndata: {json}\n\n", ct).GetAwaiter().GetResult();
                Response.Body.FlushAsync(ct).GetAwaiter().GetResult();
            });

            var success = await _installer.InstallPiperTtsAsync(progress, ct).ConfigureAwait(false);

            var completionData = JsonSerializer.Serialize(new { success });
            await Response.WriteAsync($"event: complete\ndata: {completionData}\n\n", ct).ConfigureAwait(false);
            await Response.Body.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Piper TTS installation failed");
            var errorData = JsonSerializer.Serialize(new { error = ex.Message });
            await Response.WriteAsync($"event: error\ndata: {errorData}\n\n", ct).ConfigureAwait(false);
            await Response.Body.FlushAsync(ct).ConfigureAwait(false);
        }
    }

    [HttpPost("install/all")]
    public async Task InstallAll(CancellationToken ct)
    {
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        try
        {
            _logger.LogInformation("Starting installation of all dependencies via SSE");

            // Detect what needs to be installed
            var status = await _detector.DetectAllDependenciesAsync(ct).ConfigureAwait(false);

            var overallProgress = new Progress<InstallProgress>(p =>
            {
                var json = JsonSerializer.Serialize(p);
                Response.WriteAsync($"event: progress\ndata: {json}\n\n", ct).GetAwaiter().GetResult();
                Response.Body.FlushAsync(ct).GetAwaiter().GetResult();
            });

            bool allSuccess = true;

            // Install FFmpeg if needed
            if (status.FFmpegInstallationRequired)
            {
                ((IProgress<InstallProgress>)overallProgress).Report(new InstallProgress(0, "Installing FFmpeg...", "", 0, 0));
                var ffmpegSuccess = await _installer.InstallFFmpegAsync(overallProgress, ct).ConfigureAwait(false);
                if (!ffmpegSuccess)
                {
                    allSuccess = false;
                }
            }

            // Install Piper TTS if needed
            if (allSuccess && status.PiperTtsInstallationRequired)
            {
                ((IProgress<InstallProgress>)overallProgress).Report(new InstallProgress(50, "Installing Piper TTS...", "", 0, 0));
                var piperSuccess = await _installer.InstallPiperTtsAsync(overallProgress, ct).ConfigureAwait(false);
                if (!piperSuccess)
                {
                    allSuccess = false;
                }
            }

            var completionData = JsonSerializer.Serialize(new { success = allSuccess });
            await Response.WriteAsync($"event: complete\ndata: {completionData}\n\n", ct).ConfigureAwait(false);
            await Response.Body.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Installation of all dependencies failed");
            var errorData = JsonSerializer.Serialize(new { error = ex.Message });
            await Response.WriteAsync($"event: error\ndata: {errorData}\n\n", ct).ConfigureAwait(false);
            await Response.Body.FlushAsync(ct).ConfigureAwait(false);
        }
    }

    [HttpPost("validate-key")]
    public async Task<IActionResult> ValidateKey([FromBody] ValidateKeyRequest request, CancellationToken ct)
    {
        try
        {
            // Sanitize provider name for logging
            var sanitizedProvider = SanitizeForLogging(request.Provider);
            _logger.LogInformation("Validating API key for provider: {Provider}", sanitizedProvider);

            ValidationResult result;

            switch (request.Provider?.ToLowerInvariant())
            {
                case "openai":
                    result = await _validationService.ValidateOpenAIKeyAsync(request.ApiKey ?? "", ct).ConfigureAwait(false);
                    break;
                case "anthropic":
                case "claude":
                    result = await _validationService.ValidateAnthropicKeyAsync(request.ApiKey ?? "", ct).ConfigureAwait(false);
                    break;
                case "gemini":
                case "google":
                    result = await _validationService.ValidateGeminiKeyAsync(request.ApiKey ?? "", ct).ConfigureAwait(false);
                    break;
                case "elevenlabs":
                    result = await _validationService.ValidateElevenLabsKeyAsync(request.ApiKey ?? "", ct).ConfigureAwait(false);
                    break;
                case "playht":
                    // PlayHT requires both userId and secretKey - expect them in ApiKey field separated by ':'
                    var parts = request.ApiKey?.Split(':', 2);
                    if (parts?.Length == 2)
                    {
                        result = await _validationService.ValidatePlayHTKeyAsync(parts[0], parts[1], ct).ConfigureAwait(false);
                    }
                    else
                    {
                        result = ValidationResult.Failure("PlayHT requires both User ID and Secret Key", "INVALID_FORMAT", null, null);
                    }
                    break;
                case "replicate":
                    result = await _validationService.ValidateReplicateKeyAsync(request.ApiKey ?? "", ct).ConfigureAwait(false);
                    break;
                default:
                    return BadRequest(new { success = false, error = "Unknown provider" });
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate API key");
            return Ok(new { success = false, error = ex.Message });
        }
    }

    [HttpGet("disk-space")]
    public IActionResult GetDiskSpace()
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var driveInfo = new DriveInfo(Path.GetPathRoot(localAppData) ?? "C:\\");
            var availableGB = driveInfo.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
            return Ok(new { availableGB = Math.Round(availableGB, 2) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get disk space");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("save-config")]
    public async Task<IActionResult> SaveConfig([FromBody] SetupConfig config, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Saving setup configuration for tier: {Tier}", config.Tier);

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var configDir = Path.Combine(localAppData, "Aura");
            Directory.CreateDirectory(configDir);

            var configPath = Path.Combine(configDir, "config.json");

            // Encrypt API keys using Data Protection API (Windows) or simple encryption (Unix)
            var encryptedConfig = new
            {
                config.Tier,
                config.SetupCompleted,
                config.SetupVersion,
                ApiKeys = new
                {
                    OpenAI = EncryptString(config.ApiKeys?.OpenAI),
                    Gemini = EncryptString(config.ApiKeys?.Gemini),
                    ElevenLabs = EncryptString(config.ApiKeys?.ElevenLabs),
                    PlayHT = EncryptString(config.ApiKeys?.PlayHT)
                }
            };

            var json = JsonSerializer.Serialize(encryptedConfig, new JsonSerializerOptions { WriteIndented = true });
            await System.IO.File.WriteAllTextAsync(configPath, json, ct).ConfigureAwait(false);

            _logger.LogInformation("Setup configuration saved successfully");
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save setup configuration");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var configPath = Path.Combine(localAppData, "Aura", "config.json");
            
            var setupCompleted = System.IO.File.Exists(configPath);
            
            return Ok(new { setupCompleted });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get setup status");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Saves multiple API keys securely
    /// </summary>
    [HttpPost("save-api-keys")]
    public async Task<IActionResult> SaveApiKeys([FromBody] SaveApiKeysRequest request, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Saving API keys for {Count} providers", request.Keys?.Count ?? 0);

            if (request.Keys == null || request.Keys.Count == 0)
            {
                return BadRequest(new { success = false, error = "No API keys provided" });
            }

            foreach (var kvp in request.Keys)
            {
                if (!string.IsNullOrWhiteSpace(kvp.Value))
                {
                    await _secureStorage.SaveApiKeyAsync(kvp.Key, kvp.Value).ConfigureAwait(false);
                }
            }

            _logger.LogInformation("API keys saved successfully");
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save API keys");
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Returns which providers have API keys configured (without revealing the keys)
    /// </summary>
    [HttpGet("key-status")]
    public async Task<IActionResult> GetKeyStatus()
    {
        try
        {
            var configuredProviders = await _secureStorage.GetConfiguredProvidersAsync().ConfigureAwait(false);
            
            var status = new Dictionary<string, bool>
            {
                { "openai", configuredProviders.Contains("openai", StringComparer.OrdinalIgnoreCase) },
                { "anthropic", configuredProviders.Contains("anthropic", StringComparer.OrdinalIgnoreCase) },
                { "gemini", configuredProviders.Contains("gemini", StringComparer.OrdinalIgnoreCase) },
                { "elevenlabs", configuredProviders.Contains("elevenlabs", StringComparer.OrdinalIgnoreCase) },
                { "playht", configuredProviders.Contains("playht", StringComparer.OrdinalIgnoreCase) },
                { "replicate", configuredProviders.Contains("replicate", StringComparer.OrdinalIgnoreCase) }
            };

            return Ok(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get key status");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Returns provider information including signup URL, pricing, and documentation
    /// </summary>
    [HttpGet("provider-info/{provider}")]
    public IActionResult GetProviderInfo(string provider)
    {
        var info = provider?.ToLowerInvariant() switch
        {
            "openai" => new ProviderInfo
            {
                Name = "OpenAI",
                SignupUrl = "https://platform.openai.com/signup",
                PricingUrl = "https://openai.com/pricing",
                DocsUrl = "https://platform.openai.com/docs",
                Description = "GPT-4, GPT-3.5, and other language models for script generation",
                KeyFormat = "sk-..."
            },
            "anthropic" or "claude" => new ProviderInfo
            {
                Name = "Anthropic (Claude)",
                SignupUrl = "https://console.anthropic.com/",
                PricingUrl = "https://www.anthropic.com/pricing",
                DocsUrl = "https://docs.anthropic.com/",
                Description = "Claude AI models for advanced script generation",
                KeyFormat = "sk-ant-..."
            },
            "gemini" or "google" => new ProviderInfo
            {
                Name = "Google Gemini",
                SignupUrl = "https://makersuite.google.com/",
                PricingUrl = "https://ai.google.dev/pricing",
                DocsUrl = "https://ai.google.dev/docs",
                Description = "Google's Gemini models for script generation",
                KeyFormat = "AIza..."
            },
            "elevenlabs" => new ProviderInfo
            {
                Name = "ElevenLabs",
                SignupUrl = "https://elevenlabs.io/sign-up",
                PricingUrl = "https://elevenlabs.io/pricing",
                DocsUrl = "https://docs.elevenlabs.io/",
                Description = "High-quality text-to-speech with natural voices",
                KeyFormat = "..."
            },
            "playht" => new ProviderInfo
            {
                Name = "PlayHT",
                SignupUrl = "https://play.ht/",
                PricingUrl = "https://play.ht/pricing/",
                DocsUrl = "https://docs.play.ht/",
                Description = "AI voice generation for text-to-speech",
                KeyFormat = "User ID + Secret Key"
            },
            "replicate" => new ProviderInfo
            {
                Name = "Replicate",
                SignupUrl = "https://replicate.com/",
                PricingUrl = "https://replicate.com/pricing",
                DocsUrl = "https://replicate.com/docs",
                Description = "Run AI models for image and video generation",
                KeyFormat = "r8_..."
            },
            _ => null
        };

        if (info == null)
        {
            return NotFound(new { error = "Unknown provider" });
        }

        return Ok(info);
    }

    /// <summary>
    /// Deletes an API key for a specific provider
    /// </summary>
    [HttpDelete("delete-key/{provider}")]
    public async Task<IActionResult> DeleteApiKey(string provider)
    {
        try
        {
            var sanitizedProvider = SanitizeForLogging(provider);
            _logger.LogInformation("Deleting API key for provider: {Provider}", sanitizedProvider);
            await _secureStorage.DeleteApiKeyAsync(provider).ConfigureAwait(false);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            var sanitizedProvider = SanitizeForLogging(provider);
            _logger.LogError(ex, "Failed to delete API key for provider: {Provider}", sanitizedProvider);
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Sanitizes user input for safe logging to prevent log injection
    /// </summary>
    private static string? SanitizeForLogging(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return "[empty]";
        }

        // Remove newlines and control characters to prevent log forging
        return System.Text.RegularExpressions.Regex.Replace(input, @"[\r\n\t\x00-\x1F\x7F]", "");
    }

    private static string? EncryptString(string? plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
        {
            return null;
        }

        try
        {
            // Simple base64 encoding for now
            // In production, should use proper encryption with ASP.NET Core Data Protection API
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            return Convert.ToBase64String(plainBytes);
        }
        catch
        {
            return plainText; // Fallback to plaintext if encoding fails
        }
    }
}

public class ValidateKeyRequest
{
    public string? Provider { get; set; }
    public string? ApiKey { get; set; }
}

public class SaveApiKeysRequest
{
    public Dictionary<string, string>? Keys { get; set; }
}

public class ProviderInfo
{
    public string Name { get; set; } = string.Empty;
    public string SignupUrl { get; set; } = string.Empty;
    public string PricingUrl { get; set; } = string.Empty;
    public string DocsUrl { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string KeyFormat { get; set; } = string.Empty;
}

public class SetupConfig
{
    public string? Tier { get; set; }
    public bool SetupCompleted { get; set; }
    public string? SetupVersion { get; set; }
    public ApiKeysConfig? ApiKeys { get; set; }
}

public class ApiKeysConfig
{
    public string? OpenAI { get; set; }
    public string? Gemini { get; set; }
    public string? ElevenLabs { get; set; }
    public string? PlayHT { get; set; }
}
