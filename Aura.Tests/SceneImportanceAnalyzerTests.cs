using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Aura.Core.Models;
using Aura.Core.Models.Narrative;
using Aura.Core.Models.PacingModels;
using Aura.Core.Models.Streaming;
using Aura.Core.Models.Visual;
using Aura.Core.Providers;
using Aura.Core.Services.PacingServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aura.Tests;

/// <summary>
/// Tests for the SceneImportanceAnalyzer
/// </summary>
public class SceneImportanceAnalyzerTests
{
    private readonly SceneImportanceAnalyzer _analyzer;

    public SceneImportanceAnalyzerTests()
    {
        var logger = NullLogger<SceneImportanceAnalyzer>.Instance;
        _analyzer = new SceneImportanceAnalyzer(logger);
    }

    [Fact]
    public async Task AnalyzeSceneAsync_WithSuccessfulLlm_Should_ReturnAnalysis()
    {
        // Arrange
        var scene = new Scene(0, "Test", "This is a test scene.", TimeSpan.Zero, TimeSpan.FromSeconds(5));
        var llm = new MockLlmProvider(shouldSucceed: true);

        // Act
        var result = await _analyzer.AnalyzeSceneAsync(llm, scene, null, "test goal", CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.AnalyzedWithLlm);
        Assert.InRange(result.Importance, 0, 100);
        Assert.InRange(result.Complexity, 0, 100);
        Assert.InRange(result.EmotionalIntensity, 0, 100);
        Assert.True(result.OptimalDurationSeconds > 0);
        Assert.NotEmpty(result.Reasoning);
    }

    [Fact]
    public async Task AnalyzeSceneAsync_WithFailingLlm_Should_ReturnNull()
    {
        // Arrange
        var scene = new Scene(0, "Test", "Test scene.", TimeSpan.Zero, TimeSpan.FromSeconds(5));
        var llm = new MockLlmProvider(shouldSucceed: false);

        // Act
        var result = await _analyzer.AnalyzeSceneAsync(llm, scene, null, "test goal", CancellationToken.None);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task AnalyzeScenesAsync_Should_AnalyzeAllScenes()
    {
        // Arrange
        var scenes = new List<Scene>
        {
            new Scene(0, "Scene1", "First scene.", TimeSpan.Zero, TimeSpan.FromSeconds(5)),
            new Scene(1, "Scene2", "Second scene.", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)),
            new Scene(2, "Scene3", "Third scene.", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5))
        };

        var llm = new MockLlmProvider(shouldSucceed: true);

        // Act
        var results = await _analyzer.AnalyzeScenesAsync(llm, scenes, "test goal", CancellationToken.None);

        // Assert
        Assert.Equal(3, results.Count);
        foreach (var result in results)
        {
            Assert.InRange(result.SceneIndex, 0, 2);
            Assert.True(result.AnalyzedWithLlm);
        }
    }

    [Fact]
    public async Task AnalyzeScenesAsync_WithPartialFailure_Should_UseFallback()
    {
        // Arrange
        var scenes = new List<Scene>
        {
            new Scene(0, "Scene1", "First scene.", TimeSpan.Zero, TimeSpan.FromSeconds(5)),
            new Scene(1, "Scene2", "Second scene.", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5))
        };

        var llm = new MockPartiallyFailingLlmProvider();

        // Act
        var results = await _analyzer.AnalyzeScenesAsync(llm, scenes, "test goal", CancellationToken.None);

        // Assert
        Assert.Equal(2, results.Count);
        
        // First scene should succeed with LLM
        Assert.True(results[0].AnalyzedWithLlm);
        
        // Second scene should fail and use fallback
        Assert.False(results[1].AnalyzedWithLlm);
        Assert.Contains("Fallback", results[1].Reasoning);
    }

    [Fact]
    public async Task AnalyzeScenesAsync_WithCompleteLlmFailure_Should_UseAllFallbacks()
    {
        // Arrange
        var scenes = new List<Scene>
        {
            new Scene(0, "S1", "Content.", TimeSpan.Zero, TimeSpan.FromSeconds(5)),
            new Scene(1, "S2", "More content.", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5))
        };

        var llm = new MockLlmProvider(shouldSucceed: false);

        // Act
        var results = await _analyzer.AnalyzeScenesAsync(llm, scenes, "test goal", CancellationToken.None);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.False(r.AnalyzedWithLlm));
        Assert.All(results, r => Assert.Contains("Fallback", r.Reasoning));
    }

    [Fact]
    public async Task AnalyzeScenesAsync_Should_AssignHigherImportanceToHookAndConclusion()
    {
        // Arrange
        var scenes = new List<Scene>
        {
            new Scene(0, "Hook", "Hook content.", TimeSpan.Zero, TimeSpan.FromSeconds(5)),
            new Scene(1, "Middle", "Middle content.", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)),
            new Scene(2, "Conclusion", "Conclusion content.", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5))
        };

        var llm = new MockLlmProvider(shouldSucceed: false); // Force fallback to test heuristics

        // Act
        var results = await _analyzer.AnalyzeScenesAsync(llm, scenes, "test goal", CancellationToken.None);

        // Assert
        var hookImportance = results[0].Importance;
        var middleImportance = results[1].Importance;
        var conclusionImportance = results[2].Importance;

        Assert.True(hookImportance > middleImportance, "Hook should be more important than middle");
        Assert.True(conclusionImportance > middleImportance, "Conclusion should be more important than middle");
    }

    [Fact]
    public async Task AnalyzeScenesAsync_Should_AdjustComplexityBasedOnWordCount()
    {
        // Arrange
        var shortScript = "Short.";
        var longScript = string.Join(" ", System.Linq.Enumerable.Repeat("word", 150));

        var scenes = new List<Scene>
        {
            new Scene(0, "Short", shortScript, TimeSpan.Zero, TimeSpan.FromSeconds(5)),
            new Scene(1, "Long", longScript, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30))
        };

        var llm = new MockLlmProvider(shouldSucceed: false); // Force fallback

        // Act
        var results = await _analyzer.AnalyzeScenesAsync(llm, scenes, "test goal", CancellationToken.None);

        // Assert
        var shortComplexity = results[0].Complexity;
        var longComplexity = results[1].Complexity;

        Assert.True(longComplexity > shortComplexity, 
            $"Long scene complexity ({longComplexity}) should be higher than short scene ({shortComplexity})");
    }

    [Fact]
    public async Task AnalyzeScenesAsync_Should_DetectInformationDensity()
    {
        // Arrange
        var lowDensityScript = "Simple content.";
        var highDensityScript = string.Join(" ", System.Linq.Enumerable.Repeat("word", 120));

        var scenes = new List<Scene>
        {
            new Scene(0, "Low", lowDensityScript, TimeSpan.Zero, TimeSpan.FromSeconds(5)),
            new Scene(1, "High", highDensityScript, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10))
        };

        var llm = new MockLlmProvider(shouldSucceed: false); // Force fallback

        // Act
        var results = await _analyzer.AnalyzeScenesAsync(llm, scenes, "test goal", CancellationToken.None);

        // Assert
        Assert.Equal(InformationDensity.Low, results[0].InformationDensity);
        Assert.Equal(InformationDensity.High, results[1].InformationDensity);
    }

    [Fact]
    public async Task AnalyzeSceneAsync_WithRetries_Should_EventuallySucceed()
    {
        // Arrange
        var scene = new Scene(0, "Test", "Test.", TimeSpan.Zero, TimeSpan.FromSeconds(5));
        var llm = new MockRetryingLlmProvider(failuresBeforeSuccess: 1);

        // Act
        var result = await _analyzer.AnalyzeSceneAsync(llm, scene, null, "test", CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.AnalyzedWithLlm);
    }

    // Mock LLM Providers for testing
    private sealed class MockLlmProvider : ILlmProvider
    {
        private readonly bool _shouldSucceed;

        public MockLlmProvider(bool shouldSucceed)
        {
            _shouldSucceed = shouldSucceed;
        }

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
            if (!_shouldSucceed)
                return Task.FromResult<SceneAnalysisResult?>(null);

            return Task.FromResult<SceneAnalysisResult?>(new SceneAnalysisResult(
                Importance: 70.0,
                Complexity: 55.0,
                EmotionalIntensity: 60.0,
                InformationDensity: "medium",
                OptimalDurationSeconds: 10.0,
                TransitionType: "fade",
                Reasoning: "Mock LLM analysis"
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

    private sealed class MockPartiallyFailingLlmProvider : ILlmProvider
    {
        private int _callCount;

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
            _callCount++;
            
            // First call succeeds, subsequent calls fail
            if (_callCount == 1)
            {
                return Task.FromResult<SceneAnalysisResult?>(new SceneAnalysisResult(
                    Importance: 75.0,
                    Complexity: 60.0,
                    EmotionalIntensity: 65.0,
                    InformationDensity: "medium",
                    OptimalDurationSeconds: 12.0,
                    TransitionType: "cut",
                    Reasoning: "First analysis succeeded"
                ));
            }

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

    private sealed class MockRetryingLlmProvider : ILlmProvider
    {
        private readonly int _failuresBeforeSuccess;
        private int _attemptCount;

        public MockRetryingLlmProvider(int failuresBeforeSuccess)
        {
            _failuresBeforeSuccess = failuresBeforeSuccess;
        }

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
            _attemptCount++;

            if (_attemptCount <= _failuresBeforeSuccess)
            {
                return Task.FromResult<SceneAnalysisResult?>(null);
            }

            return Task.FromResult<SceneAnalysisResult?>(new SceneAnalysisResult(
                Importance: 65.0,
                Complexity: 50.0,
                EmotionalIntensity: 55.0,
                InformationDensity: "low",
                OptimalDurationSeconds: 8.0,
                TransitionType: "dissolve",
                Reasoning: "Succeeded after retries"
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
}
