using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aura.Core.Models;
using Aura.Core.Models.Streaming;
using Aura.Core.Orchestrator;
using Aura.Core.Providers;
using Microsoft.Extensions.Logging;
using Xunit;
using TimelineModel = Aura.Core.Providers.Timeline;

namespace Aura.Tests.Integration;

/// <summary>
/// Integration tests demonstrating the provider pipeline architecture
/// Tests the flow from brief → script → TTS → visuals → composition without requiring FFmpeg
/// </summary>
public class ProviderPipelineArchitectureTests
{
    [Fact]
    public async Task ProviderPipeline_WithMockedProviders_ExecutesSequentially()
    {
        // Arrange - Create mocked providers that track execution order
        var executionOrder = new List<string>();
        var mockLlm = new MockLlmProvider(executionOrder);
        var mockTts = new MockTtsProvider(executionOrder);
        var mockImage = new MockImageProvider(executionOrder);
        var mockComposer = new MockVideoComposer(executionOrder);

        // Act - Call providers in the expected pipeline sequence
        var brief = new Brief("Test Topic", "General", "Inform", "Professional", "English", Aspect.Widescreen16x9);
        var planSpec = new PlanSpec(TimeSpan.FromSeconds(30), Pacing.Conversational, Density.Balanced, "Modern");
        var voiceSpec = new VoiceSpec("test-voice", 1.0, 1.0, PauseStyle.Natural);
        var renderSpec = new RenderSpec(
            new Resolution(1920, 1080), "mp4", 5000, 192, 30, "H264", 75, true);

        // Script generation
        var script = await mockLlm.DraftScriptAsync(brief, planSpec, CancellationToken.None);
        Assert.NotEmpty(script);

        // TTS synthesis
        var scriptLines = new List<ScriptLine>
        {
            new ScriptLine(0, "Scene 1 text", TimeSpan.Zero, TimeSpan.FromSeconds(5)),
            new ScriptLine(1, "Scene 2 text", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5))
        };
        var audioPath = await mockTts.SynthesizeAsync(scriptLines, voiceSpec, CancellationToken.None);
        Assert.NotEmpty(audioPath);

        // Visual generation
        var scene = new Scene(0, "Test Scene", "Test content", TimeSpan.Zero, TimeSpan.FromSeconds(5));
        var visualSpec = new VisualSpec("Modern", brief.Aspect, Array.Empty<string>());
        var assets = await mockImage.FetchOrGenerateAsync(scene, visualSpec, CancellationToken.None);
        Assert.NotEmpty(assets);

        // Video composition
        var timeline = new TimelineModel(
            Scenes: new[] { scene },
            SceneAssets: new Dictionary<int, IReadOnlyList<Asset>> { [0] = assets },
            NarrationPath: audioPath,
            MusicPath: string.Empty,
            SubtitlesPath: null);
        var videoPath = await mockComposer.RenderAsync(timeline, renderSpec, null, CancellationToken.None);
        Assert.NotEmpty(videoPath);

        // Assert - Verify execution order matches expected pipeline
        Assert.Equal(4, executionOrder.Count);
        Assert.Equal("LLM: DraftScript", executionOrder[0]);
        Assert.Equal("TTS: Synthesize", executionOrder[1]);
        Assert.Equal("Image: Generate", executionOrder[2]);
        Assert.Equal("Video: Render", executionOrder[3]);
    }

    [Fact]
    public async Task ProviderPipeline_WithProviderFailure_ThrowsExpectedException()
    {
        // Arrange - Create a failing LLM provider
        var failingLlm = new FailingMockLlmProvider();
        var brief = new Brief("Test", "General", "Inform", "Professional", "English", Aspect.Widescreen16x9);
        var planSpec = new PlanSpec(TimeSpan.FromSeconds(30), Pacing.Conversational, Density.Balanced, "Modern");

        // Act & Assert - Verify proper exception is thrown
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await failingLlm.DraftScriptAsync(brief, planSpec, CancellationToken.None);
        });
    }

    [Fact]
    public async Task ProviderPipeline_WithMultipleProviders_SupportsProviderSelection()
    {
        // Arrange - Multiple LLM providers in priority order
        var providers = new List<ILlmProvider>
        {
            new MockLlmProvider(new List<string>()),
            new MockLlmProvider(new List<string>())
        };

        // Act - Simulate provider selection (first available)
        var selectedProvider = providers.First();
        var brief = new Brief("Test", "General", "Inform", "Professional", "English", Aspect.Widescreen16x9);
        var planSpec = new PlanSpec(TimeSpan.FromSeconds(30), Pacing.Conversational, Density.Balanced, "Modern");
        var script = await selectedProvider.DraftScriptAsync(brief, planSpec, CancellationToken.None);

        // Assert - Verify provider was used successfully
        Assert.NotEmpty(script);
        Assert.Contains("Scene", script);
    }

    [Fact]
    public void ProviderPipeline_Architecture_FollowsInterfaceContract()
    {
        // Arrange - Create instances of all provider types
        var llmProvider = new MockLlmProvider(new List<string>());
        var ttsProvider = new MockTtsProvider(new List<string>());
        var imageProvider = new MockImageProvider(new List<string>());
        var videoComposer = new MockVideoComposer(new List<string>());

        // Assert - Verify all implement their respective interfaces
        Assert.IsAssignableFrom<ILlmProvider>(llmProvider);
        Assert.IsAssignableFrom<ITtsProvider>(ttsProvider);
        Assert.IsAssignableFrom<IImageProvider>(imageProvider);
        Assert.IsAssignableFrom<IVideoComposer>(videoComposer);
    }

    // Mock provider implementations for testing
    private sealed class MockLlmProvider : ILlmProvider
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

        private readonly List<string> _executionOrder;

        public MockLlmProvider(List<string> executionOrder)
        {
            _executionOrder = executionOrder;
        }

        public Task<string> DraftScriptAsync(Brief brief, PlanSpec spec, CancellationToken ct)
        {
            _executionOrder.Add("LLM: DraftScript");
            return Task.FromResult("# Test Script\n\n## Scene 1\nTest content for scene 1.\n\n## Scene 2\nTest content for scene 2.");
        }

        public Task<string> CompleteAsync(string prompt, CancellationToken ct) =>
            Task.FromResult("Completion result");

        public Task<SceneAnalysisResult?> AnalyzeSceneImportanceAsync(string sceneText, string? previousSceneText, string videoGoal, CancellationToken ct) =>
            Task.FromResult<SceneAnalysisResult?>(null);

        public Task<VisualPromptResult?> GenerateVisualPromptAsync(string sceneText, string? previousSceneText, string videoTone, Aura.Core.Models.Visual.VisualStyle targetStyle, CancellationToken ct) =>
            Task.FromResult<VisualPromptResult?>(null);

        public Task<ContentComplexityAnalysisResult?> AnalyzeContentComplexityAsync(string sceneText, string? previousSceneText, string videoGoal, CancellationToken ct) =>
            Task.FromResult<ContentComplexityAnalysisResult?>(null);

        public Task<SceneCoherenceResult?> AnalyzeSceneCoherenceAsync(string fromSceneText, string toSceneText, string videoGoal, CancellationToken ct) =>
            Task.FromResult<SceneCoherenceResult?>(null);

        public Task<NarrativeArcResult?> ValidateNarrativeArcAsync(IReadOnlyList<string> sceneTexts, string videoGoal, string videoType, CancellationToken ct) =>
            Task.FromResult<NarrativeArcResult?>(null);

        public Task<string?> GenerateTransitionTextAsync(string fromSceneText, string toSceneText, string videoGoal, CancellationToken ct) =>
            Task.FromResult<string?>(null);
    }

    private sealed class MockTtsProvider : ITtsProvider
    {
        private readonly List<string> _executionOrder;

        public MockTtsProvider(List<string> executionOrder)
        {
            _executionOrder = executionOrder;
        }

        public Task<IReadOnlyList<string>> GetAvailableVoicesAsync() =>
            Task.FromResult<IReadOnlyList<string>>(new[] { "test-voice-1", "test-voice-2" });

        public Task<string> SynthesizeAsync(IEnumerable<ScriptLine> lines, VoiceSpec spec, CancellationToken ct)
        {
            _executionOrder.Add("TTS: Synthesize");
            return Task.FromResult("/tmp/test-audio.wav");
        }
    }

    private sealed class MockImageProvider : IImageProvider
    {
        private readonly List<string> _executionOrder;

        public MockImageProvider(List<string> executionOrder)
        {
            _executionOrder = executionOrder;
        }

        public Task<IReadOnlyList<Asset>> FetchOrGenerateAsync(Scene scene, VisualSpec spec, CancellationToken ct)
        {
            _executionOrder.Add("Image: Generate");
            var assets = new List<Asset>
            {
                new Asset("image", "/tmp/test-image-1.jpg", null, null),
                new Asset("image", "/tmp/test-image-2.jpg", null, null)
            };
            return Task.FromResult<IReadOnlyList<Asset>>(assets);
        }
    }

    private sealed class MockVideoComposer : IVideoComposer
    {
        private readonly List<string> _executionOrder;

        public MockVideoComposer(List<string> executionOrder)
        {
            _executionOrder = executionOrder;
        }

        public Task<string> RenderAsync(TimelineModel timeline, RenderSpec spec, IProgress<RenderProgress> progress, CancellationToken ct)
        {
            _executionOrder.Add("Video: Render");
            progress?.Report(new RenderProgress(100, TimeSpan.Zero, TimeSpan.Zero, "Complete"));
            return Task.FromResult("/tmp/test-output.mp4");
        }
    }

    private sealed class FailingMockLlmProvider : ILlmProvider
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
            throw new InvalidOperationException("Provider unavailable");
        }

        public Task<string> CompleteAsync(string prompt, CancellationToken ct) =>
            throw new InvalidOperationException("Provider unavailable");

        public Task<SceneAnalysisResult?> AnalyzeSceneImportanceAsync(string sceneText, string? previousSceneText, string videoGoal, CancellationToken ct) =>
            throw new InvalidOperationException("Provider unavailable");

        public Task<VisualPromptResult?> GenerateVisualPromptAsync(string sceneText, string? previousSceneText, string videoTone, Aura.Core.Models.Visual.VisualStyle targetStyle, CancellationToken ct) =>
            throw new InvalidOperationException("Provider unavailable");

        public Task<ContentComplexityAnalysisResult?> AnalyzeContentComplexityAsync(string sceneText, string? previousSceneText, string videoGoal, CancellationToken ct) =>
            throw new InvalidOperationException("Provider unavailable");

        public Task<SceneCoherenceResult?> AnalyzeSceneCoherenceAsync(string fromSceneText, string toSceneText, string videoGoal, CancellationToken ct) =>
            throw new InvalidOperationException("Provider unavailable");

        public Task<NarrativeArcResult?> ValidateNarrativeArcAsync(IReadOnlyList<string> sceneTexts, string videoGoal, string videoType, CancellationToken ct) =>
            throw new InvalidOperationException("Provider unavailable");

        public Task<string?> GenerateTransitionTextAsync(string fromSceneText, string toSceneText, string videoGoal, CancellationToken ct) =>
            throw new InvalidOperationException("Provider unavailable");
    }
}
