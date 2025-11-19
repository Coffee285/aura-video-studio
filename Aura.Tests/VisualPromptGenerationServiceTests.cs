using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aura.Core.Models;
using Aura.Core.Models.Narrative;
using Aura.Core.Models.Streaming;
using Aura.Core.Models.PacingModels;
using Aura.Core.Models.Visual;
using Aura.Core.Providers;
using Aura.Core.Services.Visual;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aura.Tests;

/// <summary>
/// Tests for the VisualPromptGenerationService
/// </summary>
public class VisualPromptGenerationServiceTests
{
    private readonly VisualPromptGenerationService _service;
    private readonly CinematographyKnowledgeBase _cinematography;
    private readonly VisualContinuityEngine _continuityEngine;
    private readonly PromptOptimizer _promptOptimizer;

    public VisualPromptGenerationServiceTests()
    {
        var logger = NullLogger<VisualPromptGenerationService>.Instance;
        var continuityLogger = NullLogger<VisualContinuityEngine>.Instance;
        var optimizerLogger = NullLogger<PromptOptimizer>.Instance;

        _cinematography = new CinematographyKnowledgeBase();
        _continuityEngine = new VisualContinuityEngine(continuityLogger);
        _promptOptimizer = new PromptOptimizer(optimizerLogger);
        _service = new VisualPromptGenerationService(logger, _cinematography, _continuityEngine, _promptOptimizer);
    }

    [Fact]
    public async Task GenerateVisualPromptForSceneAsync_WithoutLlm_Should_GenerateFallbackPrompt()
    {
        // Arrange
        var scene = new Scene(0, "Test Scene", "A person walks through a park on a sunny day.", TimeSpan.Zero, TimeSpan.FromSeconds(10));
        var tone = "professional";
        var style = VisualStyle.Cinematic;

        // Act
        var result = await _service.GenerateVisualPromptForSceneAsync(
            scene, null, tone, style, 50.0, 50.0, null, null, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0, result.SceneIndex);
        Assert.NotEmpty(result.DetailedDescription);
        Assert.NotEmpty(result.CompositionGuidelines);
        Assert.NotNull(result.Lighting);
        Assert.NotEmpty(result.ColorPalette);
        Assert.NotNull(result.Camera);
        Assert.NotEmpty(result.StyleKeywords);
        Assert.NotEmpty(result.NegativeElements);
        Assert.Equal(style, result.Style);
    }

    [Fact]
    public async Task GenerateVisualPromptForSceneAsync_WithLlm_Should_UseProviderResponse()
    {
        // Arrange
        var scene = new Scene(0, "Test Scene", "A dramatic sunset over mountains.", TimeSpan.Zero, TimeSpan.FromSeconds(10));
        var llm = new MockSuccessfulLlmProvider();
        var tone = "dramatic";
        var style = VisualStyle.Dramatic;

        // Act
        var result = await _service.GenerateVisualPromptForSceneAsync(
            scene, null, tone, style, 85.0, 75.0, null, llm, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0, result.SceneIndex);
        Assert.Contains("dramatic", result.DetailedDescription.ToLowerInvariant());
        Assert.NotNull(result.ProviderPrompts);
        Assert.NotNull(result.ProviderPrompts.StableDiffusion);
        Assert.NotNull(result.ProviderPrompts.DallE3);
        Assert.NotNull(result.ProviderPrompts.Midjourney);
    }

    [Fact]
    public async Task GenerateVisualPromptsAsync_Should_GenerateForAllScenes()
    {
        // Arrange
        var scenes = new List<Scene>
        {
            new Scene(0, "Scene 1", "Introduction in an office.", TimeSpan.Zero, TimeSpan.FromSeconds(5)),
            new Scene(1, "Scene 2", "Presentation to a team.", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)),
            new Scene(2, "Scene 3", "Closing remarks.", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5))
        };
        var brief = new Brief("Test", null, null, "professional", "en", Aspect.Widescreen16x9);

        // Act
        var results = await _service.GenerateVisualPromptsAsync(scenes, brief, null, null, CancellationToken.None);

        // Assert
        Assert.Equal(3, results.Count);
        Assert.All(results, prompt => Assert.NotNull(prompt));
        Assert.Equal(0, results[0].SceneIndex);
        Assert.Equal(1, results[1].SceneIndex);
        Assert.Equal(2, results[2].SceneIndex);
    }

    [Fact]
    public async Task GenerateVisualPromptForSceneAsync_HighImportance_Should_UseEnhancedQuality()
    {
        // Arrange
        var scene = new Scene(0, "Key Scene", "Critical moment in the story.", TimeSpan.Zero, TimeSpan.FromSeconds(10));
        var importance = 90.0;

        // Act
        var result = await _service.GenerateVisualPromptForSceneAsync(
            scene, null, "dramatic", VisualStyle.Dramatic, importance, 80.0, null, null, null, CancellationToken.None);

        // Assert
        Assert.Equal(VisualQualityTier.Premium, result.QualityTier);
        Assert.Equal(ShotType.CloseUp, result.Camera.ShotType);
    }

    [Fact]
    public async Task GenerateVisualPromptForSceneAsync_LowImportance_Should_UseBasicQuality()
    {
        // Arrange
        var scene = new Scene(0, "Filler Scene", "Background information.", TimeSpan.Zero, TimeSpan.FromSeconds(10));
        var importance = 30.0;

        // Act
        var result = await _service.GenerateVisualPromptForSceneAsync(
            scene, null, "casual", VisualStyle.Realistic, importance, 20.0, null, null, null, CancellationToken.None);

        // Assert
        Assert.Equal(VisualQualityTier.Basic, result.QualityTier);
    }

    [Fact]
    public async Task GenerateVisualPromptForSceneAsync_WithPreviousScene_Should_TrackContinuity()
    {
        // Arrange
        var previousScene = new Scene(0, "Scene 1", "Meeting in an office with John.", TimeSpan.Zero, TimeSpan.FromSeconds(5));
        var currentScene = new Scene(1, "Scene 2", "John continues the discussion in the office.", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        
        var previousPrompt = await _service.GenerateVisualPromptForSceneAsync(
            previousScene, null, "professional", VisualStyle.Realistic, 50.0, 50.0, null, null, null, CancellationToken.None);

        // Act
        var result = await _service.GenerateVisualPromptForSceneAsync(
            currentScene, previousScene, "professional", VisualStyle.Realistic, 50.0, 50.0, previousPrompt, null, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result.Continuity);
        Assert.True(result.Continuity.SimilarityScore > 0);
        Assert.NotEmpty(result.Continuity.LocationDetails.Concat(result.Continuity.CharacterAppearance));
    }

    [Fact]
    public void CinematographyKnowledgeBase_Should_RecommendAppropriateShotTypes()
    {
        // Arrange & Act
        var closeUpShot = _cinematography.RecommendShotType(importance: 85.0, emotionalIntensity: 75.0, sceneIndex: 5);
        var wideShot = _cinematography.RecommendShotType(importance: 35.0, emotionalIntensity: 30.0, sceneIndex: 0);
        var mediumShot = _cinematography.RecommendShotType(importance: 60.0, emotionalIntensity: 50.0, sceneIndex: 3);

        // Assert
        Assert.Equal(ShotType.CloseUp, closeUpShot);
        Assert.Equal(ShotType.WideShot, wideShot);
        Assert.Equal(ShotType.MediumShot, mediumShot);
    }

    [Fact]
    public void CinematographyKnowledgeBase_Should_RecommendDramaticAngleForDramaticTone()
    {
        // Arrange & Act
        var angle = _cinematography.RecommendCameraAngle("dramatic", 85.0);

        // Assert
        Assert.Equal(CameraAngle.LowAngle, angle);
    }

    [Fact]
    public void CinematographyKnowledgeBase_Should_ProvideKnowledgeForAllShotTypes()
    {
        // Arrange & Act & Assert
        foreach (ShotType shotType in Enum.GetValues(typeof(ShotType)))
        {
            var knowledge = _cinematography.GetShotKnowledge(shotType);
            Assert.NotNull(knowledge);
            Assert.NotEmpty(knowledge.Description);
            Assert.NotEmpty(knowledge.TypicalUsage);
            Assert.NotEmpty(knowledge.EmotionalImpact);
        }
    }

    [Fact]
    public void PromptOptimizer_Should_GenerateStableDiffusionPrompt()
    {
        // Arrange
        var prompt = new VisualPrompt
        {
            SceneIndex = 0,
            DetailedDescription = "A beautiful landscape with mountains",
            CompositionGuidelines = "rule of thirds",
            Lighting = new LightingSetup { Mood = "dramatic", Direction = "side", Quality = "hard", TimeOfDay = "golden hour" },
            ColorPalette = new[] { "#ffa500", "#ff8c00", "#ffd700" },
            Camera = new CameraSetup { ShotType = ShotType.WideShot, Angle = CameraAngle.LowAngle, DepthOfField = "deep" },
            StyleKeywords = new[] { "cinematic", "professional", "high quality" },
            QualityTier = VisualQualityTier.Premium,
            NegativeElements = new[] { "blurry", "low quality" }
        };

        // Act
        var optimized = _promptOptimizer.OptimizeForProviders(prompt);

        // Assert
        Assert.NotNull(optimized.StableDiffusion);
        Assert.Contains("mountains", optimized.StableDiffusion);
        Assert.Contains(":", optimized.StableDiffusion); // Emphasis syntax
        Assert.Contains("masterpiece", optimized.StableDiffusion); // Quality tags for Premium
        Assert.Contains("Negative prompt:", optimized.StableDiffusion);
    }

    [Fact]
    public void PromptOptimizer_Should_GenerateDallE3Prompt()
    {
        // Arrange
        var prompt = new VisualPrompt
        {
            SceneIndex = 0,
            DetailedDescription = "A sunset over a city skyline",
            Lighting = new LightingSetup { Mood = "warm", TimeOfDay = "golden hour" },
            Camera = new CameraSetup { ShotType = ShotType.MediumShot },
            StyleKeywords = new[] { "photorealistic", "detailed" }
        };

        // Act
        var optimized = _promptOptimizer.OptimizeForProviders(prompt);

        // Assert
        Assert.NotNull(optimized.DallE3);
        Assert.Contains("medium shot", optimized.DallE3.ToLowerInvariant());
        Assert.Contains("sunset", optimized.DallE3.ToLowerInvariant());
        Assert.Contains("warm", optimized.DallE3);
    }

    [Fact]
    public void PromptOptimizer_Should_GenerateMidjourneyPrompt()
    {
        // Arrange
        var prompt = new VisualPrompt
        {
            SceneIndex = 0,
            DetailedDescription = "A futuristic cityscape",
            Camera = new CameraSetup { ShotType = ShotType.WideShot, DepthOfField = "shallow" },
            StyleKeywords = new[] { "sci-fi", "neon", "futuristic" },
            QualityTier = VisualQualityTier.Enhanced,
            Style = VisualStyle.Cinematic
        };

        // Act
        var optimized = _promptOptimizer.OptimizeForProviders(prompt);

        // Assert
        Assert.NotNull(optimized.Midjourney);
        Assert.Contains("--ar", optimized.Midjourney); // Aspect ratio parameter
        Assert.Contains("--q", optimized.Midjourney); // Quality parameter
        Assert.Contains("bokeh", optimized.Midjourney); // Depth of field
    }

    [Fact]
    public void VisualContinuityEngine_Should_CalculateHighSimilarityForSimilarScenes()
    {
        // Arrange
        var scene1 = new Scene(0, "Office Meeting", "John discusses the project in the office.", TimeSpan.Zero, TimeSpan.FromSeconds(5));
        var scene2 = new Scene(1, "Office Discussion", "John continues the project discussion in the office.", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        var prompt1 = new VisualPrompt
        {
            ColorPalette = new[] { "#2c3e50", "#ecf0f1", "#3498db" },
            Lighting = new LightingSetup { TimeOfDay = "day" }
        };

        // Act
        var continuity = _continuityEngine.AnalyzeContinuity(scene2, scene1, prompt1, new List<string> { "same office", "John" });

        // Assert
        Assert.NotNull(continuity);
        Assert.True(continuity.SimilarityScore >= 60);
    }

    [Fact]
    public void VisualContinuityEngine_Should_CalculateLowSimilarityForDifferentScenes()
    {
        // Arrange
        var scene1 = new Scene(0, "Office", "Meeting in corporate office.", TimeSpan.Zero, TimeSpan.FromSeconds(5));
        var scene2 = new Scene(1, "Beach", "Vacation on a tropical beach.", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        var prompt1 = new VisualPrompt
        {
            ColorPalette = new[] { "#2c3e50" },
            Lighting = new LightingSetup { TimeOfDay = "day" }
        };

        // Act
        var continuity = _continuityEngine.AnalyzeContinuity(scene2, scene1, prompt1, null);

        // Assert
        Assert.NotNull(continuity);
        Assert.True(continuity.SimilarityScore < 40);
    }

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
                Importance: 70.0,
                Complexity: 55.0,
                EmotionalIntensity: 60.0,
                InformationDensity: "medium",
                OptimalDurationSeconds: 10.0,
                TransitionType: "fade",
                Reasoning: "Mock analysis"
            ));
        }

        public Task<VisualPromptResult?> GenerateVisualPromptAsync(
            string sceneText,
            string? previousSceneText,
            string videoTone,
            VisualStyle targetStyle,
            CancellationToken ct)
        {
            return Task.FromResult<VisualPromptResult?>(new VisualPromptResult(
                DetailedDescription: "A dramatic sunset over mountains with vibrant orange and purple hues",
                CompositionGuidelines: "rule of thirds with mountains in lower third, sky in upper two thirds",
                LightingMood: "dramatic",
                LightingDirection: "side",
                LightingQuality: "hard",
                TimeOfDay: "golden hour",
                ColorPalette: new[] { "#ffa500", "#ff8c00", "#8b008b" },
                ShotType: "wide shot",
                CameraAngle: "eye level",
                DepthOfField: "deep",
                StyleKeywords: new[] { "cinematic", "dramatic", "atmospheric", "vivid", "professional" },
                NegativeElements: new[] { "blurry", "oversaturated", "noisy" },
                ContinuityElements: new[] { "same location", "consistent lighting" },
                Reasoning: "Dramatic tone requires strong lighting and vivid colors"
            ));
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
