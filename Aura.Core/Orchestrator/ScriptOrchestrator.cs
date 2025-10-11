using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Aura.Core.Models;
using Aura.Core.Providers;
using Microsoft.Extensions.Logging;

namespace Aura.Core.Orchestrator;

/// <summary>
/// Orchestrates script generation with provider routing and fallback logic
/// </summary>
public class ScriptOrchestrator
{
    private readonly ILogger<ScriptOrchestrator> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ProviderMixer _providerMixer;
    private readonly Dictionary<string, ILlmProvider> _providers;

    public ScriptOrchestrator(
        ILogger<ScriptOrchestrator> logger,
        ILoggerFactory loggerFactory,
        ProviderMixer providerMixer,
        Dictionary<string, ILlmProvider> providers)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _providerMixer = providerMixer;
        _providers = providers;
    }

    /// <summary>
    /// Generate a script using the appropriate provider based on tier and availability
    /// </summary>
    public async Task<ScriptResult> GenerateScriptAsync(
        Brief brief, 
        PlanSpec spec, 
        string preferredTier,
        bool offlineOnly,
        CancellationToken ct)
    {
        _logger.LogInformation("Starting script generation for topic: {Topic}, preferredTier: {Tier}, offlineOnly: {OfflineOnly}", 
            brief.Topic, preferredTier, offlineOnly);

        // Block Pro providers if offline-only mode
        if (offlineOnly && (preferredTier == "Pro" || preferredTier == "ProIfAvailable"))
        {
            _logger.LogWarning("Pro providers requested but system is in OfflineOnly mode (E307)");
            
            if (preferredTier == "Pro")
            {
                return new ScriptResult
                {
                    Success = false,
                    ErrorCode = "E307",
                    ErrorMessage = "Pro LLM providers require internet connection but system is in OfflineOnly mode. Please disable OfflineOnly mode or use Free providers.",
                    Script = null,
                    ProviderUsed = null,
                    IsFallback = false
                };
            }
            
            // ProIfAvailable downgrades gracefully
            _logger.LogInformation("ProIfAvailable downgrading to Free providers due to OfflineOnly mode");
            preferredTier = "Free";
        }

        // Select provider
        var selection = _providerMixer.SelectLlmProvider(_providers, preferredTier);
        _providerMixer.LogSelection(selection);

        var requestedProvider = selection.SelectedProvider;

        // Try primary provider
        var result = await TryGenerateWithProviderAsync(
            brief, 
            spec, 
            selection.SelectedProvider, 
            selection.IsFallback,
            requestedProvider,
            null,
            ct).ConfigureAwait(false);

        if (result.Success)
        {
            return result;
        }

        // If primary provider failed, try fallback chain (always enabled for now)
        {
            var primaryFailureReason = result.ErrorMessage ?? "Provider failed";
            _logger.LogWarning("Primary provider {Provider} failed, attempting fallback: {Reason}", 
                selection.SelectedProvider, primaryFailureReason);
            
            // Try Ollama if not already tried
            if (selection.SelectedProvider != "Ollama" && _providers.ContainsKey("Ollama"))
            {
                _logger.LogInformation("Falling back to Ollama");
                result = await TryGenerateWithProviderAsync(
                    brief, spec, "Ollama", true, requestedProvider, 
                    $"Primary provider {requestedProvider} failed: {primaryFailureReason}", 
                    ct).ConfigureAwait(false);
                if (result.Success)
                {
                    _logger.LogWarning("Successfully downgraded from {Requested} to {Actual}", 
                        requestedProvider, result.ProviderUsed);
                    return result;
                }
            }

            // Finally, fall back to RuleBased (always available - guaranteed fallback)
            // Try this even if RuleBased is not in the providers dictionary
            if (selection.SelectedProvider != "RuleBased")
            {
                _logger.LogInformation("Falling back to RuleBased provider (final guaranteed fallback)");
                result = await TryGenerateWithProviderAsync(
                    brief, spec, "RuleBased", true, requestedProvider,
                    $"All higher-tier providers failed, final fallback to RuleBased",
                    ct).ConfigureAwait(false);
                if (result.Success)
                {
                    _logger.LogWarning("Successfully downgraded from {Requested} to {Actual} (final fallback)", 
                        requestedProvider, result.ProviderUsed);
                    return result;
                }
                else
                {
                    _logger.LogError("CRITICAL: Even RuleBased fallback failed - no providers available");
                }
            }
        }

        // All providers failed
        return new ScriptResult
        {
            Success = false,
            ErrorCode = "E300",
            ErrorMessage = "All LLM providers failed to generate script",
            Script = null,
            ProviderUsed = null,
            IsFallback = false
        };
    }

    private async Task<ScriptResult> TryGenerateWithProviderAsync(
        Brief brief,
        PlanSpec spec,
        string providerName,
        bool isFallback,
        string? requestedProvider,
        string? downgradeReason,
        CancellationToken ct)
    {
        if (!_providers.TryGetValue(providerName, out var provider))
        {
            // Special case: if RuleBased is requested but not in dictionary, instantiate it as last resort
            if (providerName == "RuleBased")
            {
                _logger.LogWarning("RuleBased provider not in registry, instantiating as guaranteed fallback");
                try
                {
                    // Instantiate RuleBased provider dynamically with correct logger type
                    var type = Type.GetType("Aura.Providers.Llm.RuleBasedLlmProvider, Aura.Providers");
                    if (type != null)
                    {
                        // Create a typed logger using reflection
                        var createLoggerMethod = typeof(LoggerFactoryExtensions)
                            .GetMethods()
                            .FirstOrDefault(m => m.Name == "CreateLogger" && m.IsGenericMethod && m.GetParameters().Length == 1);
                        
                        if (createLoggerMethod != null)
                        {
                            var genericMethod = createLoggerMethod.MakeGenericMethod(type);
                            var logger = genericMethod.Invoke(null, new object[] { _loggerFactory });
                            provider = (ILlmProvider)Activator.CreateInstance(type, logger)!;
                            _providers[providerName] = provider; // Cache it for future use
                        }
                        else
                        {
                            _logger.LogError("CreateLogger<T> method not found");
                        }
                    }
                    else
                    {
                        _logger.LogError("RuleBasedLlmProvider type not found via reflection");
                        return new ScriptResult
                        {
                            Success = false,
                            ErrorCode = "E305",
                            ErrorMessage = "RuleBased provider type not found",
                            Script = null,
                            ProviderUsed = providerName,
                            IsFallback = isFallback,
                            RequestedProvider = requestedProvider,
                            DowngradeReason = downgradeReason
                        };
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to instantiate RuleBased provider");
                    return new ScriptResult
                    {
                        Success = false,
                        ErrorCode = "E305",
                        ErrorMessage = $"Failed to instantiate RuleBased provider: {ex.Message}",
                        Script = null,
                        ProviderUsed = providerName,
                        IsFallback = isFallback,
                        RequestedProvider = requestedProvider,
                        DowngradeReason = downgradeReason
                    };
                }
            }
            else
            {
                _logger.LogWarning("Provider {Provider} not available", providerName);
                return new ScriptResult
                {
                    Success = false,
                    ErrorCode = "E305",
                    ErrorMessage = $"Provider {providerName} not available",
                    Script = null,
                    ProviderUsed = providerName,
                    IsFallback = isFallback,
                    RequestedProvider = requestedProvider,
                    DowngradeReason = downgradeReason
                };
            }
        }

        try
        {
            _logger.LogInformation("Attempting script generation with {Provider}", providerName);
            var script = await provider.DraftScriptAsync(brief, spec, ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(script))
            {
                _logger.LogWarning("Provider {Provider} returned empty script", providerName);
                return new ScriptResult
                {
                    Success = false,
                    ErrorCode = "E302",
                    ErrorMessage = $"Provider {providerName} returned empty script",
                    Script = null,
                    ProviderUsed = providerName,
                    IsFallback = isFallback,
                    RequestedProvider = requestedProvider,
                    DowngradeReason = downgradeReason
                };
            }

            _logger.LogInformation("Successfully generated script with {Provider} ({Length} chars)", 
                providerName, script.Length);

            return new ScriptResult
            {
                Success = true,
                ErrorCode = null,
                ErrorMessage = null,
                Script = script,
                ProviderUsed = providerName,
                IsFallback = isFallback,
                RequestedProvider = requestedProvider,
                DowngradeReason = downgradeReason
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Provider {Provider} failed: {Message}", providerName, ex.Message);
            return new ScriptResult
            {
                Success = false,
                ErrorCode = "E300",
                ErrorMessage = $"Provider {providerName} failed: {ex.Message}",
                Script = null,
                ProviderUsed = providerName,
                IsFallback = isFallback,
                RequestedProvider = requestedProvider,
                DowngradeReason = downgradeReason
            };
        }
    }
}

/// <summary>
/// Result of script generation attempt
/// 
/// Error Codes:
/// - E300: General script provider failure
/// - E301: Request timeout or cancellation
/// - E302: Provider returned empty/invalid script
/// - E303: Invalid enum value or input validation failure
/// - E304: Invalid plan parameters (duration, etc.)
/// - E305: Provider not available/not registered
/// - E307: Offline mode restriction (Pro providers blocked)
/// </summary>
public record ScriptResult
{
    public bool Success { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? Script { get; init; }
    public string? ProviderUsed { get; init; }
    public bool IsFallback { get; init; }
    
    /// <summary>
    /// The provider that was originally requested (if different from ProviderUsed due to fallback)
    /// </summary>
    public string? RequestedProvider { get; init; }
    
    /// <summary>
    /// Reason for downgrade/fallback if one occurred
    /// </summary>
    public string? DowngradeReason { get; init; }
}
