using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aura.Core.Configuration;
using Aura.Core.Errors;
using Aura.Core.Models;
using Microsoft.Extensions.Logging;

namespace Aura.Core.Services.Providers;

/// <summary>
/// Centralized service for managing provider validation state
/// </summary>
public class ProviderValidationService
{
    private readonly ILogger<ProviderValidationService> _logger;
    private readonly IEnumerable<IProviderValidator> _validators;
    private readonly ISecureStorageService _secureStorage;
    private readonly ProviderSettings _providerSettings;
    private readonly string _stateFilePath;

    private readonly Dictionary<string, ProviderState> _providerStates = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public ProviderValidationService(
        ILogger<ProviderValidationService> logger,
        IEnumerable<IProviderValidator> validators,
        ISecureStorageService secureStorage,
        ProviderSettings providerSettings)
    {
        _logger = logger;
        _validators = validators;
        _secureStorage = secureStorage;
        _providerSettings = providerSettings;

        var auraDataDir = _providerSettings.GetAuraDataDirectory();
        var configDir = Path.Combine(auraDataDir, "configurations");
        Directory.CreateDirectory(configDir);
        _stateFilePath = Path.Combine(configDir, "provider-states.json");

        LoadProviderStates();
    }

    /// <summary>
    /// Get the current state of all providers
    /// </summary>
    public async Task<Dictionary<string, ProviderState>> GetAllProviderStatesAsync(
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return new Dictionary<string, ProviderState>(_providerStates);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Get the state of a specific provider
    /// </summary>
    public async Task<ProviderState?> GetProviderStateAsync(
        string providerId,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _providerStates.TryGetValue(providerId, out var state) ? state : null;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Validate a specific provider
    /// </summary>
    public async Task<ProviderValidationResultV2> ValidateProviderAsync(
        string providerId,
        ProviderCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Validating provider {ProviderId}", providerId);

        var validator = _validators.FirstOrDefault(v => 
            v.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));

        if (validator == null)
        {
            _logger.LogWarning("No validator found for provider {ProviderId}", providerId);
            return new ProviderValidationResultV2
            {
                IsValid = false,
                ErrorCode = "NO_VALIDATOR",
                ErrorMessage = $"No validator available for provider {providerId}",
                ResponseTimeMs = 0
            };
        }

        var startTime = DateTime.UtcNow;
        try
        {
            var result = await validator.ValidateAsync(credentials, cancellationToken).ConfigureAwait(false);

            await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_providerStates.TryGetValue(providerId, out var state))
                {
                    state.ValidationStatus = result.IsValid 
                        ? ProviderValidationStatus.Valid 
                        : DetermineValidationStatus(result.ErrorCode);
                    state.LastValidationAt = DateTimeOffset.UtcNow;
                    state.LastErrorCode = result.ErrorCode;
                    state.LastErrorMessage = result.ErrorMessage;

                    await SaveProviderStatesAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                _lock.Release();
            }

            _logger.LogInformation(
                "Provider {ProviderId} validation completed: {Status} in {ElapsedMs}ms",
                providerId,
                result.IsValid ? "Valid" : "Invalid",
                result.ResponseTimeMs);

            return result;
        }
        catch (Exception ex)
        {
            var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogError(ex, "Error validating provider {ProviderId}", providerId);

            await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_providerStates.TryGetValue(providerId, out var state))
                {
                    state.ValidationStatus = ProviderValidationStatus.Error;
                    state.LastValidationAt = DateTimeOffset.UtcNow;
                    state.LastErrorCode = "VALIDATION_EXCEPTION";
                    state.LastErrorMessage = ex.Message;

                    await SaveProviderStatesAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                _lock.Release();
            }

            return new ProviderValidationResultV2
            {
                IsValid = false,
                ErrorCode = "VALIDATION_EXCEPTION",
                ErrorMessage = $"Validation failed: {ex.Message}",
                ResponseTimeMs = (long)elapsed,
                DiagnosticInfo = ex.ToString()
            };
        }
    }

    /// <summary>
    /// Validate all enabled providers
    /// </summary>
    public async Task<Dictionary<string, ProviderValidationResultV2>> ValidateAllProvidersAsync(
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Validating all enabled providers");

        var results = new Dictionary<string, ProviderValidationResultV2>();

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        List<ProviderState> enabledProviders;
        try
        {
            enabledProviders = _providerStates.Values
                .Where(p => p.Enabled && p.CredentialsConfigured)
                .ToList();
        }
        finally
        {
            _lock.Release();
        }

        foreach (var providerState in enabledProviders)
        {
            try
            {
                var credentials = await LoadCredentialsAsync(providerState.ProviderId, cancellationToken)
                    .ConfigureAwait(false);

                if (credentials != null)
                {
                    var result = await ValidateProviderAsync(
                        providerState.ProviderId,
                        credentials,
                        cancellationToken).ConfigureAwait(false);

                    results[providerState.ProviderId] = result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating provider {ProviderId}", providerState.ProviderId);
                results[providerState.ProviderId] = new ProviderValidationResultV2
                {
                    IsValid = false,
                    ErrorCode = "VALIDATION_ERROR",
                    ErrorMessage = ex.Message,
                    ResponseTimeMs = 0
                };
            }
        }

        _logger.LogInformation(
            "Validated {Total} providers: {Valid} valid, {Invalid} invalid",
            results.Count,
            results.Count(r => r.Value.IsValid),
            results.Count(r => !r.Value.IsValid));

        return results;
    }

    /// <summary>
    /// Save provider credentials and mark as configured
    /// </summary>
    public async Task SaveProviderCredentialsAsync(
        string providerId,
        ProviderCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Saving credentials for provider {ProviderId}", providerId);

        if (!string.IsNullOrWhiteSpace(credentials.ApiKey))
        {
            await _secureStorage.SaveApiKeyAsync(providerId, credentials.ApiKey).ConfigureAwait(false);
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_providerStates.TryGetValue(providerId, out var state))
            {
                state.CredentialsConfigured = !string.IsNullOrWhiteSpace(credentials.ApiKey) 
                    || credentials.AdditionalSettings.Count > 0;
                state.ValidationStatus = ProviderValidationStatus.Unknown;
                state.LastValidationAt = null;
                state.LastErrorCode = null;
                state.LastErrorMessage = null;

                if (!string.IsNullOrWhiteSpace(credentials.BaseUrl))
                {
                    state.Metadata["BaseUrl"] = credentials.BaseUrl;
                }
                if (!string.IsNullOrWhiteSpace(credentials.OrganizationId))
                {
                    state.Metadata["OrganizationId"] = credentials.OrganizationId;
                }
                if (!string.IsNullOrWhiteSpace(credentials.ProjectId))
                {
                    state.Metadata["ProjectId"] = credentials.ProjectId;
                }

                await SaveProviderStatesAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Enable or disable a provider
    /// </summary>
    public async Task SetProviderEnabledAsync(
        string providerId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_providerStates.TryGetValue(providerId, out var state))
            {
                state.Enabled = enabled;
                await SaveProviderStatesAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Initialize default provider states if not already initialized
    /// </summary>
    public async Task InitializeDefaultProvidersAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_providerStates.Count > 0)
            {
                return;
            }

            var defaultProviders = new[]
            {
                new ProviderState { ProviderId = "OpenAI", Type = ProviderType.LLM, Enabled = false, CredentialsConfigured = false, ValidationStatus = ProviderValidationStatus.NotConfigured, Priority = 1 },
                new ProviderState { ProviderId = "Anthropic", Type = ProviderType.LLM, Enabled = false, CredentialsConfigured = false, ValidationStatus = ProviderValidationStatus.NotConfigured, Priority = 2 },
                new ProviderState { ProviderId = "Google", Type = ProviderType.LLM, Enabled = false, CredentialsConfigured = false, ValidationStatus = ProviderValidationStatus.NotConfigured, Priority = 3 },
                new ProviderState { ProviderId = "Ollama", Type = ProviderType.LLM, Enabled = false, CredentialsConfigured = false, ValidationStatus = ProviderValidationStatus.NotConfigured, Priority = 4 },
                new ProviderState { ProviderId = "RuleBased", Type = ProviderType.LLM, Enabled = false, CredentialsConfigured = true, ValidationStatus = ProviderValidationStatus.Valid, Priority = 99 },
                new ProviderState { ProviderId = "ElevenLabs", Type = ProviderType.TTS, Enabled = false, CredentialsConfigured = false, ValidationStatus = ProviderValidationStatus.NotConfigured, Priority = 1 },
                new ProviderState { ProviderId = "PlayHT", Type = ProviderType.TTS, Enabled = false, CredentialsConfigured = false, ValidationStatus = ProviderValidationStatus.NotConfigured, Priority = 2 },
                new ProviderState { ProviderId = "Windows", Type = ProviderType.TTS, Enabled = false, CredentialsConfigured = true, ValidationStatus = ProviderValidationStatus.Unknown, Priority = 3 },
                new ProviderState { ProviderId = "Piper", Type = ProviderType.TTS, Enabled = false, CredentialsConfigured = true, ValidationStatus = ProviderValidationStatus.Unknown, Priority = 4 },
                new ProviderState { ProviderId = "Mimic3", Type = ProviderType.TTS, Enabled = false, CredentialsConfigured = true, ValidationStatus = ProviderValidationStatus.Unknown, Priority = 5 },
                new ProviderState { ProviderId = "StabilityAI", Type = ProviderType.Visual, Enabled = false, CredentialsConfigured = false, ValidationStatus = ProviderValidationStatus.NotConfigured, Priority = 1 },
                new ProviderState { ProviderId = "StableDiffusion", Type = ProviderType.Visual, Enabled = false, CredentialsConfigured = false, ValidationStatus = ProviderValidationStatus.NotConfigured, Priority = 2 },
                new ProviderState { ProviderId = "Stock", Type = ProviderType.Visual, Enabled = false, CredentialsConfigured = true, ValidationStatus = ProviderValidationStatus.Valid, Priority = 3 },
            };

            foreach (var provider in defaultProviders)
            {
                _providerStates[provider.ProviderId] = provider;
            }

            await SaveProviderStatesAsync().ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private void LoadProviderStates()
    {
        try
        {
            if (File.Exists(_stateFilePath))
            {
                var json = File.ReadAllText(_stateFilePath);
                var states = JsonSerializer.Deserialize<List<ProviderState>>(json);
                
                if (states != null)
                {
                    foreach (var state in states)
                    {
                        _providerStates[state.ProviderId] = state;
                    }
                    _logger.LogInformation("Loaded {Count} provider states from disk", states.Count);
                }
            }
            else
            {
                _logger.LogInformation("No existing provider states found, will initialize defaults");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading provider states, starting fresh");
        }
    }

    private async Task SaveProviderStatesAsync()
    {
        try
        {
            var states = _providerStates.Values.ToList();
            var json = JsonSerializer.Serialize(states, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            await File.WriteAllTextAsync(_stateFilePath, json).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving provider states");
        }
    }

    private async Task<ProviderCredentials?> LoadCredentialsAsync(
        string providerId,
        CancellationToken cancellationToken = default)
    {
        var apiKey = await _secureStorage.GetApiKeyAsync(providerId).ConfigureAwait(false);
        
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var credentials = new ProviderCredentials { ApiKey = apiKey };

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_providerStates.TryGetValue(providerId, out var state))
            {
                if (state.Metadata.TryGetValue("BaseUrl", out var baseUrl))
                {
                    credentials.BaseUrl = baseUrl;
                }
                if (state.Metadata.TryGetValue("OrganizationId", out var orgId))
                {
                    credentials.OrganizationId = orgId;
                }
                if (state.Metadata.TryGetValue("ProjectId", out var projId))
                {
                    credentials.ProjectId = projId;
                }
            }
        }
        finally
        {
            _lock.Release();
        }

        return credentials;
    }

    private static ProviderValidationStatus DetermineValidationStatus(string? errorCode)
    {
        return errorCode switch
        {
            "INVALID_API_KEY" or "UNAUTHORIZED" or "FORBIDDEN" => ProviderValidationStatus.Invalid,
            "NOT_CONFIGURED" => ProviderValidationStatus.NotConfigured,
            _ => ProviderValidationStatus.Error
        };
    }
}
