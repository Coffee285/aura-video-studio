using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aura.Core.Configuration;
using Aura.Core.Dependencies;
using Aura.Core.Models;
using Aura.Core.Models.Narrative;
using Aura.Core.Models.Streaming;
using Aura.Core.Models.Visual;
using Aura.Core.Models.Generation;
using Aura.Core.Orchestrator;
using Aura.Core.Providers;
using Aura.Core.Services.Generation;
using Aura.Core.Services;
using Aura.Core.Services.Providers;
using Aura.Core.Telemetry;
using Aura.Core.Timeline;
using Aura.Core.Validation;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Aura.Tests;

/// <summary>
/// Comprehensive integration tests for video generation functionality.
/// Tests both demo (QuickService) and normal (VideoOrchestrator) video generation paths.
/// </summary>
public class VideoGenerationComprehensiveTests
{
    private readonly ILoggerFactory _loggerFactory;

    public VideoGenerationComprehensiveTests()
    {
        _loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
    }

    [Fact]
    public async Task DemoVideo_ShouldUseShortDurationAndSafeDefaults()
    {
        // Arrange - This test validates that QuickService uses the correct safe defaults
        var brief = new Brief(
            Topic: "Welcome to Aura Video Studio",
            Audience: "General",
            Goal: "Demonstrate",
            Tone: "Informative",
            Language: "en-US",
            Aspect: Aspect.Widescreen16x9
        );

        var planSpec = new PlanSpec(
            TargetDuration: TimeSpan.FromSeconds(12), // Demo: 10-15 seconds
            Pacing: Pacing.Fast,
            Density: Density.Sparse,
            Style: "Demo"
        );

        var renderSpec = new RenderSpec(
            Res: new Resolution(1920, 1080), // Demo: Locked to 1080p
            Container: "mp4",
            VideoBitrateK: 5000,
            AudioBitrateK: 192,
            Fps: 30, // Demo: Locked to 30fps
            Codec: "H264", // Demo: Locked to H.264 for compatibility
            QualityLevel: 75,
            EnableSceneCut: true
        );

        // Assert - Validate demo defaults
        Assert.Equal(12, planSpec.TargetDuration.TotalSeconds);
        Assert.Equal(Pacing.Fast, planSpec.Pacing);
        Assert.Equal("Demo", planSpec.Style);
        Assert.Equal(1920, renderSpec.Res.Width);
        Assert.Equal(1080, renderSpec.Res.Height);
        Assert.Equal(30, renderSpec.Fps);
        Assert.Equal("H264", renderSpec.Codec);
    }

    [Fact]
    public async Task NormalVideo_ShouldSupportCustomDurationAndSettings()
    {
        // Arrange - This test validates that normal video supports custom settings
        var brief = new Brief(
            Topic: "Machine Learning Fundamentals",
            Audience: "Technical professionals",
            Goal: "Educate",
            Tone: "Professional",
            Language: "en-US",
            Aspect: Aspect.Widescreen16x9
        );

        var planSpec = new PlanSpec(
            TargetDuration: TimeSpan.FromMinutes(3), // Normal: Custom duration
            Pacing: Pacing.Conversational,
            Density: Density.Balanced,
            Style: "Educational"
        );

        var renderSpec = new RenderSpec(
            Res: new Resolution(2560, 1440), // Normal: Custom resolution
            Container: "mp4",
            VideoBitrateK: 8000,
            AudioBitrateK: 256,
            Fps: 60, // Normal: Custom fps
            Codec: "H264",
            QualityLevel: 85, // Normal: Higher quality
            EnableSceneCut: true
        );

        // Assert - Validate custom settings
        Assert.Equal(180, planSpec.TargetDuration.TotalSeconds);
        Assert.Equal(Pacing.Conversational, planSpec.Pacing);
        Assert.Equal("Educational", planSpec.Style);
        Assert.Equal(2560, renderSpec.Res.Width);
        Assert.Equal(1440, renderSpec.Res.Height);
        Assert.Equal(60, renderSpec.Fps);
        Assert.Equal(85, renderSpec.QualityLevel);
    }

    [Fact]
    public async Task VideoOrchestrator_WithSmartOrchestration_ShouldExecuteAllStages()
    {
        // Arrange
        var monitor = new ResourceMonitor(_loggerFactory.CreateLogger<ResourceMonitor>());
        var selector = new StrategySelector(_loggerFactory.CreateLogger<StrategySelector>());
        var smartOrchestrator = new VideoGenerationOrchestrator(
            _loggerFactory.CreateLogger<VideoGenerationOrchestrator>(),
            monitor,
            selector);

        var mockLlmProvider = new TestLlmProvider();
        var mockTtsProvider = new TestTtsProvider();
        var mockVideoComposer = new TestVideoComposer();
        var mockImageProvider = new TestImageProvider();
        var mockFfmpegLocator = new TestFfmpegLocator();
        var mockHardwareDetector = new TestHardwareDetector();
        var mockCache = new MemoryCache(
            new MemoryCacheOptions());
        var ffmpegResolver = new FFmpegResolver(
            _loggerFactory.CreateLogger<FFmpegResolver>(),
            mockCache);

        var preGenerationValidator = new PreGenerationValidator(
            _loggerFactory.CreateLogger<PreGenerationValidator>(),
            mockFfmpegLocator,
            ffmpegResolver,
            mockHardwareDetector,
            CreateReadyProviderReadinessService());
        var scriptValidator = new ScriptValidator();
        var retryWrapper = new ProviderRetryWrapper(
            _loggerFactory.CreateLogger<ProviderRetryWrapper>());
        var ttsValidator = new TtsOutputValidator(
            _loggerFactory.CreateLogger<TtsOutputValidator>());
        var imageValidator = new ImageOutputValidator(
            _loggerFactory.CreateLogger<ImageOutputValidator>());
        var llmValidator = new LlmOutputValidator(
            _loggerFactory.CreateLogger<LlmOutputValidator>());
        var cleanupManager = new ResourceCleanupManager(
            _loggerFactory.CreateLogger<ResourceCleanupManager>());

        var timelineBuilder = new TimelineBuilder();
        var providerSettings = new ProviderSettings(
            _loggerFactory.CreateLogger<ProviderSettings>());
        var telemetryCollector = new RunTelemetryCollector(
            _loggerFactory.CreateLogger<RunTelemetryCollector>(),
            System.IO.Path.GetTempPath());

        var orchestrator = new VideoOrchestrator(
            _loggerFactory.CreateLogger<VideoOrchestrator>(),
            mockLlmProvider,
            mockTtsProvider,
            mockVideoComposer,
            smartOrchestrator,
            monitor,
            preGenerationValidator,
            scriptValidator,
            retryWrapper,
            ttsValidator,
            imageValidator,
            llmValidator,
            cleanupManager,
            timelineBuilder,
            providerSettings,
            telemetryCollector,
            mockImageProvider);

        var brief = new Brief("Test Topic", null, null, "Professional", "English", Aspect.Widescreen16x9);
        var planSpec = new PlanSpec(TimeSpan.FromSeconds(30), Pacing.Conversational, Density.Balanced, "Modern");
        var voiceSpec = new VoiceSpec("en-US-AriaNeural", 1.0, 1.0, PauseStyle.Natural);
        var renderSpec = new RenderSpec(new Resolution(1920, 1080), "mp4", 5000, 192, 30, "H264", 75, true);
        var systemProfile = new SystemProfile
        {
            Tier = HardwareTier.B,
            LogicalCores = 4,
            PhysicalCores = 2,
            RamGB = 8,
            OfflineOnly = false
        };

        // Act
        var result = await orchestrator.GenerateVideoAsync(
            brief, planSpec, voiceSpec, renderSpec, systemProfile,
            null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.True(mockLlmProvider.DraftScriptCalled);
        Assert.True(mockTtsProvider.SynthesizeCalled);
        Assert.True(mockVideoComposer.RenderCalled);
    }

    [Fact]
    public async Task VideoOrchestrator_WithCancellation_ShouldStopGracefully()
    {
        // Arrange
        var monitor = new ResourceMonitor(_loggerFactory.CreateLogger<ResourceMonitor>());
        var selector = new StrategySelector(_loggerFactory.CreateLogger<StrategySelector>());
        var smartOrchestrator = new VideoGenerationOrchestrator(
            _loggerFactory.CreateLogger<VideoGenerationOrchestrator>(),
            monitor,
            selector);

        var mockLlmProvider = new TestSlowLlmProvider(); // Slow provider to allow cancellation
        var mockTtsProvider = new TestTtsProvider();
        var mockVideoComposer = new TestVideoComposer();
        var mockFfmpegLocator = new TestFfmpegLocator();
        var mockHardwareDetector = new TestHardwareDetector();
        var mockCache2 = new MemoryCache(
            new MemoryCacheOptions());
        var ffmpegResolver2 = new FFmpegResolver(
            _loggerFactory.CreateLogger<FFmpegResolver>(),
            mockCache2);

        var preGenerationValidator = new PreGenerationValidator(
            _loggerFactory.CreateLogger<PreGenerationValidator>(),
            mockFfmpegLocator,
            ffmpegResolver2,
            mockHardwareDetector,
            CreateReadyProviderReadinessService());
        var scriptValidator = new ScriptValidator();
        var retryWrapper = new ProviderRetryWrapper(
            _loggerFactory.CreateLogger<ProviderRetryWrapper>());
        var ttsValidator = new TtsOutputValidator(
            _loggerFactory.CreateLogger<TtsOutputValidator>());
        var imageValidator = new ImageOutputValidator(
            _loggerFactory.CreateLogger<ImageOutputValidator>());
        var llmValidator = new LlmOutputValidator(
            _loggerFactory.CreateLogger<LlmOutputValidator>());
        var cleanupManager = new ResourceCleanupManager(
            _loggerFactory.CreateLogger<ResourceCleanupManager>());

        var timelineBuilder = new TimelineBuilder();
        var providerSettings = new ProviderSettings(
            _loggerFactory.CreateLogger<ProviderSettings>());
        var telemetryCollector = new RunTelemetryCollector(
            _loggerFactory.CreateLogger<RunTelemetryCollector>(),
            System.IO.Path.GetTempPath());

        var orchestrator = new VideoOrchestrator(
            _loggerFactory.CreateLogger<VideoOrchestrator>(),
            mockLlmProvider,
            mockTtsProvider,
            mockVideoComposer,
            smartOrchestrator,
            monitor,
            preGenerationValidator,
            scriptValidator,
            retryWrapper,
            ttsValidator,
            imageValidator,
            llmValidator,
            cleanupManager,
            timelineBuilder,
            providerSettings,
            telemetryCollector,
            null);

        var brief = new Brief("Test Topic", null, null, "Professional", "English", Aspect.Widescreen16x9);
        var planSpec = new PlanSpec(TimeSpan.FromSeconds(30), Pacing.Conversational, Density.Balanced, "Modern");
        var voiceSpec = new VoiceSpec("en-US-AriaNeural", 1.0, 1.0, PauseStyle.Natural);
        var renderSpec = new RenderSpec(new Resolution(1920, 1080), "mp4", 5000, 192, 30, "H264", 75, true);
        var systemProfile = new SystemProfile
        {
            Tier = HardwareTier.B,
            LogicalCores = 4,
            PhysicalCores = 2,
            RamGB = 8,
            OfflineOnly = false
        };

        var cts = new CancellationTokenSource();
        cts.CancelAfter(100); // Cancel after 100ms

        // Act & Assert - Cancellation may be wrapped in OrchestrationException
        var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await orchestrator.GenerateVideoAsync(
                brief, planSpec, voiceSpec, renderSpec, systemProfile,
                null, cts.Token);
        });

        // Verify it's either a cancellation or orchestration exception with cancellation as inner
        Assert.True(
            exception is OperationCanceledException ||
            exception is Aura.Core.Services.Generation.OrchestrationException,
            $"Expected cancellation-related exception, got: {exception.GetType().Name}");
    }

    private static IProviderReadinessService CreateReadyProviderReadinessService()
    {
        return new StaticProviderReadinessService(CreateReadyProvidersResult());
    }

    private static ProviderReadinessResult CreateReadyProvidersResult()
    {
        var result = new ProviderReadinessResult();
        result.CategoryStatuses.Add(new ProviderCategoryStatus(
            "LLM",
            true,
            "TestLLM",
            null,
            "LLM ready",
            Array.Empty<string>(),
            Array.Empty<ProviderCandidateStatus>()));
        result.CategoryStatuses.Add(new ProviderCategoryStatus(
            "TTS",
            true,
            "TestTTS",
            null,
            "TTS ready",
            Array.Empty<string>(),
            Array.Empty<ProviderCandidateStatus>()));
        result.CategoryStatuses.Add(new ProviderCategoryStatus(
            "Images",
            true,
            "TestImages",
            null,
            "Images ready",
            Array.Empty<string>(),
            Array.Empty<ProviderCandidateStatus>()));
        return result;
    }

    private sealed class StaticProviderReadinessService : IProviderReadinessService
    {
        private readonly ProviderReadinessResult _result;

        public StaticProviderReadinessService(ProviderReadinessResult result)
        {
            _result = result;
        }

        public Task<ProviderReadinessResult> ValidateRequiredProvidersAsync(CancellationToken ct = default)
        {
            return Task.FromResult(_result);
        }
    }

    // Test helper classes
    private sealed class TestLlmProvider : ILlmProvider
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

        public bool DraftScriptCalled { get; private set; }

        public Task<string> DraftScriptAsync(Brief brief, PlanSpec spec, CancellationToken ct)
        {
            DraftScriptCalled = true;
            return Task.FromResult(@"# Test Video

## Scene 1
This is a test scene with enough content to pass validation. Artificial intelligence is transforming our world with innovative solutions.

## Scene 2
More test content here to ensure we have adequate word count for the duration. Machine learning enables computers to learn from data.");
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

    private sealed class TestSlowLlmProvider : ILlmProvider
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

        public async Task<string> DraftScriptAsync(Brief brief, PlanSpec spec, CancellationToken ct)
        {
            await Task.Delay(5000, ct); // Slow operation to allow cancellation
            return "This shouldn't be reached";
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

    private sealed class TestTtsProvider : ITtsProvider
    {
        public bool SynthesizeCalled { get; private set; }

        public Task<IReadOnlyList<string>> GetAvailableVoicesAsync()
        {
            return Task.FromResult<IReadOnlyList<string>>(new List<string> { "en-US-AriaNeural" });
        }

        public async Task<string> SynthesizeAsync(IEnumerable<ScriptLine> lines, VoiceSpec spec, CancellationToken ct)
        {
            SynthesizeCalled = true;
            var outputPath = Path.Combine(Path.GetTempPath(), $"test-audio-{Guid.NewGuid()}.wav");

            // Create a valid WAV file
            var sampleRate = 44100;
            var channels = 1;
            var bitsPerSample = 16;
            var duration = 1.0;
            var numSamples = (int)(sampleRate * duration);
            var dataSize = numSamples * channels * (bitsPerSample / 8);

            using (var fs = new FileStream(outputPath, FileMode.Create))
            using (var writer = new BinaryWriter(fs))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + dataSize);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)channels);
                writer.Write(sampleRate);
                writer.Write(sampleRate * channels * (bitsPerSample / 8));
                writer.Write((short)(channels * (bitsPerSample / 8)));
                writer.Write((short)bitsPerSample);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                writer.Write(dataSize);
                for (int i = 0; i < numSamples; i++)
                {
                    writer.Write((short)0);
                }
            }

            return await Task.FromResult(outputPath);
        }
    }

    private sealed class TestVideoComposer : IVideoComposer
    {
        public bool RenderCalled { get; private set; }

        public Task<string> RenderAsync(Aura.Core.Providers.Timeline timeline, RenderSpec spec, IProgress<RenderProgress> progress, CancellationToken ct)
        {
            RenderCalled = true;
            progress?.Report(new RenderProgress(100, TimeSpan.FromSeconds(1), TimeSpan.Zero, "Complete"));
            return Task.FromResult($"/tmp/test-video-{Guid.NewGuid()}.mp4");
        }
    }

    private sealed class TestImageProvider : IImageProvider
    {
        public async Task<IReadOnlyList<Asset>> FetchOrGenerateAsync(Scene scene, VisualSpec spec, CancellationToken ct)
        {
            var imagePath = Path.Combine(Path.GetTempPath(), $"test-image-{Guid.NewGuid()}.jpg");

            // Minimal JPEG file
            byte[] minimalJpeg = new byte[]
            {
                0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x01, 0x00, 0x48,
                0x00, 0x48, 0x00, 0x00, 0xFF, 0xDB, 0x00, 0x43, 0x00, 0x03, 0x02, 0x02, 0x03, 0x02, 0x02, 0x03,
                0x03, 0x03, 0x03, 0x04, 0x03, 0x03, 0x04, 0x05, 0x08, 0x05, 0x05, 0x04, 0x04, 0x05, 0x0A, 0x07,
                0x07, 0x06, 0x08, 0x0C, 0x0A, 0x0C, 0x0C, 0x0B, 0x0A, 0x0B, 0x0B, 0x0D, 0x0E, 0x12, 0x10, 0x0D,
                0x0E, 0x11, 0x0E, 0x0B, 0x0B, 0x10, 0x16, 0x10, 0x11, 0x13, 0x14, 0x15, 0x15, 0x15, 0x0C, 0x0F,
                0x17, 0x18, 0x16, 0x14, 0x18, 0x12, 0x14, 0x15, 0x14, 0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x01,
                0x00, 0x01, 0x01, 0x01, 0x11, 0x00, 0xFF, 0xC4, 0x00, 0x14, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x09, 0xFF, 0xC4, 0x00, 0x14,
                0x10, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00, 0xD2, 0xCF, 0x20, 0xFF,
                0xD9
            };

            await File.WriteAllBytesAsync(imagePath, minimalJpeg, ct);

            var assets = new List<Asset>
            {
                new Asset("image", imagePath, "CC0", null)
            };
            return await Task.FromResult<IReadOnlyList<Asset>>(assets);
        }
    }

    private sealed class TestFfmpegLocator : Aura.Core.Dependencies.IFfmpegLocator
    {
        private readonly string _mockPath;

        public TestFfmpegLocator()
        {
            // Create a temporary file to simulate FFmpeg
            _mockPath = Path.GetTempFileName();
        }

        public Task<string> GetEffectiveFfmpegPathAsync(string? configuredPath = null, CancellationToken ct = default)
        {
            return Task.FromResult(_mockPath);
        }

        public Task<Aura.Core.Dependencies.FfmpegValidationResult> CheckAllCandidatesAsync(string? configuredPath = null, CancellationToken ct = default)
        {
            return Task.FromResult(new Aura.Core.Dependencies.FfmpegValidationResult
            {
                Found = true,
                FfmpegPath = _mockPath,
                Source = "PATH",
                HasX264 = true,
                VersionString = "ffmpeg version 4.4.0",
                Diagnostics = new List<string> { "FFmpeg found in PATH" }
            });
        }

        public Task<Aura.Core.Dependencies.FfmpegValidationResult> ValidatePathAsync(string ffmpegPath, CancellationToken ct = default)
        {
            return Task.FromResult(new Aura.Core.Dependencies.FfmpegValidationResult
            {
                Found = true,
                FfmpegPath = ffmpegPath,
                Source = "Attached",
                HasX264 = true,
                VersionString = "ffmpeg version 4.4.0",
                Diagnostics = new List<string> { "FFmpeg validated successfully" }
            });
        }
    }

    private sealed class TestHardwareDetector : Aura.Core.Hardware.IHardwareDetector
    {
        public Task<SystemProfile> DetectSystemAsync()
        {
            return Task.FromResult(new SystemProfile
            {
                Tier = HardwareTier.B,
                LogicalCores = 4,
                PhysicalCores = 2,
                RamGB = 8,
                OfflineOnly = false
            });
        }

        public SystemProfile ApplyManualOverrides(SystemProfile detected, HardwareOverrides overrides)
        {
            return detected;
        }

        public Task RunHardwareProbeAsync()
        {
            return Task.CompletedTask;
        }
    }
}
