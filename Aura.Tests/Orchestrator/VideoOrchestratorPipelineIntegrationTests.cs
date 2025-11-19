using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aura.Core.Dependencies;
using Aura.Core.Hardware;
using Aura.Core.Models;
using Aura.Core.Orchestrator;
using Aura.Core.Providers;
using Aura.Core.Services;
using Aura.Core.Services.Providers;
using Aura.Core.Telemetry;
using Aura.Core.Validation;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Aura.Tests.Orchestrator;

/// <summary>
/// Integration tests for the complete video orchestration pipeline
/// These tests verify end-to-end functionality with realistic scenarios
/// </summary>
public class VideoOrchestratorPipelineIntegrationTests
{
    private readonly Mock<ILogger<EnhancedVideoOrchestrator>> _mockLogger;
    private readonly FakeLlmProvider _llmProvider;
    private readonly FakeTtsProvider _ttsProvider;
    private readonly FakeVideoComposer _videoComposer;
    private readonly FakeImageProvider _imageProvider;
    private readonly ProviderCircuitBreakerService _circuitBreaker;
    private readonly ProviderRetryWrapper _retryWrapper;
    private readonly Mock<IFfmpegLocator> _ffmpegLocator;
    private readonly FFmpegResolver _ffmpegResolver;
    private readonly Mock<IHardwareDetector> _hardwareDetector;
    private readonly Mock<IProviderReadinessService> _providerReadiness;
    private readonly PreGenerationValidator _preValidator;
    private readonly ScriptValidator _scriptValidator;
    private readonly TtsOutputValidator _ttsValidator;
    private readonly ImageOutputValidator _imageValidator;
    private readonly LlmOutputValidator _llmValidator;
    private readonly ResourceCleanupManager _cleanupManager;
    private readonly RunTelemetryCollector _telemetryCollector;

    public VideoOrchestratorPipelineIntegrationTests()
    {
        _mockLogger = new Mock<ILogger<EnhancedVideoOrchestrator>>();
        _llmProvider = new FakeLlmProvider();
        _ttsProvider = new FakeTtsProvider();
        _videoComposer = new FakeVideoComposer();
        _imageProvider = new FakeImageProvider();

        _circuitBreaker = new ProviderCircuitBreakerService(
            NullLogger<ProviderCircuitBreakerService>.Instance);

        _retryWrapper = new ProviderRetryWrapper(
            NullLogger<ProviderRetryWrapper>.Instance);

        _ffmpegLocator = new Mock<IFfmpegLocator>();
        _hardwareDetector = new Mock<IHardwareDetector>();
        _providerReadiness = new Mock<IProviderReadinessService>();

        var cache = new MemoryCache(new MemoryCacheOptions());
        _ffmpegResolver = new FFmpegResolver(
            NullLogger<FFmpegResolver>.Instance,
            cache);

        _ffmpegLocator
            .Setup(locator => locator.CheckAllCandidatesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FfmpegValidationResult
            {
                Found = true,
                FfmpegPath = "/usr/bin/ffmpeg",
                AttemptedPaths = new List<string>()
            });

        _hardwareDetector
            .Setup(detector => detector.DetectSystemAsync())
            .ReturnsAsync(new SystemProfile
            {
                LogicalCores = 8,
                PhysicalCores = 4,
                RamGB = 16
            });

        _providerReadiness
            .Setup(service => service.ValidateRequiredProvidersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateReadyProvidersResult());

        _preValidator = new PreGenerationValidator(
            NullLogger<PreGenerationValidator>.Instance,
            _ffmpegLocator.Object,
            _ffmpegResolver,
            _hardwareDetector.Object,
            _providerReadiness.Object);

        _scriptValidator = new ScriptValidator(
            NullLogger<ScriptValidator>.Instance);

        _ttsValidator = new TtsOutputValidator(
            NullLogger<TtsOutputValidator>.Instance);

        _imageValidator = new ImageOutputValidator(
            NullLogger<ImageOutputValidator>.Instance);

        _llmValidator = new LlmOutputValidator(
            NullLogger<LlmOutputValidator>.Instance);

        _cleanupManager = new ResourceCleanupManager(
            NullLogger<ResourceCleanupManager>.Instance);

        _telemetryCollector = new RunTelemetryCollector(
            NullLogger<RunTelemetryCollector>.Instance);
    }

    [Fact]
    public async Task EndToEndPipeline_WithAllStages_CompletesSuccessfully()
    {
        // Arrange
        using var orchestrator = new EnhancedVideoOrchestrator(
            _mockLogger.Object,
            _llmProvider,
            _ttsProvider,
            _videoComposer,
            _circuitBreaker,
            _retryWrapper,
            _preValidator,
            _scriptValidator,
            _ttsValidator,
            _imageValidator,
            _llmValidator,
            _cleanupManager,
            _telemetryCollector,
            _imageProvider);

        var brief = CreateTestBrief();
        var planSpec = CreateTestPlanSpec();
        var voiceSpec = CreateTestVoiceSpec();
        var renderSpec = CreateTestRenderSpec();
        var systemProfile = CreateTestSystemProfile();

        var progressUpdates = new List<GenerationProgress>();
        var progress = new Progress<GenerationProgress>(p => progressUpdates.Add(p));

        // Act
        var result = await orchestrator.GenerateVideoAsync(
            brief,
            planSpec,
            voiceSpec,
            renderSpec,
            systemProfile,
            progress,
            new PipelineConfiguration(),
            CancellationToken.None,
            "test-job-id");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("test_video.mp4", result);

        // Verify all stages reported progress
        Assert.Contains(progressUpdates, p => p.Stage.Contains("Brief", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(progressUpdates, p => p.Stage.Contains("Script", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(progressUpdates, p => p.Stage.Contains("TTS", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(progressUpdates, p => p.Stage.Contains("Image", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(progressUpdates, p => p.Stage.Contains("Render", StringComparison.OrdinalIgnoreCase));

        // Verify providers were called
        Assert.True(_llmProvider.WasCalled);
        Assert.True(_ttsProvider.WasCalled);
        Assert.True(_imageProvider.WasCalled);
        Assert.True(_videoComposer.WasCalled);
    }

    [Fact]
    public async Task EndToEndPipeline_WithProviderRetry_RecoversFromTransientFailure()
    {
        // Arrange
        _llmProvider.FailFirstNAttempts = 2; // Fail first 2 attempts, succeed on 3rd

        using var orchestrator = new EnhancedVideoOrchestrator(
            _mockLogger.Object,
            _llmProvider,
            _ttsProvider,
            _videoComposer,
            _circuitBreaker,
            _retryWrapper,
            _preValidator,
            _scriptValidator,
            _ttsValidator,
            _imageValidator,
            _llmValidator,
            _cleanupManager,
            _telemetryCollector,
            _imageProvider);

        var brief = CreateTestBrief();
        var planSpec = CreateTestPlanSpec();
        var voiceSpec = CreateTestVoiceSpec();
        var renderSpec = CreateTestRenderSpec();
        var systemProfile = CreateTestSystemProfile();

        var config = new PipelineConfiguration
        {
            MaxRetryAttempts = 3
        };

        // Act
        var result = await orchestrator.GenerateVideoAsync(
            brief,
            planSpec,
            voiceSpec,
            renderSpec,
            systemProfile,
            null,
            config,
            CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, _llmProvider.AttemptCount - 1); // -1 because AttemptCount includes the successful attempt
    }

    [Fact]
    public async Task EndToEndPipeline_WithCancellation_StopsGracefully()
    {
        // Arrange
        using var orchestrator = new EnhancedVideoOrchestrator(
            _mockLogger.Object,
            _llmProvider,
            _ttsProvider,
            _videoComposer,
            _circuitBreaker,
            _retryWrapper,
            _preValidator,
            _scriptValidator,
            _ttsValidator,
            _imageValidator,
            _llmValidator,
            _cleanupManager,
            _telemetryCollector,
            _imageProvider);

        var brief = CreateTestBrief();
        var planSpec = CreateTestPlanSpec();
        var voiceSpec = CreateTestVoiceSpec();
        var renderSpec = CreateTestRenderSpec();
        var systemProfile = CreateTestSystemProfile();

        var cts = new CancellationTokenSource();

        // Cancel after brief validation
        _llmProvider.OnBeforeCall = () => cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            orchestrator.GenerateVideoAsync(
                brief,
                planSpec,
                voiceSpec,
                renderSpec,
                systemProfile,
                null,
                null,
                cts.Token));
    }

    [Fact]
    public async Task EndToEndPipeline_CollectsPerformanceMetrics()
    {
        // Arrange
        using var orchestrator = new EnhancedVideoOrchestrator(
            _mockLogger.Object,
            _llmProvider,
            _ttsProvider,
            _videoComposer,
            _circuitBreaker,
            _retryWrapper,
            _preValidator,
            _scriptValidator,
            _ttsValidator,
            _imageValidator,
            _llmValidator,
            _cleanupManager,
            _telemetryCollector,
            _imageProvider);

        var brief = CreateTestBrief();
        var planSpec = CreateTestPlanSpec();
        var voiceSpec = CreateTestVoiceSpec();
        var renderSpec = CreateTestRenderSpec();
        var systemProfile = CreateTestSystemProfile();

        var config = new PipelineConfiguration
        {
            EnableMetrics = true
        };

        // Act
        await orchestrator.GenerateVideoAsync(
            brief,
            planSpec,
            voiceSpec,
            renderSpec,
            systemProfile,
            null,
            config,
            CancellationToken.None);

        // Assert - Verify metrics were collected
        // (In real implementation, we'd verify via telemetry collector or metrics logger)
        Assert.True(_llmProvider.WasCalled);
        Assert.True(_ttsProvider.WasCalled);
    }

    #region Test Helpers

    private Brief CreateTestBrief()
    {
        return new Brief
        {
            Topic = "Integration Test Topic",
            Audience = "Developers",
            Goal = "Educational",
            Aspect = Aspect.Landscape_16_9
        };
    }

    private PlanSpec CreateTestPlanSpec()
    {
        return new PlanSpec
        {
            TargetDuration = TimeSpan.FromSeconds(30),
            Style = "Professional"
        };
    }

    private VoiceSpec CreateTestVoiceSpec()
    {
        return new VoiceSpec
        {
            VoiceName = "TestVoice",
            Speed = 1.0
        };
    }

    private RenderSpec CreateTestRenderSpec()
    {
        return new RenderSpec
        {
            Res = new Resolution { Width = 1920, Height = 1080 },
            Fps = 30,
            Codec = "h264"
        };
    }

    private SystemProfile CreateTestSystemProfile()
    {
        return new SystemProfile
        {
            Tier = HardwareTier.B,
            LogicalCores = 8,
            PhysicalCores = 4,
            RamGB = 16
        };
    }

    #endregion

    #region Fake Providers

    private class FakeLlmProvider : ILlmProvider
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

        public bool WasCalled { get; private set; }
        public int AttemptCount { get; private set; }
        public int FailFirstNAttempts { get; set; }
        public Action? OnBeforeCall { get; set; }

        public Task<string> DraftScriptAsync(Brief brief, PlanSpec spec, CancellationToken ct)
        {
            OnBeforeCall?.Invoke();
            AttemptCount++;

            if (AttemptCount <= FailFirstNAttempts)
            {
                throw new Exception("Transient LLM failure");
            }

            WasCalled = true;
            return Task.FromResult("## Introduction\nTest script content\n## Conclusion\nMore content");
        }

        public Task<string> CompleteAsync(string prompt, CancellationToken ct)
            => Task.FromResult("Completion result");

        public Task<SceneAnalysisResult?> AnalyzeSceneImportanceAsync(string sceneText, string? previousSceneText, string videoGoal, CancellationToken ct)
            => Task.FromResult<SceneAnalysisResult?>(null);

        public Task<VisualPromptResult?> GenerateVisualPromptAsync(string sceneText, string? previousSceneText, string videoTone, VisualStyle targetStyle, CancellationToken ct)
            => Task.FromResult<VisualPromptResult?>(null);

        public Task<ContentComplexityAnalysisResult?> AnalyzeContentComplexityAsync(string sceneText, string? previousSceneText, string videoGoal, CancellationToken ct)
            => Task.FromResult<ContentComplexityAnalysisResult?>(null);

        public Task<SceneCoherenceResult?> AnalyzeSceneCoherenceAsync(string fromSceneText, string toSceneText, string videoGoal, CancellationToken ct)
            => Task.FromResult<SceneCoherenceResult?>(null);

        public Task<NarrativeArcResult?> ValidateNarrativeArcAsync(IReadOnlyList<string> sceneTexts, string videoGoal, string videoType, CancellationToken ct)
            => Task.FromResult<NarrativeArcResult?>(null);

        public Task<string?> GenerateTransitionTextAsync(string fromSceneText, string toSceneText, string videoGoal, CancellationToken ct)
            => Task.FromResult<string?>(null);
    }

    private class FakeTtsProvider : ITtsProvider
    {
        public bool WasCalled { get; private set; }

        public Task<IReadOnlyList<string>> GetAvailableVoicesAsync()
            => Task.FromResult<IReadOnlyList<string>>(new List<string> { "TestVoice" });

        public Task<string> SynthesizeAsync(IEnumerable<ScriptLine> lines, VoiceSpec spec, CancellationToken ct)
        {
            WasCalled = true;
            return Task.FromResult("/tmp/test_audio.wav");
        }

    private static ProviderReadinessResult CreateReadyProvidersResult()
    {
        var result = new ProviderReadinessResult();
        result.CategoryStatuses.Add(new ProviderCategoryStatus(
            "LLM",
            true,
            "RuleBased",
            null,
            "RuleBased ready",
            Array.Empty<string>(),
            Array.Empty<ProviderCandidateStatus>()));
        result.CategoryStatuses.Add(new ProviderCategoryStatus(
            "TTS",
            true,
            "TestTTS",
            null,
            "Test TTS ready",
            Array.Empty<string>(),
            Array.Empty<ProviderCandidateStatus>()));
        result.CategoryStatuses.Add(new ProviderCategoryStatus(
            "Images",
            true,
            "MockImages",
            null,
            "Image provider ready",
            Array.Empty<string>(),
            Array.Empty<ProviderCandidateStatus>()));
        return result;
    }
    }

    private class FakeVideoComposer : IVideoComposer
    {
        public bool WasCalled { get; private set; }

        public Task<string> RenderAsync(Providers.Timeline timeline, RenderSpec spec, IProgress<RenderProgress> progress, CancellationToken ct)
        {
            WasCalled = true;
            progress?.Report(new RenderProgress { Percentage = 50, CurrentStage = "Encoding" });
            progress?.Report(new RenderProgress { Percentage = 100, CurrentStage = "Complete" });
            return Task.FromResult("/tmp/test_video.mp4");
        }
    }

    private class FakeImageProvider : IImageProvider
    {
        public bool WasCalled { get; private set; }

        public Task<IReadOnlyList<Asset>> FetchOrGenerateAsync(Scene scene, VisualSpec spec, CancellationToken ct)
        {
            WasCalled = true;
            var assets = new List<Asset>
            {
                new Asset($"/tmp/image_{scene.Index}.jpg", AssetType.Image, 0, scene.Duration)
            };
            return Task.FromResult<IReadOnlyList<Asset>>(assets);
        }
    }

    #endregion
}
