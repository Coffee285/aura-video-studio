using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Aura.Core.Configuration;
using Aura.Core.Models;
using Aura.Core.Models.Narrative;
using Aura.Core.Models.Streaming;
using Aura.Core.Models.Visual;
using Aura.Core.Orchestrator;
using Microsoft.Extensions.Logging;

namespace Aura.Core.Providers;

/// <summary>
/// Composite LLM provider that orchestrates across all available providers (OpenAI, Azure, Gemini, Ollama, RuleBased)
/// with automatic fallback based on ProviderMixer decisions and secure key availability.
/// </summary>
public class CompositeLlmProvider : ILlmProvider
{
    private const string DefaultPreferredTier = "ProIfAvailable";

    private readonly ILogger<CompositeLlmProvider> _logger;
    private readonly LlmProviderFactory _providerFactory;
    private readonly ProviderMixer _providerMixer;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IKeyStore _keyStore;

    private readonly object _sync = new();
    private Dictionary<string, ILlmProvider>? _cachedProviders;
    private DateTime _lastProviderRefresh = DateTime.MinValue;
    private readonly TimeSpan _providerCacheDuration = TimeSpan.FromMinutes(5);

    public CompositeLlmProvider(
        ILogger<CompositeLlmProvider> logger,
        LlmProviderFactory providerFactory,
        ProviderMixer providerMixer,
        ILoggerFactory loggerFactory,
        IKeyStore keyStore)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _providerFactory = providerFactory ?? throw new ArgumentNullException(nameof(providerFactory));
        _providerMixer = providerMixer ?? throw new ArgumentNullException(nameof(providerMixer));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _keyStore = keyStore ?? throw new ArgumentNullException(nameof(keyStore));
    }

    public Task<string> DraftScriptAsync(Brief brief, PlanSpec spec, CancellationToken ct)
    {
        return ExecuteWithFallbackAsync(
            provider => provider.DraftScriptAsync(brief, spec, ct),
            "script generation",
            ct,
            DefaultPreferredTier,
            result => string.IsNullOrWhiteSpace(result));
    }

    public Task<string> CompleteAsync(string prompt, CancellationToken ct)
    {
        return ExecuteWithFallbackAsync(
            provider => provider.CompleteAsync(prompt, ct),
            "prompt completion",
            ct,
            DefaultPreferredTier,
            result => string.IsNullOrWhiteSpace(result) || result.Trim() == "{}" || result.Trim() == "[]" || result.Trim().Length < 10);
    }

    /// <summary>
    /// Generate a chat completion response for ideation, brainstorming, and other conversational tasks.
    /// Executes with automatic fallback across configured providers.
    /// </summary>
    public Task<string> GenerateChatCompletionAsync(
        string systemPrompt,
        string userPrompt,
        LlmParameters? parameters = null,
        CancellationToken ct = default)
    {
        return ExecuteWithFallbackAsync(
            provider => provider.GenerateChatCompletionAsync(systemPrompt, userPrompt, parameters, ct),
            "chat completion for ideation",
            ct,
            DefaultPreferredTier,
            result => string.IsNullOrWhiteSpace(result) || result.Trim() == "{}" || result.Trim() == "[]" || result.Trim().Length < 10);
    }

    public Task<SceneAnalysisResult?> AnalyzeSceneImportanceAsync(
        string sceneText,
        string? previousSceneText,
        string videoGoal,
        CancellationToken ct)
    {
        return ExecuteWithFallbackAsync(
            provider => provider.AnalyzeSceneImportanceAsync(sceneText, previousSceneText, videoGoal, ct),
            "scene importance analysis",
            ct,
            DefaultPreferredTier,
            result => result == null,
            allowDefaultResultOnFailure: true);
    }

    public Task<VisualPromptResult?> GenerateVisualPromptAsync(
        string sceneText,
        string? previousSceneText,
        string videoTone,
        VisualStyle targetStyle,
        CancellationToken ct)
    {
        return ExecuteWithFallbackAsync(
            provider => provider.GenerateVisualPromptAsync(sceneText, previousSceneText, videoTone, targetStyle, ct),
            "visual prompt generation",
            ct,
            DefaultPreferredTier,
            result => result == null,
            allowDefaultResultOnFailure: true);
    }

    public Task<ContentComplexityAnalysisResult?> AnalyzeContentComplexityAsync(
        string sceneText,
        string? previousSceneText,
        string videoGoal,
        CancellationToken ct)
    {
        return ExecuteWithFallbackAsync(
            provider => provider.AnalyzeContentComplexityAsync(sceneText, previousSceneText, videoGoal, ct),
            "content complexity analysis",
            ct,
            DefaultPreferredTier,
            result => result == null,
            allowDefaultResultOnFailure: true);
    }

    public Task<SceneCoherenceResult?> AnalyzeSceneCoherenceAsync(
        string fromSceneText,
        string toSceneText,
        string videoGoal,
        CancellationToken ct)
    {
        return ExecuteWithFallbackAsync(
            provider => provider.AnalyzeSceneCoherenceAsync(fromSceneText, toSceneText, videoGoal, ct),
            "scene coherence analysis",
            ct,
            DefaultPreferredTier,
            result => result == null,
            allowDefaultResultOnFailure: true);
    }

    public Task<NarrativeArcResult?> ValidateNarrativeArcAsync(
        IReadOnlyList<string> sceneTexts,
        string videoGoal,
        string videoType,
        CancellationToken ct)
    {
        return ExecuteWithFallbackAsync(
            provider => provider.ValidateNarrativeArcAsync(sceneTexts, videoGoal, videoType, ct),
            "narrative arc validation",
            ct,
            DefaultPreferredTier,
            result => result == null,
            allowDefaultResultOnFailure: true);
    }

    public Task<string?> GenerateTransitionTextAsync(
        string fromSceneText,
        string toSceneText,
        string videoGoal,
        CancellationToken ct)
    {
        return ExecuteWithFallbackAsync(
            provider => provider.GenerateTransitionTextAsync(fromSceneText, toSceneText, videoGoal, ct),
            "transition text generation",
            ct,
            DefaultPreferredTier,
            result => string.IsNullOrWhiteSpace(result),
            allowDefaultResultOnFailure: true);
    }

    /// <summary>
    /// Whether this composite provider supports streaming (delegates to underlying providers)
    /// </summary>
    public bool SupportsStreaming => true;

    /// <summary>
    /// Get characteristics from the first available streaming provider
    /// </summary>
    public LlmProviderCharacteristics GetCharacteristics()
    {
        var providers = GetProviders();
        var chain = BuildProviderChain(providers, DefaultPreferredTier, "get characteristics");

        foreach (var providerName in chain)
        {
            if (providers.TryGetValue(providerName, out var provider) && provider != null)
            {
                if (provider.SupportsStreaming)
                {
                    return provider.GetCharacteristics();
                }
            }
        }

        return new LlmProviderCharacteristics
        {
            IsLocal = false,
            ExpectedFirstTokenMs = 1000,
            ExpectedTokensPerSec = 15,
            SupportsStreaming = true,
            ProviderTier = "Unknown"
        };
    }

    /// <summary>
    /// Stream script generation using the first available streaming provider
    /// </summary>
    public async IAsyncEnumerable<LlmStreamChunk> DraftScriptStreamAsync(
        Brief brief, 
        PlanSpec spec, 
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var providers = GetProviders();
        var chain = BuildProviderChain(providers, DefaultPreferredTier, "streaming script generation");

        Exception? lastException = null;

        foreach (var providerName in chain)
        {
            if (!providers.TryGetValue(providerName, out var provider) || provider == null)
            {
                continue;
            }

            if (!provider.SupportsStreaming)
            {
                _logger.LogDebug("Provider {Provider} does not support streaming, skipping", providerName);
                continue;
            }

            _logger.LogInformation("Starting streaming script generation with provider {Provider}", providerName);

            var hasYieldedChunks = false;
            var streamEnumerator = provider.DraftScriptStreamAsync(brief, spec, ct).ConfigureAwait(false).GetAsyncEnumerator();
            LlmStreamChunk? errorChunk = null;

            try
            {
                while (true)
                {
                    LlmStreamChunk chunk;
                    try
                    {
                        if (!await streamEnumerator.MoveNextAsync())
                        {
                            break;
                        }
                        chunk = streamEnumerator.Current;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        _logger.LogWarning(ex, "Streaming script generation failed with provider {Provider}, trying next", providerName);
                        
                        if (hasYieldedChunks)
                        {
                            errorChunk = new LlmStreamChunk
                            {
                                ProviderName = providerName,
                                Content = string.Empty,
                                TokenIndex = 0,
                                IsFinal = true,
                                ErrorMessage = $"Stream interrupted: {ex.Message}"
                            };
                        }
                        break;
                    }

                    hasYieldedChunks = true;
                    yield return chunk;

                    if (chunk.IsFinal)
                    {
                        if (!string.IsNullOrWhiteSpace(chunk.ErrorMessage))
                        {
                            _logger.LogWarning(
                                "Provider {Provider} completed with error: {Error}",
                                providerName,
                                chunk.ErrorMessage);
                            break;
                        }

                        yield break;
                    }
                }

                if (errorChunk != null)
                {
                    yield return errorChunk;
                    yield break;
                }

                if (hasYieldedChunks)
                {
                    yield break;
                }
            }
            finally
            {
                await streamEnumerator.DisposeAsync();
            }
        }

        yield return new LlmStreamChunk
        {
            ProviderName = "Composite",
            Content = string.Empty,
            TokenIndex = 0,
            IsFinal = true,
            ErrorMessage = lastException != null 
                ? $"All streaming providers failed: {lastException.Message}" 
                : "No streaming providers available"
        };
    }

    private Dictionary<string, ILlmProvider> GetProviders(bool forceRefresh = false)
    {
        lock (_sync)
        {
            if (forceRefresh ||
                _cachedProviders == null ||
                DateTime.UtcNow - _lastProviderRefresh > _providerCacheDuration)
            {
                _logger.LogInformation("Refreshing LLM provider registry (force: {Force})", forceRefresh);
                _cachedProviders = _providerFactory.CreateAvailableProviders(_loggerFactory);
                _lastProviderRefresh = DateTime.UtcNow;
            }

            return _cachedProviders;
        }
    }

    private List<string> BuildProviderChain(
        Dictionary<string, ILlmProvider> providers,
        string preferredTier,
        string operationName)
    {
        var offlineOnly = _keyStore.IsOfflineOnly();
        var decision = _providerMixer.ResolveLlm(providers, preferredTier, offlineOnly);

        var chain = new List<string>();

        void AddIfAvailable(string? providerName)
        {
            if (string.IsNullOrWhiteSpace(providerName))
            {
                return;
            }

            if (!providers.ContainsKey(providerName))
            {
                _logger.LogDebug("Provider {Provider} not registered for {Operation}, skipping", providerName, operationName);
                return;
            }

            if (!chain.Contains(providerName))
            {
                chain.Add(providerName);
            }
        }

        if (decision.ProviderName == "None")
        {
            _logger.LogWarning(
                "Preferred tier {Tier} unavailable for {Operation} (offlineOnly: {Offline}). Falling back to local providers.",
                preferredTier,
                operationName,
                offlineOnly);

            AddIfAvailable("Ollama");
            AddIfAvailable("RuleBased");
        }
        else
        {
            AddIfAvailable(decision.ProviderName);

            if (decision.DowngradeChain?.Length > 0)
            {
                foreach (var providerName in decision.DowngradeChain)
                {
                    AddIfAvailable(providerName);
                }
            }

            if (!chain.Contains("RuleBased"))
            {
                AddIfAvailable("RuleBased");
            }
        }

        if (chain.Count == 0 && providers.ContainsKey("RuleBased"))
        {
            chain.Add("RuleBased");
        }

        _logger.LogInformation(
            "Provider chain for {Operation}: {Chain}",
            operationName,
            chain.Count > 0 ? string.Join(" → ", chain) : "[none]");

        return chain;
    }

    private async Task<T> ExecuteWithFallbackAsync<T>(
        Func<ILlmProvider, Task<T>> operation,
        string operationName,
        CancellationToken ct,
        string preferredTier,
        Func<T, bool>? shouldRetry,
        bool allowDefaultResultOnFailure = false)
    {
        var exceptions = new List<Exception>();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var forceRefresh = attempt > 0;
            var providers = GetProviders(forceRefresh);
            if (providers == null || providers.Count == 0)
            {
                _logger.LogWarning("No LLM providers available in registry for {Operation} (attempt {Attempt})", operationName, attempt + 1);
                continue;
            }

            var chain = BuildProviderChain(providers, preferredTier, operationName);
            if (chain.Count == 0)
            {
                _logger.LogWarning("Provider chain empty for {Operation} (attempt {Attempt})", operationName, attempt + 1);
                continue;
            }

            foreach (var providerName in chain)
            {
                if (!providers.TryGetValue(providerName, out var provider) || provider == null)
                {
                    _logger.LogDebug("Resolved provider {Provider} not instantiated for {Operation}", providerName, operationName);
                    continue;
                }

                try
                {
                    ct.ThrowIfCancellationRequested();

                    _logger.LogInformation("Executing {Operation} with provider {Provider}", operationName, providerName);
                    var result = await operation(provider).ConfigureAwait(false);

                    if (shouldRetry != null && shouldRetry(result))
                    {
                        _logger.LogWarning(
                            "{Operation} via {Provider} returned insufficient result, attempting fallback",
                            operationName,
                            providerName);
                        continue;
                    }

                    return result;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "{Operation} failed using provider {Provider} (attempt {Attempt})",
                        operationName,
                        providerName,
                        attempt + 1);
                    exceptions.Add(ex);
                }
            }
        }

        if (allowDefaultResultOnFailure && exceptions.Count == 0)
        {
            _logger.LogInformation("{Operation} could not produce a result from available providers; returning default value", operationName);
            return default!;
        }

        throw new AggregateException($"All LLM providers failed for {operationName}", exceptions);
    }
}
