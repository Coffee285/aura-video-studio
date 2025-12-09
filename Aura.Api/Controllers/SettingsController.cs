using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aura.Core.Models;
using Aura.Core.Services.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Aura.Api.Controllers;

/// <summary>
/// Controller for managing application settings and preferences
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly ILogger<SettingsController> _logger;
    private readonly ISettingsService _settingsService;

    public SettingsController(
        ILogger<SettingsController> logger,
        ISettingsService settingsService)
    {
        _logger = logger;
        _settingsService = settingsService;
    }

    /// <summary>
    /// Get all user settings
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(UserSettings), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSettings(CancellationToken ct)
    {
        try
        {
            var settings = await _settingsService.GetSettingsAsync(ct).ConfigureAwait(false);
            return Ok(settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get settings");
            return StatusCode(500, new { error = "Failed to retrieve settings" });
        }
    }

    /// <summary>
    /// Update user settings
    /// </summary>
    [HttpPut]
    [ProducesResponseType(typeof(SettingsUpdateResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateSettings([FromBody] UserSettings settings, CancellationToken ct)
    {
        try
        {
            var result = await _settingsService.UpdateSettingsAsync(settings, ct).ConfigureAwait(false);
            
            if (result.Success)
            {
                return Ok(result);
            }
            else
            {
                return BadRequest(result);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update settings");
            return StatusCode(500, new { error = "Failed to update settings" });
        }
    }

    /// <summary>
    /// Reset settings to defaults
    /// </summary>
    [HttpPost("reset")]
    [ProducesResponseType(typeof(SettingsUpdateResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResetSettings(CancellationToken ct)
    {
        try
        {
            var result = await _settingsService.ResetToDefaultsAsync(ct).ConfigureAwait(false);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset settings");
            return StatusCode(500, new { error = "Failed to reset settings" });
        }
    }

    /// <summary>
    /// Get general settings section
    /// </summary>
    [HttpGet("general")]
    [ProducesResponseType(typeof(GeneralSettings), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetGeneralSettings(CancellationToken ct)
    {
        try
        {
            var settings = await _settingsService.GetSettingsSectionAsync<GeneralSettings>(ct).ConfigureAwait(false);
            return Ok(settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get general settings");
            return StatusCode(500, new { error = "Failed to retrieve general settings" });
        }
    }

    /// <summary>
    /// Update general settings section
    /// </summary>
    [HttpPut("general")]
    [ProducesResponseType(typeof(SettingsUpdateResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateGeneralSettings([FromBody] GeneralSettings settings, CancellationToken ct)
    {
        try
        {
            var result = await _settingsService.UpdateSettingsSectionAsync(settings, ct).ConfigureAwait(false);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update general settings");
            return StatusCode(500, new { error = "Failed to update general settings" });
        }
    }

    /// <summary>
    /// Validate settings
    /// </summary>
    [HttpPost("validate")]
    [ProducesResponseType(typeof(SettingsValidationResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ValidateSettings([FromBody] UserSettings settings, CancellationToken ct)
    {
        try
        {
            var result = await _settingsService.ValidateSettingsAsync(settings, ct).ConfigureAwait(false);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate settings");
            return StatusCode(500, new { error = "Failed to validate settings" });
        }
    }

    /// <summary>
    /// Export settings to JSON
    /// </summary>
    [HttpGet("export")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportSettings([FromQuery] bool includeSecrets = false, CancellationToken ct = default)
    {
        try
        {
            var json = await _settingsService.ExportSettingsAsync(includeSecrets, ct).ConfigureAwait(false);
            return Content(json, "application/json");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export settings");
            return StatusCode(500, new { error = "Failed to export settings" });
        }
    }

    /// <summary>
    /// Import settings from JSON
    /// </summary>
    [HttpPost("import")]
    [ProducesResponseType(typeof(SettingsUpdateResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ImportSettings(
        [FromBody] string json,
        [FromQuery] bool overwriteExisting = false,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _settingsService.ImportSettingsAsync(json, overwriteExisting, ct).ConfigureAwait(false);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import settings");
            return StatusCode(500, new { error = "Failed to import settings" });
        }
    }

    /// <summary>
    /// Get hardware performance settings
    /// </summary>
    [HttpGet("hardware")]
    [ProducesResponseType(typeof(HardwarePerformanceSettings), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHardwareSettings(CancellationToken ct)
    {
        try
        {
            var settings = await _settingsService.GetHardwareSettingsAsync(ct).ConfigureAwait(false);
            return Ok(settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get hardware settings");
            return StatusCode(500, new { error = "Failed to retrieve hardware settings" });
        }
    }

    /// <summary>
    /// Update hardware performance settings
    /// </summary>
    [HttpPut("hardware")]
    [ProducesResponseType(typeof(SettingsUpdateResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateHardwareSettings([FromBody] HardwarePerformanceSettings settings, CancellationToken ct)
    {
        try
        {
            var result = await _settingsService.UpdateHardwareSettingsAsync(settings, ct).ConfigureAwait(false);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update hardware settings");
            return StatusCode(500, new { error = "Failed to update hardware settings" });
        }
    }

    /// <summary>
    /// Get provider configuration
    /// </summary>
    [HttpGet("providers")]
    [ProducesResponseType(typeof(ProviderConfiguration), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProviderConfiguration(CancellationToken ct)
    {
        try
        {
            var config = await _settingsService.GetProviderConfigurationAsync(ct).ConfigureAwait(false);
            return Ok(config);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get provider configuration");
            return StatusCode(500, new { error = "Failed to retrieve provider configuration" });
        }
    }

    /// <summary>
    /// Update provider configuration
    /// </summary>
    [HttpPut("providers")]
    [ProducesResponseType(typeof(SettingsUpdateResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateProviderConfiguration([FromBody] ProviderConfiguration config, CancellationToken ct)
    {
        try
        {
            var result = await _settingsService.UpdateProviderConfigurationAsync(config, ct).ConfigureAwait(false);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update provider configuration");
            return StatusCode(500, new { error = "Failed to update provider configuration" });
        }
    }

    /// <summary>
    /// Test provider connection
    /// </summary>
    [HttpPost("providers/{providerName}/test")]
    [ProducesResponseType(typeof(ProviderTestResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> TestProviderConnection(string providerName, CancellationToken ct)
    {
        try
        {
            var result = await _settingsService.TestProviderConnectionAsync(providerName, ct).ConfigureAwait(false);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to test provider connection for {Provider}", providerName);
            return StatusCode(500, new { error = $"Failed to test {providerName} connection" });
        }
    }

    /// <summary>
    /// Get available GPU devices
    /// </summary>
    [HttpGet("hardware/gpus")]
    [ProducesResponseType(typeof(List<GpuDevice>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAvailableGpuDevices(CancellationToken ct)
    {
        try
        {
            var devices = await _settingsService.GetAvailableGpuDevicesAsync(ct).ConfigureAwait(false);
            return Ok(devices);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get available GPU devices");
            return StatusCode(500, new { error = "Failed to retrieve GPU devices" });
        }
    }

    /// <summary>
    /// Get available hardware encoders
    /// </summary>
    [HttpGet("hardware/encoders")]
    [ProducesResponseType(typeof(List<EncoderOption>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAvailableEncoders(CancellationToken ct)
    {
        try
        {
            var encoders = await _settingsService.GetAvailableEncodersAsync(ct).ConfigureAwait(false);
            return Ok(encoders);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get available encoders");
            return StatusCode(500, new { error = "Failed to retrieve available encoders" });
        }
    }

    /// <summary>
    /// Get export settings section
    /// </summary>
    [HttpGet("export-settings")]
    [ProducesResponseType(typeof(Aura.Core.Models.Settings.ExportSettings), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetExportSettings(CancellationToken ct)
    {
        try
        {
            var settings = await _settingsService.GetSettingsSectionAsync<Core.Models.Settings.ExportSettings>(ct).ConfigureAwait(false);
            return Ok(settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get export settings");
            return StatusCode(500, new { error = "Failed to retrieve export settings" });
        }
    }

    /// <summary>
    /// Update export settings section
    /// </summary>
    [HttpPut("export")]
    [ProducesResponseType(typeof(SettingsUpdateResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateExportSettings([FromBody] Aura.Core.Models.Settings.ExportSettings settings, CancellationToken ct)
    {
        try
        {
            var result = await _settingsService.UpdateSettingsSectionAsync(settings, ct).ConfigureAwait(false);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update export settings");
            return StatusCode(500, new { error = "Failed to update export settings" });
        }
    }

    /// <summary>
    /// Get provider rate limits section
    /// </summary>
    [HttpGet("ratelimits")]
    [ProducesResponseType(typeof(Aura.Core.Models.Settings.ProviderRateLimits), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRateLimits(CancellationToken ct)
    {
        try
        {
            var settings = await _settingsService.GetSettingsSectionAsync<Core.Models.Settings.ProviderRateLimits>(ct).ConfigureAwait(false);
            return Ok(settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get rate limits");
            return StatusCode(500, new { error = "Failed to retrieve rate limits" });
        }
    }

    /// <summary>
    /// Update provider rate limits section
    /// </summary>
    [HttpPut("ratelimits")]
    [ProducesResponseType(typeof(SettingsUpdateResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateRateLimits([FromBody] Aura.Core.Models.Settings.ProviderRateLimits settings, CancellationToken ct)
    {
        try
        {
            var result = await _settingsService.UpdateSettingsSectionAsync(settings, ct).ConfigureAwait(false);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update rate limits");
            return StatusCode(500, new { error = "Failed to update rate limits" });
        }
    }

    /// <summary>
    /// Test upload destination connection
    /// </summary>
    [HttpPost("upload-destinations/{id}/test")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> TestUploadDestination(string id, CancellationToken ct)
    {
        try
        {
            var settings = await _settingsService.GetSettingsAsync(ct).ConfigureAwait(false);
            var destination = settings.Export.UploadDestinations.Find(d => d.Id == id);
            
            if (destination == null)
            {
                return NotFound(new { error = "Upload destination not found" });
            }

            // Simple validation - full implementation would test actual connection
            var testResult = new
            {
                success = !string.IsNullOrEmpty(destination.Name),
                message = destination.Enabled 
                    ? "Upload destination configured successfully" 
                    : "Upload destination is disabled",
                destinationType = destination.Type.ToString()
            };

            return Ok(testResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to test upload destination {Id}", id);
            return StatusCode(500, new { error = "Failed to test upload destination" });
        }
    }

    /// <summary>
    /// Get operating mode (online/offline)
    /// </summary>
    [HttpGet("operating-mode")]
    [ProducesResponseType(typeof(OperatingModeResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOperatingMode(CancellationToken ct)
    {
        try
        {
            var settings = await _settingsService.GetSettingsAsync(ct).ConfigureAwait(false);
            var isOffline = settings.Advanced.OfflineMode;

            return Ok(new OperatingModeResponse
            {
                Mode = isOffline ? "offline" : "online",
                IsOffline = isOffline,
                AllowedLlmProviders = isOffline
                    ? Aura.Core.Configuration.OperatingModeHelper.OfflineLlmProviders
                    : Array.Empty<string>(),
                AllowedTtsProviders = isOffline
                    ? Aura.Core.Configuration.OperatingModeHelper.OfflineTtsProviders
                    : Array.Empty<string>(),
                AllowedImageProviders = isOffline
                    ? Aura.Core.Configuration.OperatingModeHelper.OfflineImageProviders
                    : Array.Empty<string>()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get operating mode");
            return StatusCode(500, new { error = "Failed to get operating mode" });
        }
    }

    /// <summary>
    /// Set operating mode (online/offline)
    /// </summary>
    [HttpPut("operating-mode")]
    [ProducesResponseType(typeof(SettingsUpdateResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetOperatingMode([FromBody] SetOperatingModeRequest request, CancellationToken ct)
    {
        try
        {
            var settings = await _settingsService.GetSettingsAsync(ct).ConfigureAwait(false);
            settings.Advanced.OfflineMode = request.IsOffline;

            var result = await _settingsService.UpdateSettingsAsync(settings, ct).ConfigureAwait(false);
            
            _logger.LogInformation("Operating mode changed to {Mode}", request.IsOffline ? "offline" : "online");
            
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set operating mode");
            return StatusCode(500, new { error = "Failed to set operating mode" });
        }
    }
}

/// <summary>
/// Response for operating mode query
/// </summary>
public class OperatingModeResponse
{
    public string Mode { get; set; } = "online";
    public bool IsOffline { get; set; }
    public string[] AllowedLlmProviders { get; set; } = Array.Empty<string>();
    public string[] AllowedTtsProviders { get; set; } = Array.Empty<string>();
    public string[] AllowedImageProviders { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Request to set operating mode
/// </summary>
public class SetOperatingModeRequest
{
    public bool IsOffline { get; set; }
}
