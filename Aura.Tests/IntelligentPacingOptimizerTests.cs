using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aura.Core.ML.Models;
using Aura.Core.Models;
using Aura.Core.Models.Narrative;
using Aura.Core.Models.Streaming;
using Aura.Core.Models.PacingModels;
using Aura.Core.Models.Visual;
using Aura.Core.Providers;
using Aura.Core.Services.PacingServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aura.Tests;

/// <summary>
/// Tests for the IntelligentPacingOptimizer
/// </summary>
public class IntelligentPacingOptimizerTests
{
    private readonly IntelligentPacingOptimizer _optimizer;
    private readonly SceneImportanceAnalyzer _sceneAnalyzer;
    private readonly AttentionCurvePredictor _attentionPredictor;
    private readonly AttentionRetentionModel _attentionModel;
    private readonly TransitionRecommender _transitionRecommender;
    private readonly EmotionalBeatAnalyzer _emotionalBeatAnalyzer;
    private readonly SceneRelationshipMapper _relationshipMapper;

    public IntelligentPacingOptimizerTests()
    {
        var sceneLogger = NullLogger<SceneImportanceAnalyzer>.Instance;
        var predictorLogger = NullLogger<AttentionCurvePredictor>.Instance;
        var modelLogger = NullLogger<AttentionRetentionModel>.Instance;
        var optimizerLogger = NullLogger<IntelligentPacingOptimizer>.Instance;
        var transitionLogger = NullLogger<TransitionRecommender>.Instance;
        var emotionalLogger = NullLogger<EmotionalBeatAnalyzer>.Instance;
        var relationshipLogger = NullLogger<SceneRelationshipMapper>.Instance;

        _attentionModel = new AttentionRetentionModel(modelLogger);
        _sceneAnalyzer = new SceneImportanceAnalyzer(sceneLogger);
        _attentionPredictor = new AttentionCurvePredictor(predictorLogger, _attentionModel);
        _transitionRecommender = new TransitionRecommender(transitionLogger);
        _emotionalBeatAnalyzer = new EmotionalBeatAnalyzer(emotionalLogger);
        _relationshipMapper = new SceneRelationshipMapper(relationshipLogger);
        _optimizer = new IntelligentPacingOptimizer(
            optimizerLogger,
            _sceneAnalyzer,
            _attentionPredictor,
            _transitionRecommender,
            _emotionalBeatAnalyzer,
            _relationshipMapper,
            null);
    }

    [Fact]
    public async Task OptimizePacingAsync_WithoutLlm_Should_UseHeuristicAnalysis()
    {
        // Arrange
        var scenes = new List<Scene>
        {
            new Scene(0, "Hook", "Welcome to our amazing video about AI.", TimeSpan.Zero, TimeSpan.FromSeconds(5)),
            new Scene(1, "Body", "AI is transforming how we work and live in many ways.", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10))
        };

        var brief = new Brief(
            Topic: "AI Technology",
            Audience: "general",
            Goal: "educate",
            Tone: "professional",
            Language: "en",
            Aspect: Aspect.Widescreen16x9);

        // Act
        var result = await _optimizer.OptimizePacingAsync(scenes, brief, null, false, PacingProfile.BalancedDocumentary, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.TimingSuggestions.Count);
        Assert.False(result.LlmAnalysisSucceeded);
        Assert.Null(result.LlmProviderUsed);
        Assert.True(result.ConfidenceScore > 0);
        Assert.NotNull(result.AttentionCurve);
    }

    [Fact]
    public async Task OptimizePacingAsync_WithMockLlm_Should_UseLlmAnalysis()
    {
        // Arrange
        var scenes = new List<Scene>
        {
            new Scene(0, "Introduction", "This is an introduction with important information.", TimeSpan.Zero, TimeSpan.FromSeconds(8))
        };

        var brief = new Brief(
            Topic: "Test Topic",
            Audience: "experts",
            Goal: "explain",
            Tone: "professional",
            Language: "en",
            Aspect: Aspect.Widescreen16x9);

        var mockLlm = new MockSuccessfulLlmProvider();

        // Act
        var result = await _optimizer.OptimizePacingAsync(scenes, brief, mockLlm, false, PacingProfile.BalancedDocumentary, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.TimingSuggestions);
        Assert.True(result.LlmAnalysisSucceeded);
        Assert.Equal(nameof(MockSuccessfulLlmProvider), result.LlmProviderUsed);
        Assert.True(result.ConfidenceScore > 70); // Higher confidence with LLM
    }

    [Fact]
    public async Task OptimizePacingAsync_Should_GenerateTimingSuggestions()
    {
        // Arrange
        var scenes = new List<Scene>
        {
            new Scene(0, "Hook", "Short hook.", TimeSpan.Zero, TimeSpan.FromSeconds(3)),
            new Scene(1, "Main", string.Join(" ", Enumerable.Repeat("word", 50)), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(15)),
            new Scene(2, "Conclusion", "Thank you for watching!", TimeSpan.FromSeconds(18), TimeSpan.FromSeconds(5))
        };

        var brief = new Brief(
            Topic: "Tutorial",
            Audience: "beginners",
            Goal: "teach",
            Tone: "friendly",
            Language: "en",
            Aspect: Aspect.Widescreen16x9);

        // Act
        var result = await _optimizer.OptimizePacingAsync(scenes, brief, null, false, PacingProfile.BalancedDocumentary, CancellationToken.None);

        // Assert
        Assert.Equal(3, result.TimingSuggestions.Count);
        
        foreach (var suggestion in result.TimingSuggestions)
        {
            Assert.True(suggestion.OptimalDuration.TotalSeconds >= 3); // Min duration
            Assert.True(suggestion.OptimalDuration.TotalSeconds <= 120); // Max duration
            Assert.True(suggestion.MinDuration < suggestion.OptimalDuration);
            Assert.True(suggestion.MaxDuration > suggestion.OptimalDuration);
            Assert.InRange(suggestion.ImportanceScore, 0, 100);
            Assert.InRange(suggestion.ComplexityScore, 0, 100);
            Assert.NotEmpty(suggestion.Reasoning);
        }
    }

    [Fact]
    public async Task OptimizePacingAsync_Should_ApplyPlatformMultiplier()
    {
        // Arrange - Same scene, different platforms
        var scene = new Scene(0, "Test", string.Join(" ", Enumerable.Repeat("word", 30)), TimeSpan.Zero, TimeSpan.FromSeconds(10));

        var youtubeBrief = new Brief("Test", "general", "test", "casual", "en", Aspect.Widescreen16x9);
        var tiktokBrief = new Brief("Test", "general", "test", "casual", "en", Aspect.Vertical9x16);
        var instagramBrief = new Brief("Test", "general", "test", "casual", "en", Aspect.Square1x1);

        // Act
        var youtubeResult = await _optimizer.OptimizePacingAsync(new[] { scene }, youtubeBrief, null, false, PacingProfile.BalancedDocumentary, CancellationToken.None);
        var tiktokResult = await _optimizer.OptimizePacingAsync(new[] { scene }, tiktokBrief, null, false, PacingProfile.BalancedDocumentary, CancellationToken.None);
        var instagramResult = await _optimizer.OptimizePacingAsync(new[] { scene }, instagramBrief, null, false, PacingProfile.BalancedDocumentary, CancellationToken.None);

        // Assert - TikTok should have shortest duration (faster pacing), YouTube longest
        var youtubeDuration = youtubeResult.TimingSuggestions[0].OptimalDuration.TotalSeconds;
        var tiktokDuration = tiktokResult.TimingSuggestions[0].OptimalDuration.TotalSeconds;
        var instagramDuration = instagramResult.TimingSuggestions[0].OptimalDuration.TotalSeconds;

        Assert.True(tiktokDuration < youtubeDuration, "TikTok should have faster pacing than YouTube");
        Assert.True(instagramDuration < youtubeDuration, "Instagram should have faster pacing than YouTube");
    }

    [Fact]
    public async Task OptimizePacingAsync_Should_GenerateAttentionCurve()
    {
        // Arrange
        var scenes = new List<Scene>
        {
            new Scene(0, "Scene1", "Test script.", TimeSpan.Zero, TimeSpan.FromSeconds(10)),
            new Scene(1, "Scene2", "Another test.", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10))
        };

        var brief = new Brief("Test", "general", "test", "casual", "en", Aspect.Widescreen16x9);

        // Act
        var result = await _optimizer.OptimizePacingAsync(scenes, brief, null, false, PacingProfile.BalancedDocumentary, CancellationToken.None);

        // Assert
        Assert.NotNull(result.AttentionCurve);
        Assert.NotEmpty(result.AttentionCurve.DataPoints);
        Assert.InRange(result.AttentionCurve.AverageEngagement, 0, 100);
        Assert.InRange(result.AttentionCurve.OverallRetentionScore, 0, 100);
        
        // Verify data points are ordered by timestamp
        for (int i = 1; i < result.AttentionCurve.DataPoints.Count; i++)
        {
            Assert.True(result.AttentionCurve.DataPoints[i].Timestamp >= result.AttentionCurve.DataPoints[i - 1].Timestamp);
        }
    }

    [Fact]
    public async Task OptimizePacingAsync_Should_GenerateWarnings()
    {
        // Arrange - Very long scene that should trigger warning
        var longScript = string.Join(" ", Enumerable.Repeat("word", 300));
        var scenes = new List<Scene>
        {
            new Scene(0, "VeryLong", longScript, TimeSpan.Zero, TimeSpan.FromSeconds(5))
        };

        var brief = new Brief("Test", "general", "test", "casual", "en", Aspect.Widescreen16x9);

        // Act
        var result = await _optimizer.OptimizePacingAsync(scenes, brief, null, false, PacingProfile.BalancedDocumentary, CancellationToken.None);

        // Assert
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public async Task OptimizePacingAsync_Should_CompleteQuickly()
    {
        // Arrange - 5 scenes (simulating ~5 minute video)
        var scenes = Enumerable.Range(0, 5).Select(i => new Scene(
            i,
            $"Scene{i}",
            string.Join(" ", Enumerable.Repeat("word", 50)),
            TimeSpan.FromSeconds(i * 10),
            TimeSpan.FromSeconds(10)
        )).ToList();

        var brief = new Brief("Test", "general", "test", "casual", "en", Aspect.Widescreen16x9);

        // Act
        var startTime = DateTime.UtcNow;
        var result = await _optimizer.OptimizePacingAsync(scenes, brief, null, false, PacingProfile.BalancedDocumentary, CancellationToken.None);
        var elapsed = DateTime.UtcNow - startTime;

        // Assert - Should complete in under 10 seconds as per spec
        Assert.True(elapsed.TotalSeconds < 10, $"Analysis took {elapsed.TotalSeconds:F2}s, should be under 10s");
        Assert.Equal(5, result.TimingSuggestions.Count);
    }

    [Fact]
    public async Task OptimizePacingAsync_WithFailingLlm_Should_FallbackToHeuristics()
    {
        // Arrange
        var scenes = new List<Scene>
        {
            new Scene(0, "Test", "Test script.", TimeSpan.Zero, TimeSpan.FromSeconds(5))
        };

        var brief = new Brief("Test", "general", "test", "casual", "en", Aspect.Widescreen16x9);
        var failingLlm = new MockFailingLlmProvider();

        // Act
        var result = await _optimizer.OptimizePacingAsync(scenes, brief, failingLlm, false, PacingProfile.BalancedDocumentary, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.TimingSuggestions);
        Assert.False(result.LlmAnalysisSucceeded);
        // Should still have results from fallback heuristics
        Assert.True(result.TimingSuggestions[0].ImportanceScore > 0);
    }

    // Mock LLM Providers for testing
    private sealed class MockSuccessfulLlmProvider : ILlmProvider
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

        public Task<string> DraftScriptAsync(Brief brief, PlanSpec spec, CancellationToken ct)
        {
            return Task.FromResult("Mock script");
        }

        public Task<string> CompleteAsync(string prompt, CancellationToken ct)
        {
            return Task.FromResult("Mock response");
        }

        public Task<SceneAnalysisResult?> AnalyzeSceneImportanceAsync(
            string sceneText,
            string? previousSceneText,
            string videoGoal,
            CancellationToken ct)
        {
            return Task.FromResult<SceneAnalysisResult?>(new SceneAnalysisResult(
                Importance: 75.0,
                Complexity: 60.0,
                EmotionalIntensity: 50.0,
                InformationDensity: "medium",
                OptimalDurationSeconds: 12.0,
                TransitionType: "fade",
                Reasoning: "Mock analysis from LLM"
            ));
        }

        public Task<VisualPromptResult?> GenerateVisualPromptAsync(
            string sceneText,
            string? previousSceneText,
            string videoTone,
            VisualStyle targetStyle,
            CancellationToken ct)
        {
            return Task.FromResult<VisualPromptResult?>(null);
        }

        public Task<ContentComplexityAnalysisResult?> AnalyzeContentComplexityAsync(
            string sceneText,
            string? previousSceneText,
            string videoGoal,
            CancellationToken ct)
        {
            return Task.FromResult<ContentComplexityAnalysisResult?>(null);
        }

        public Task<SceneCoherenceResult?> AnalyzeSceneCoherenceAsync(string fromSceneText, string toSceneText, string videoGoal, CancellationToken ct)
        {
            return Task.FromResult<SceneCoherenceResult?>(null);
        }

        public Task<NarrativeArcResult?> ValidateNarrativeArcAsync(IReadOnlyList<string> sceneTexts, string videoGoal, string videoType, CancellationToken ct)
        {
            return Task.FromResult<NarrativeArcResult?>(null);
        }

        public Task<string?> GenerateTransitionTextAsync(string fromSceneText, string toSceneText, string videoGoal, CancellationToken ct)
        {
            return Task.FromResult<string?>(null);
        }
    }

    private sealed class MockFailingLlmProvider : ILlmProvider
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

        public Task<string> DraftScriptAsync(Brief brief, PlanSpec spec, CancellationToken ct)
        {
            throw new Exception("Mock LLM failure");
        }

        public Task<string> CompleteAsync(string prompt, CancellationToken ct)
        {
            throw new Exception("Mock LLM failure");
        }

        public Task<SceneAnalysisResult?> AnalyzeSceneImportanceAsync(
            string sceneText,
            string? previousSceneText,
            string videoGoal,
            CancellationToken ct)
        {
            return Task.FromResult<SceneAnalysisResult?>(null);
        }

        public Task<VisualPromptResult?> GenerateVisualPromptAsync(
            string sceneText,
            string? previousSceneText,
            string videoTone,
            VisualStyle targetStyle,
            CancellationToken ct)
        {
            return Task.FromResult<VisualPromptResult?>(null);
        }

        public Task<ContentComplexityAnalysisResult?> AnalyzeContentComplexityAsync(
            string sceneText,
            string? previousSceneText,
            string videoGoal,
            CancellationToken ct)
        {
            return Task.FromResult<ContentComplexityAnalysisResult?>(null);
        }

        public Task<SceneCoherenceResult?> AnalyzeSceneCoherenceAsync(string fromSceneText, string toSceneText, string videoGoal, CancellationToken ct)
        {
            return Task.FromResult<SceneCoherenceResult?>(null);
        }

        public Task<NarrativeArcResult?> ValidateNarrativeArcAsync(IReadOnlyList<string> sceneTexts, string videoGoal, string videoType, CancellationToken ct)
        {
            return Task.FromResult<NarrativeArcResult?>(null);
        }

        public Task<string?> GenerateTransitionTextAsync(string fromSceneText, string toSceneText, string videoGoal, CancellationToken ct)
        {
            return Task.FromResult<string?>(null);
        }
    }
}
