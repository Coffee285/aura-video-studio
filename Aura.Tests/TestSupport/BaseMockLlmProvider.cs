using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Aura.Core.Models;
using Aura.Core.Models.Narrative;
using Aura.Core.Models.Streaming;
using Aura.Core.Models.Visual;
using Aura.Core.Providers;

namespace Aura.Tests.TestSupport;

/// <summary>
/// Base mock LLM provider for testing with default streaming implementations
/// </summary>
public abstract class BaseMockLlmProvider : ILlmProvider
{
    public virtual bool SupportsStreaming => false;

    public virtual LlmProviderCharacteristics GetCharacteristics()
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

    public virtual async IAsyncEnumerable<LlmStreamChunk> DraftScriptStreamAsync(
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
            IsFinal = true,
            Metadata = new LlmStreamMetadata
            {
                TotalTokens = 1,
                EstimatedCost = 0m,
                TokensPerSecond = 50,
                IsLocalModel = false,
                ModelName = "mock",
                TimeToFirstTokenMs = 100,
                TotalDurationMs = 200,
                FinishReason = "stop"
            }
        };
    }

    public abstract Task<string> DraftScriptAsync(Brief brief, PlanSpec spec, CancellationToken ct);

    public abstract Task<string> CompleteAsync(string prompt, CancellationToken ct);

    public abstract Task<SceneAnalysisResult?> AnalyzeSceneImportanceAsync(
        string sceneText,
        string? previousSceneText,
        string videoGoal,
        CancellationToken ct);

    public abstract Task<VisualPromptResult?> GenerateVisualPromptAsync(
        string sceneText,
        string? previousSceneText,
        string videoTone,
        VisualStyle targetStyle,
        CancellationToken ct);

    public abstract Task<ContentComplexityAnalysisResult?> AnalyzeContentComplexityAsync(
        string sceneText,
        string? previousSceneText,
        string videoGoal,
        CancellationToken ct);

    public abstract Task<SceneCoherenceResult?> AnalyzeSceneCoherenceAsync(
        string fromSceneText,
        string toSceneText,
        string videoGoal,
        CancellationToken ct);

    public abstract Task<NarrativeArcResult?> ValidateNarrativeArcAsync(
        IReadOnlyList<string> sceneTexts,
        string videoGoal,
        string videoType,
        CancellationToken ct);

    public abstract Task<string?> GenerateTransitionTextAsync(
        string fromSceneText,
        string toSceneText,
        string videoGoal,
        CancellationToken ct);
}
