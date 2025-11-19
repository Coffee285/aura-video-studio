using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Aura.Core.Models;
using Aura.Core.Models.Visual;
using Aura.Core.Providers;
using Aura.Core.Models.Streaming;

namespace Aura.E2E;

/// <summary>
/// Mock TTS provider for E2E testing
/// </summary>
internal sealed class MockTtsProvider : ITtsProvider
{
    private readonly string _name;

    public MockTtsProvider(string name)
    {
        _name = name;
    }

    public Task<IReadOnlyList<string>> GetAvailableVoicesAsync()
    {
        return Task.FromResult<IReadOnlyList<string>>(new List<string> { "TestVoice" });
    }

    public Task<string> SynthesizeAsync(IEnumerable<ScriptLine> lines, VoiceSpec voice, CancellationToken ct)
    {
        return Task.FromResult($"/tmp/tts_output_{_name}.wav");
    }
}

/// <summary>
/// Mock LLM provider that always fails (for testing fallback)
/// </summary>
internal sealed class FailingLlmProvider : ILlmProvider
{
    public bool SupportsStreaming => false;

    public LlmProviderCharacteristics GetCharacteristics()
    {
        return new LlmProviderCharacteristics
        {
            IsLocal = false,
            ExpectedFirstTokenMs = 100,
            ExpectedTokensPerSec = 50,
            SupportsStreaming = false,
            ProviderTier = "Test",
            CostPer1KTokens = 0m
        };
    }

    public async IAsyncEnumerable<LlmStreamChunk> DraftScriptStreamAsync(
        Brief brief, 
        PlanSpec spec, 
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var result = await DraftScriptAsync(brief, spec, ct).ConfigureAwait(false);
        yield return new LlmStreamChunk
        {
            ProviderName = "Mock",
            Content = result,
            AccumulatedContent = result,
            TokenIndex = 1,
            IsFinal = true
        };
    }

    private readonly string _name;

    public FailingLlmProvider(string name)
    {
        _name = name;
    }

    public Task<string> DraftScriptAsync(Brief brief, PlanSpec spec, CancellationToken ct)
    {
        throw new System.Exception($"{_name} provider is not available");
    }

    public Task<string> CompleteAsync(string prompt, CancellationToken ct)
    {
        throw new System.Exception($"{_name} provider is not available");
    }

    public Task<SceneAnalysisResult?> AnalyzeSceneImportanceAsync(
        string sceneText,
        string? previousSceneText,
        string videoGoal,
        CancellationToken ct)
    {
        throw new System.Exception($"{_name} provider is not available");
    }

    public Task<VisualPromptResult?> GenerateVisualPromptAsync(
        string sceneText,
        string? previousSceneText,
        string videoTone,
        VisualStyle targetStyle,
        CancellationToken ct)
    {
        throw new System.Exception($"{_name} provider is not available");
    }

    public Task<ContentComplexityAnalysisResult?> AnalyzeContentComplexityAsync(
        string sceneText,
        string? previousSceneText,
        string videoGoal,
        CancellationToken ct)
    {
        return Task.FromResult<ContentComplexityAnalysisResult?>(null);
    }

    public Task<SceneCoherenceResult?> AnalyzeSceneCoherenceAsync(
        string fromSceneText,
        string toSceneText,
        string videoGoal,
        CancellationToken ct)
    {
        throw new System.Exception($"{_name} provider is not available");
    }

    public Task<NarrativeArcResult?> ValidateNarrativeArcAsync(
        IReadOnlyList<string> sceneTexts,
        string videoGoal,
        string videoType,
        CancellationToken ct)
    {
        throw new System.Exception($"{_name} provider is not available");
    }

    public Task<string?> GenerateTransitionTextAsync(
        string fromSceneText,
        string toSceneText,
        string videoGoal,
        CancellationToken ct)
    {
        throw new System.Exception($"{_name} provider is not available");
    }
}
