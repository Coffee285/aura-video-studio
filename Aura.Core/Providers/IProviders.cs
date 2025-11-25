using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aura.Core.Models;
using Aura.Core.Models.Narrative;
using Aura.Core.Models.Streaming;
using Aura.Core.Models.Visual;

namespace Aura.Core.Providers;

public interface ILlmProvider
{
    Task<string> DraftScriptAsync(Brief brief, PlanSpec spec, CancellationToken ct);
    
    /// <summary>
    /// Draft script with real-time streaming support for token-by-token generation
    /// </summary>
    IAsyncEnumerable<LlmStreamChunk> DraftScriptStreamAsync(Brief brief, PlanSpec spec, CancellationToken ct);
    
    /// <summary>
    /// Whether this provider supports streaming
    /// </summary>
    bool SupportsStreaming { get; }
    
    /// <summary>
    /// Get provider characteristics for adaptive UI (expected latency, throughput, cost)
    /// </summary>
    LlmProviderCharacteristics GetCharacteristics();
    
    /// <summary>
    /// Executes a raw prompt completion for structured output generation (used by orchestration layer)
    /// </summary>
    Task<string> CompleteAsync(string prompt, CancellationToken ct);
    
    /// <summary>
    /// Generate a chat completion response for ideation, brainstorming, and other conversational tasks.
    /// Uses system/user prompt pattern for more reliable JSON output.
    /// </summary>
    /// <param name="systemPrompt">System prompt defining the AI's role and output format</param>
    /// <param name="userPrompt">User prompt with the actual request</param>
    /// <param name="parameters">Optional LLM parameters (temperature, maxTokens, etc.)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The LLM response text</returns>
    Task<string> GenerateChatCompletionAsync(
        string systemPrompt,
        string userPrompt,
        LlmParameters? parameters = null,
        CancellationToken ct = default);
    
    Task<SceneAnalysisResult?> AnalyzeSceneImportanceAsync(
        string sceneText,
        string? previousSceneText,
        string videoGoal,
        CancellationToken ct);
    Task<VisualPromptResult?> GenerateVisualPromptAsync(
        string sceneText,
        string? previousSceneText,
        string videoTone,
        VisualStyle targetStyle,
        CancellationToken ct);
    Task<ContentComplexityAnalysisResult?> AnalyzeContentComplexityAsync(
        string sceneText,
        string? previousSceneText,
        string videoGoal,
        CancellationToken ct);
    Task<SceneCoherenceResult?> AnalyzeSceneCoherenceAsync(
        string fromSceneText,
        string toSceneText,
        string videoGoal,
        CancellationToken ct);
    Task<NarrativeArcResult?> ValidateNarrativeArcAsync(
        IReadOnlyList<string> sceneTexts,
        string videoGoal,
        string videoType,
        CancellationToken ct);
    Task<string?> GenerateTransitionTextAsync(
        string fromSceneText,
        string toSceneText,
        string videoGoal,
        CancellationToken ct);
}

/// <summary>
/// Result of LLM-based scene analysis for pacing optimization
/// </summary>
public record SceneAnalysisResult(
    double Importance,
    double Complexity,
    double EmotionalIntensity,
    string InformationDensity,
    double OptimalDurationSeconds,
    string TransitionType,
    string Reasoning
);

/// <summary>
/// Result of LLM-based visual prompt generation
/// </summary>
public record VisualPromptResult(
    string DetailedDescription,
    string CompositionGuidelines,
    string LightingMood,
    string LightingDirection,
    string LightingQuality,
    string TimeOfDay,
    string[] ColorPalette,
    string ShotType,
    string CameraAngle,
    string DepthOfField,
    string[] StyleKeywords,
    string[] NegativeElements,
    string[] ContinuityElements,
    string Reasoning
);

/// <summary>
/// Result of LLM-based content complexity analysis for adaptive pacing
/// </summary>
public record ContentComplexityAnalysisResult(
    double OverallComplexityScore,
    double ConceptDifficulty,
    double TerminologyDensity,
    double PrerequisiteKnowledgeLevel,
    double MultiStepReasoningRequired,
    int NewConceptsIntroduced,
    double CognitiveProcessingTimeSeconds,
    double OptimalAttentionWindowSeconds,
    string DetailedBreakdown
);

/// <summary>
/// Result of LLM-based scene coherence analysis
/// </summary>
public record SceneCoherenceResult(
    double CoherenceScore,
    string[] ConnectionTypes,
    double ConfidenceScore,
    string Reasoning
);

/// <summary>
/// Result of LLM-based narrative arc validation
/// </summary>
public record NarrativeArcResult(
    bool IsValid,
    string DetectedStructure,
    string ExpectedStructure,
    string[] StructuralIssues,
    string[] Recommendations,
    string Reasoning
);

public interface ITtsProvider
{
    Task<IReadOnlyList<string>> GetAvailableVoicesAsync();
    Task<string> SynthesizeAsync(IEnumerable<ScriptLine> lines, VoiceSpec spec, CancellationToken ct);
}

public interface IImageProvider
{
    Task<IReadOnlyList<Asset>> FetchOrGenerateAsync(Scene scene, VisualSpec spec, CancellationToken ct);
}

public interface IVideoComposer
{
    Task<string> RenderAsync(Timeline timeline, RenderSpec spec, IProgress<RenderProgress> progress, CancellationToken ct);
}

public interface IStockProvider
{
    Task<IReadOnlyList<Asset>> SearchAsync(string query, int count, CancellationToken ct);
}

public record VisualSpec(string Style, Aspect Aspect, string[] Keywords);

public record Timeline(
    IReadOnlyList<Scene> Scenes,
    IReadOnlyDictionary<int, IReadOnlyList<Asset>> SceneAssets, 
    string NarrationPath, 
    string MusicPath,
    string? SubtitlesPath);