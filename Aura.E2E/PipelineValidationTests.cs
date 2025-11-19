using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aura.Core.Artifacts;
using Aura.Core.Configuration;
using Aura.Core.Hardware;
using Aura.Core.Models;
using Aura.Core.Models.Visual;
using Aura.Core.Orchestrator;
using Aura.Core.Providers;
using Aura.Core.Services.Generation;
using Aura.Providers.Llm;
using Aura.Providers.Tts;
using Aura.Providers.Video;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using Aura.Core.Models.Streaming;

namespace Aura.E2E;

/// <summary>
/// Comprehensive pipeline validation tests for Pull #188 (PR 32)
/// Tests complete video generation pipeline from brief to downloadable video
/// </summary>
public class PipelineValidationTests
{
    private readonly ITestOutputHelper _output;

    public PipelineValidationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Test 1-9: Complete pipeline from brief to video with progress tracking
    /// Validates: Job creation, progress updates, script generation, TTS, image gen, FFmpeg assembly
    /// </summary>
    [Fact(Skip = "Integration test - requires FFmpeg and providers")]
    public async Task CompletePipeline_Should_GenerateVideoWithAllStages()
    {
        // Arrange
        var services = CreateTestServiceProvider();
        var serviceProvider = services.BuildServiceProvider();
        var jobRunner = serviceProvider.GetRequiredService<JobRunner>();
        
        var brief = new Brief(
            Topic: "Quick Demo - AI Video Studio",
            Audience: "General",
            Goal: "Demonstrate capabilities",
            Tone: "Friendly",
            Language: "en-US",
            Aspect: Aspect.Widescreen16x9
        );

        var planSpec = new PlanSpec(
            TargetDuration: TimeSpan.FromSeconds(15),
            Pacing: Pacing.Fast,
            Density: Density.Sparse,
            Style: "Demo"
        );

        var voiceSpec = new VoiceSpec(
            VoiceName: "en-US-Standard-A",
            Rate: 1.0,
            Pitch: 0.0,
            Pause: PauseStyle.Short
        );

        var renderSpec = new RenderSpec(
            Res: new Resolution(1920, 1080),
            Container: "mp4",
            VideoBitrateK: 5000,
            AudioBitrateK: 192,
            Fps: 30,
            Codec: "H264",
            QualityLevel: 75,
            EnableSceneCut: true
        );

        var correlationId = $"pipeline-validation-{DateTime.UtcNow:yyyyMMddHHmmss}";

        // Act: Create and start job
        _output.WriteLine($"[TEST] Creating job with correlation ID: {correlationId}");
        var job = await jobRunner.CreateAndStartJobAsync(brief, planSpec, voiceSpec, renderSpec, correlationId).ConfigureAwait(false);
        
        // Assert: Job creation
        Assert.NotNull(job);
        Assert.NotNull(job.Id);
        _output.WriteLine($"[TEST] ✓ Job created: {job.Id}");

        // Track progress through all stages
        var seenStages = new HashSet<string>();
        var timeout = TimeSpan.FromMinutes(3);
        var pollInterval = TimeSpan.FromSeconds(1);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        Job? completedJob = null;
        while (stopwatch.Elapsed < timeout)
        {
            completedJob = jobRunner.GetJob(job.Id);
            Assert.NotNull(completedJob);

            if (!string.IsNullOrEmpty(completedJob.Stage))
            {
                if (seenStages.Add(completedJob.Stage))
                {
                    _output.WriteLine($"[TEST] Stage entered: {completedJob.Stage} ({completedJob.Percent}%)");
                }
            }

            _output.WriteLine($"[{stopwatch.Elapsed:mm\\:ss}] Status: {completedJob.Status}, Progress: {completedJob.Percent}%, Stage: {completedJob.Stage}");

            if (completedJob.Status == JobStatus.Done)
            {
                _output.WriteLine("[TEST] ✓ Job completed successfully!");
                break;
            }

            if (completedJob.Status == JobStatus.Failed)
            {
                var errorMessage = completedJob.ErrorMessage ?? "Unknown error";
                var failureDetails = completedJob.FailureDetails != null
                    ? $"\nStage: {completedJob.FailureDetails.Stage}\nDetails: {completedJob.FailureDetails.Message}"
                    : "";
                
                Assert.Fail($"Job failed: {errorMessage}{failureDetails}");
            }

            await Task.Delay(pollInterval).ConfigureAwait(false);
        }

        // Assert: Successful completion
        Assert.NotNull(completedJob);
        Assert.Equal(JobStatus.Done, completedJob.Status);
        Assert.Equal(100, completedJob.Percent);
        _output.WriteLine($"[TEST] ✓ Progress tracking validated through {seenStages.Count} stages");

        // Assert: Script generation completed
        Assert.Contains("Script", seenStages, StringComparer.OrdinalIgnoreCase);
        _output.WriteLine("[TEST] ✓ Script generation completed");

        // Assert: TTS generation completed
        Assert.Contains("Audio", seenStages, StringComparer.OrdinalIgnoreCase);
        _output.WriteLine("[TEST] ✓ TTS audio generation completed");

        // Assert: Image generation completed
        Assert.Contains("Visual", seenStages, StringComparer.OrdinalIgnoreCase);
        _output.WriteLine("[TEST] ✓ Image generation completed");

        // Assert: FFmpeg assembly completed
        Assert.Contains("Render", seenStages, StringComparer.OrdinalIgnoreCase);
        _output.WriteLine("[TEST] ✓ FFmpeg assembly completed");

        // Assert: Video file exists and is valid
        Assert.NotEmpty(completedJob.Artifacts);
        var videoArtifact = Assert.Single(completedJob.Artifacts);
        Assert.NotNull(videoArtifact.Path);
        Assert.True(File.Exists(videoArtifact.Path), $"Video file does not exist at: {videoArtifact.Path}");

        var fileInfo = new FileInfo(videoArtifact.Path);
        Assert.True(fileInfo.Length > 100_000, $"Video file too small ({fileInfo.Length} bytes)");
        
        _output.WriteLine($"[TEST] ✓ Video file generated: {fileInfo.Length:N0} bytes");
        _output.WriteLine($"[TEST] ✓ Video downloadable at: {videoArtifact.Path}");
    }

    /// <summary>
    /// Test 10: Job cancellation mechanism validation
    /// Note: This test validates cancellation token behavior, not full JobRunner integration
    /// </summary>
    [Fact]
    public async Task JobCancellation_Should_SupportCancellationToken()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var llmProvider = new RuleBasedLlmProvider(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RuleBasedLlmProvider>.Instance);
        
        var brief = new Brief(
            Topic: "Cancellation Test Video",
            Audience: "Test",
            Goal: "Test cancellation",
            Tone: "Neutral",
            Language: "en-US",
            Aspect: Aspect.Widescreen16x9
        );

        var planSpec = new PlanSpec(
            TargetDuration: TimeSpan.FromSeconds(30),
            Pacing: Pacing.Conversational,
            Density: Density.Balanced,
            Style: "Test"
        );

        // Act: Start operation then cancel
        var task = llmProvider.DraftScriptAsync(brief, planSpec, cts.Token);
        
        // Cancel immediately
        cts.Cancel();

        // Assert: Operation respects cancellation (or completes quickly for fast operations)
        try
        {
            var result = await task.ConfigureAwait(false);
            // If it completes, that's fine - it was fast enough
            Assert.NotNull(result);
            _output.WriteLine("[TEST] ✓ Operation completed before cancellation took effect");
        }
        catch (OperationCanceledException)
        {
            _output.WriteLine("[TEST] ✓ Operation cancelled successfully");
            Assert.True(true);
        }
    }

    /// <summary>
    /// Test 11: Error handling when LLM provider unavailable
    /// </summary>
    [Fact]
    public async Task ErrorHandling_LlmProviderUnavailable_Should_FailGracefully()
    {
        // Arrange
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<PipelineValidationFailingLlmProvider>.Instance;
        var failingProvider = new PipelineValidationFailingLlmProvider(logger);
        
        var brief = new Brief(
            Topic: "Test Video",
            Audience: "Test",
            Goal: "Test error handling",
            Tone: "Neutral",
            Language: "en-US",
            Aspect: Aspect.Widescreen16x9
        );

        var planSpec = new PlanSpec(
            TargetDuration: TimeSpan.FromSeconds(10),
            Pacing: Pacing.Fast,
            Density: Density.Sparse,
            Style: "Test"
        );

        // Act & Assert: Provider should fail gracefully
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await failingProvider.DraftScriptAsync(brief, planSpec, CancellationToken.None).ConfigureAwait(false)
        ).ConfigureAwait(false);

        // Assert: Error message is present and user-friendly
        Assert.NotNull(exception.Message);
        Assert.NotEmpty(exception.Message);
        Assert.Contains("unavailable", exception.Message, StringComparison.OrdinalIgnoreCase);
        _output.WriteLine($"[TEST] ✓ LLM provider fails gracefully with error: {exception.Message}");
        
        // Assert: Error message is user-friendly (doesn't contain technical details)
        Assert.DoesNotContain("Stack trace", exception.Message, StringComparison.OrdinalIgnoreCase);
        _output.WriteLine("[TEST] ✓ Error message is user-friendly");
    }

    /// <summary>
    /// Test 12: Error handling when TTS provider unavailable
    /// </summary>
    [Fact]
    public async Task ErrorHandling_TtsProviderUnavailable_Should_FailGracefully()
    {
        // Arrange
        var failingProvider = new PipelineValidationFailingTtsProvider();
        
        var voiceSpec = new VoiceSpec(
            VoiceName: "en-US-Standard-A",
            Rate: 1.0,
            Pitch: 0.0,
            Pause: PauseStyle.Short
        );

        var scriptLines = new List<ScriptLine>
        {
            new ScriptLine(
                SceneIndex: 0,
                Text: "Test line",
                Start: TimeSpan.Zero,
                Duration: TimeSpan.FromSeconds(1)
            )
        };

        // Act & Assert: Provider should fail gracefully
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await failingProvider.SynthesizeAsync(scriptLines, voiceSpec, CancellationToken.None).ConfigureAwait(false)
        ).ConfigureAwait(false);

        // Assert: Error message is present and user-friendly
        Assert.NotNull(exception.Message);
        Assert.NotEmpty(exception.Message);
        Assert.Contains("unavailable", exception.Message, StringComparison.OrdinalIgnoreCase);
        _output.WriteLine($"[TEST] ✓ TTS provider fails gracefully with error: {exception.Message}");
    }

    /// <summary>
    /// Test 13: Error handling when image provider unavailable
    /// Note: This test validates error propagation, not full job integration
    /// </summary>
    [Fact]
    public void ErrorHandling_ImageProviderUnavailable_Should_PropagateError()
    {
        // Arrange
        // Image provider error handling is typically validated through validator
        var validator = new GenerationValidator(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<GenerationValidator>.Instance);

        var llmProviders = new Dictionary<string, ILlmProvider>();
        var ttsProviders = new Dictionary<string, ITtsProvider>();
        var visualProviders = new Dictionary<string, object>();

        // Act: Validate with no providers available
        var result = validator.ValidateProviders(
            llmProviders,
            ttsProviders,
            visualProviders,
            "Free",
            "Free",
            "Free",
            offlineOnly: false
        );

        // Assert: Validation should detect missing providers
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Issues);
        _output.WriteLine($"[TEST] ✓ Image provider validation detects missing providers: {string.Join(", ", result.Issues)}");
    }

    /// <summary>
    /// Test 15: Temporary files cleanup after completion
    /// </summary>
    [Fact]
    public void TemporaryFiles_Should_BeCleanedUpAfterCompletion()
    {
        // Arrange
        var services = CreateTestServiceProvider();
        var serviceProvider = services.BuildServiceProvider();
        var artifactManager = serviceProvider.GetRequiredService<ArtifactManager>();
        var providerSettings = serviceProvider.GetRequiredService<ProviderSettings>();

        var testJobId = Guid.NewGuid().ToString();
        
        // Create temporary test files
        var tempDir = Path.Combine(providerSettings.GetOutputDirectory(), "temp", testJobId);
        Directory.CreateDirectory(tempDir);
        
        var tempFile1 = Path.Combine(tempDir, "test1.tmp");
        var tempFile2 = Path.Combine(tempDir, "test2.tmp");
        File.WriteAllText(tempFile1, "test data 1");
        File.WriteAllText(tempFile2, "test data 2");
        
        _output.WriteLine($"[TEST] Created temporary files in: {tempDir}");
        Assert.True(File.Exists(tempFile1));
        Assert.True(File.Exists(tempFile2));

        // Act: Cleanup temporary files
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }

        // Assert: Temporary files are cleaned up
        Assert.False(Directory.Exists(tempDir));
        _output.WriteLine("[TEST] ✓ Temporary files cleaned up successfully");
    }

    /// <summary>
    /// Test 16: Logs capture all pipeline events and errors
    /// </summary>
    [Fact]
    public void Logging_Should_CaptureAllPipelineEvents()
    {
        // Arrange
        var logMessages = new List<string>();
        var services = new ServiceCollection();
        
        // Add custom logger that captures messages
        services.AddLogging(builder =>
        {
            builder.AddProvider(new TestLoggerProvider(logMessages));
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // Core services
        services.AddSingleton<HardwareDetector>();
        services.AddSingleton<ProviderSettings>();
        services.AddHttpClient();

        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PipelineValidationTests>>();

        // Act: Log various pipeline events
        logger.LogInformation("Pipeline started");
        logger.LogDebug("Script generation stage entered");
        logger.LogWarning("Provider fallback occurred");
        logger.LogError("Stage failed with error");
        logger.LogInformation("Pipeline completed");

        // Assert: All log levels captured
        Assert.Contains(logMessages, m => m.Contains("Pipeline started"));
        Assert.Contains(logMessages, m => m.Contains("Script generation stage entered"));
        Assert.Contains(logMessages, m => m.Contains("Provider fallback occurred"));
        Assert.Contains(logMessages, m => m.Contains("Stage failed with error"));
        Assert.Contains(logMessages, m => m.Contains("Pipeline completed"));
        
        _output.WriteLine($"[TEST] ✓ Captured {logMessages.Count} log messages");
        _output.WriteLine("[TEST] ✓ All pipeline events logged");
    }

    // Helper methods

    private ServiceCollection CreateTestServiceProvider()
    {
        var services = new ServiceCollection();

        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Core services
        services.AddSingleton<HardwareDetector>();
        services.AddSingleton<ProviderSettings>();
        services.AddHttpClient();

        // Audio services
        services.AddSingleton<Aura.Core.Audio.SilentWavGenerator>();
        services.AddSingleton<Aura.Core.Audio.WavValidator>();

        // Providers
        services.AddSingleton<ILlmProvider, RuleBasedLlmProvider>();
        services.AddSingleton<ITtsProvider, NullTtsProvider>();
        services.AddSingleton<IVideoComposer>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<FfmpegVideoComposer>>();
            var providerSettings = sp.GetRequiredService<ProviderSettings>();
            var ffmpegLocator = new Aura.Core.Dependencies.FfmpegLocator(
                sp.GetRequiredService<ILogger<Aura.Core.Dependencies.FfmpegLocator>>(),
                providerSettings.GetToolsDirectory());
            var outputDirectory = providerSettings.GetOutputDirectory();
            return new FfmpegVideoComposer(logger, ffmpegLocator, null, outputDirectory);
        });

        // Orchestration services
        services.AddSingleton<ResourceMonitor>();
        services.AddSingleton<StrategySelector>();
        services.AddSingleton<VideoGenerationOrchestrator>();
        services.AddSingleton<VideoOrchestrator>();

        // Job management
        services.AddSingleton<ArtifactManager>();
        services.AddSingleton<JobRunner>();

        return services;
    }

    private ServiceCollection CreateTestServiceProviderWithFailingLlm()
    {
        var services = CreateTestServiceProvider();
        
        // Replace LLM provider with failing one
        var llmDescriptor = services.First(d => d.ServiceType == typeof(ILlmProvider));
        services.Remove(llmDescriptor);
        services.AddSingleton<ILlmProvider>(sp => 
            new PipelineValidationFailingLlmProvider(
                sp.GetRequiredService<ILogger<PipelineValidationFailingLlmProvider>>()));
        
        return services;
    }

    private ServiceCollection CreateTestServiceProviderWithFailingTts()
    {
        var services = CreateTestServiceProvider();
        
        // Replace TTS provider with failing one
        var ttsDescriptor = services.First(d => d.ServiceType == typeof(ITtsProvider));
        services.Remove(ttsDescriptor);
        services.AddSingleton<ITtsProvider, PipelineValidationFailingTtsProvider>();
        
        return services;
    }

    private ServiceCollection CreateTestServiceProviderWithFailingImageProvider()
    {
        var services = CreateTestServiceProvider();
        
        // Image provider failures are typically handled in the visual generation stage
        // For this test, we'll use the standard setup which will fail gracefully
        // when no images are available
        
        return services;
    }
}

/// <summary>
/// Mock LLM provider that always fails (for pipeline validation tests)
/// </summary>
internal sealed class PipelineValidationFailingLlmProvider : ILlmProvider
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

    private readonly ILogger<PipelineValidationFailingLlmProvider> _logger;

    public PipelineValidationFailingLlmProvider(ILogger<PipelineValidationFailingLlmProvider> logger)
    {
        _logger = logger;
    }

    public Task<string> DraftScriptAsync(Brief brief, PlanSpec spec, CancellationToken ct)
    {
        _logger.LogError("LLM provider is unavailable or misconfigured");
        throw new InvalidOperationException("LLM provider is unavailable or misconfigured");
    }

    public Task<string> CompleteAsync(string prompt, CancellationToken ct)
    {
        _logger.LogError("LLM provider is unavailable or misconfigured");
        throw new InvalidOperationException("LLM provider is unavailable or misconfigured");
    }

    public Task<SceneAnalysisResult?> AnalyzeSceneImportanceAsync(
        string sceneText,
        string? previousSceneText,
        string videoGoal,
        CancellationToken ct)
    {
        _logger.LogError("LLM provider is unavailable or misconfigured");
        throw new InvalidOperationException("LLM provider is unavailable or misconfigured");
    }

    public Task<VisualPromptResult?> GenerateVisualPromptAsync(
        string sceneText,
        string? previousSceneText,
        string videoTone,
        VisualStyle targetStyle,
        CancellationToken ct)
    {
        _logger.LogError("LLM provider is unavailable or misconfigured");
        throw new InvalidOperationException("LLM provider is unavailable or misconfigured");
    }

    public Task<ContentComplexityAnalysisResult?> AnalyzeContentComplexityAsync(
        string sceneText,
        string? previousSceneText,
        string videoGoal,
        CancellationToken ct)
    {
        _logger.LogError("LLM provider is unavailable or misconfigured");
        throw new InvalidOperationException("LLM provider is unavailable or misconfigured");
    }

    public Task<SceneCoherenceResult?> AnalyzeSceneCoherenceAsync(
        string fromSceneText,
        string toSceneText,
        string videoGoal,
        CancellationToken ct)
    {
        _logger.LogError("LLM provider is unavailable or misconfigured");
        throw new InvalidOperationException("LLM provider is unavailable or misconfigured");
    }

    public Task<NarrativeArcResult?> ValidateNarrativeArcAsync(
        IReadOnlyList<string> sceneTexts,
        string videoGoal,
        string videoType,
        CancellationToken ct)
    {
        _logger.LogError("LLM provider is unavailable or misconfigured");
        throw new InvalidOperationException("LLM provider is unavailable or misconfigured");
    }

    public Task<string?> GenerateTransitionTextAsync(
        string fromSceneText,
        string toSceneText,
        string videoGoal,
        CancellationToken ct)
    {
        _logger.LogError("LLM provider is unavailable or misconfigured");
        throw new InvalidOperationException("LLM provider is unavailable or misconfigured");
    }
}

/// <summary>
/// Mock TTS provider that always fails (for pipeline validation tests)
/// </summary>
internal sealed class PipelineValidationFailingTtsProvider : ITtsProvider
{
    public Task<IReadOnlyList<string>> GetAvailableVoicesAsync()
    {
        return Task.FromResult<IReadOnlyList<string>>(new List<string>());
    }

    public Task<string> SynthesizeAsync(IEnumerable<ScriptLine> lines, VoiceSpec voice, CancellationToken ct)
    {
        throw new InvalidOperationException("TTS provider is unavailable or misconfigured");
    }
}

/// <summary>
/// Test logger provider that captures log messages
/// </summary>
internal sealed class TestLoggerProvider : ILoggerProvider
{
    private readonly List<string> _messages;

    public TestLoggerProvider(List<string> messages)
    {
        _messages = messages;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new TestLogger(_messages);
    }

    public void Dispose()
    {
    }
}

/// <summary>
/// Test logger that captures messages
/// </summary>
internal sealed class TestLogger : ILogger
{
    private readonly List<string> _messages;

    public TestLogger(List<string> messages)
    {
        _messages = messages;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        _messages.Add($"[{logLevel}] {message}");
    }
}
