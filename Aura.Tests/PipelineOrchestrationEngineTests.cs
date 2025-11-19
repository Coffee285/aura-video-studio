using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aura.Core.AI;
using Aura.Core.Models;
using Aura.Core.Models.Narrative;
using Aura.Core.Models.Streaming;
using Aura.Core.Models.Visual;
using Aura.Core.Providers;
using Aura.Core.Services.Orchestration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Aura.Tests;

public class PipelineOrchestrationEngineTests
{
    private readonly ILogger<PipelineOrchestrationEngine> _engineLogger;
    private readonly ILogger<PipelineCache> _cacheLogger;
    private readonly ILogger<PipelineHealthCheck> _healthCheckLogger;

    public PipelineOrchestrationEngineTests()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        _engineLogger = loggerFactory.CreateLogger<PipelineOrchestrationEngine>();
        _cacheLogger = loggerFactory.CreateLogger<PipelineCache>();
        _healthCheckLogger = loggerFactory.CreateLogger<PipelineHealthCheck>();
    }

    [Fact]
    public async Task ExecutePipelineAsync_WithMinimalServices_ShouldSucceed()
    {
        var mockLlmProvider = new MockLlmProvider();
        var cache = new PipelineCache(_cacheLogger);
        var healthCheck = new PipelineHealthCheck(_healthCheckLogger, mockLlmProvider);
        
        var engine = new PipelineOrchestrationEngine(
            _engineLogger,
            mockLlmProvider,
            cache,
            healthCheck,
            new PipelineConfiguration
            {
                EnableCaching = false,
                EnableParallelExecution = false,
                MaxConcurrentLlmCalls = 1
            });

        var context = new PipelineExecutionContext
        {
            Brief = new Brief("Test Topic", null, null, "professional", "en", Aspect.Widescreen16x9),
            PlanSpec = new PlanSpec(TimeSpan.FromMinutes(1), Pacing.Conversational, Density.Balanced, "modern"),
            VoiceSpec = new VoiceSpec("test-voice", 1.0, 1.0, PauseStyle.Natural),
            RenderSpec = new RenderSpec(new Resolution(1920, 1080), "mp4", 5000, 192),
            SystemProfile = new SystemProfile
            {
                Tier = HardwareTier.B,
                LogicalCores = 4,
                PhysicalCores = 2,
                RamGB = 8
            }
        };

        var config = new PipelineConfiguration
        {
            EnableCaching = false,
            EnableParallelExecution = false,
            MaxConcurrentLlmCalls = 1
        };

        var result = await engine.ExecutePipelineAsync(context, config, null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.Success || result.Errors.Count == 0, $"Pipeline failed: {string.Join(", ", result.Errors)}");
        Assert.NotEmpty(result.ServiceResults);
        Assert.True(result.TotalExecutionTime > TimeSpan.Zero);
    }

    [Fact]
    public async Task PipelineHealthCheck_WithMissingRequiredServices_ShouldFailHealthCheck()
    {
        var healthCheck = new PipelineHealthCheck(_healthCheckLogger, null);

        var result = await healthCheck.CheckHealthAsync(CancellationToken.None);

        Assert.False(result.IsHealthy);
        Assert.Contains(result.MissingRequiredServices, msg => msg.Contains("LlmProvider"));
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public async Task PipelineHealthCheck_WithRequiredServices_ShouldPassHealthCheck()
    {
        var mockLlmProvider = new MockLlmProvider();
        var mockTtsProvider = new MockTtsProvider();
        
        var healthCheck = new PipelineHealthCheck(
            _healthCheckLogger,
            mockLlmProvider,
            mockTtsProvider);

        var result = await healthCheck.CheckHealthAsync(CancellationToken.None);

        Assert.True(result.IsHealthy);
        Assert.Empty(result.MissingRequiredServices);
        Assert.True(result.ServiceAvailability["LlmProvider"]);
        Assert.True(result.ServiceAvailability["TtsProvider"]);
    }

    [Fact]
    public void PipelineCache_SetAndGet_ShouldReturnValue()
    {
        var cache = new PipelineCache(_cacheLogger, maxEntries: 10);

        var key = cache.GenerateKey("test", "param1", "param2");
        cache.Set(key, "test-value");

        var found = cache.TryGet<string>(key, out var value);

        Assert.True(found);
        Assert.Equal("test-value", value);
    }

    [Fact]
    public void PipelineCache_ExpiredEntry_ShouldReturnFalse()
    {
        var cache = new PipelineCache(_cacheLogger, maxEntries: 10, defaultTtl: TimeSpan.FromMilliseconds(1));

        var key = cache.GenerateKey("test", "expire");
        cache.Set(key, "test-value");

        Thread.Sleep(10);

        var found = cache.TryGet<string>(key, out var value);

        Assert.False(found);
        Assert.Null(value);
    }

    [Fact]
    public void PipelineCache_Invalidate_ShouldRemoveEntry()
    {
        var cache = new PipelineCache(_cacheLogger);

        var key = cache.GenerateKey("test", "invalidate");
        cache.Set(key, "test-value");

        cache.Invalidate(key);

        var found = cache.TryGet<string>(key, out var value);

        Assert.False(found);
    }

    [Fact]
    public async Task ExecutePipelineAsync_WithCaching_ShouldUseCachedResults()
    {
        var mockLlmProvider = new MockLlmProvider();
        var cache = new PipelineCache(_cacheLogger);
        var healthCheck = new PipelineHealthCheck(_healthCheckLogger, mockLlmProvider);
        
        var engine = new PipelineOrchestrationEngine(
            _engineLogger,
            mockLlmProvider,
            cache,
            healthCheck,
            new PipelineConfiguration
            {
                EnableCaching = true,
                MaxConcurrentLlmCalls = 1
            });

        var context = new PipelineExecutionContext
        {
            Brief = new Brief("Cached Test", null, null, "professional", "en", Aspect.Widescreen16x9),
            PlanSpec = new PlanSpec(TimeSpan.FromMinutes(1), Pacing.Conversational, Density.Balanced, "modern"),
            VoiceSpec = new VoiceSpec("test-voice", 1.0, 1.0, PauseStyle.Natural),
            RenderSpec = new RenderSpec(new Resolution(1920, 1080), "mp4", 5000, 192),
            SystemProfile = new SystemProfile { Tier = HardwareTier.B, LogicalCores = 4 }
        };

        var config = new PipelineConfiguration
        {
            EnableCaching = true,
            EnableParallelExecution = false,
            MaxConcurrentLlmCalls = 1
        };

        var result1 = await engine.ExecutePipelineAsync(context, config, null, CancellationToken.None);
        var result2 = await engine.ExecutePipelineAsync(context, config, null, CancellationToken.None);

        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.True(result2.CacheHits > 0, "Second run should have cache hits");
    }

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

        public Task<string> DraftScriptAsync(Brief brief, PlanSpec spec, CancellationToken ct)
        {
            return Task.FromResult($"## Scene 1\nTest script content for {brief.Topic}\n\n## Scene 2\nMore content");
        }

        public Task<string> CompleteAsync(string prompt, CancellationToken ct)
        {
            return Task.FromResult("Mock response");
        }

        public Task<SceneAnalysisResult?> AnalyzeSceneImportanceAsync(string sceneText, string? previousSceneText, string videoGoal, CancellationToken ct)
        {
            return Task.FromResult<SceneAnalysisResult?>(new SceneAnalysisResult(
                Importance: 50.0,
                Complexity: 50.0,
                EmotionalIntensity: 50.0,
                InformationDensity: "medium",
                OptimalDurationSeconds: 10.0,
                TransitionType: "cut",
                Reasoning: "Mock analysis"
            ));
        }

        public Task<VisualPromptResult?> GenerateVisualPromptAsync(string sceneText, string? previousSceneText, string videoTone, VisualStyle targetStyle, CancellationToken ct)
        {
            return Task.FromResult<VisualPromptResult?>(null);
        }

        public Task<ContentComplexityAnalysisResult?> AnalyzeContentComplexityAsync(string sceneText, string? previousSceneText, string videoGoal, CancellationToken ct)
        {
            return Task.FromResult<ContentComplexityAnalysisResult?>(null);
        }

        public Task<SceneCoherenceResult?> AnalyzeSceneCoherenceAsync(string fromSceneText, string toSceneText, string videoGoal, CancellationToken ct)
        {
            return Task.FromResult<SceneCoherenceResult?>(new SceneCoherenceResult(
                CoherenceScore: 85.0,
                ConnectionTypes: new[] { "logical" },
                ConfidenceScore: 90.0,
                Reasoning: "Coherent"
            ));
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

    private sealed class MockTtsProvider : ITtsProvider
    {
        public string Name => "MockTTS";

        public Task<string> SynthesizeAsync(IEnumerable<ScriptLine> lines, VoiceSpec voiceSpec, CancellationToken ct)
        {
            return Task.FromResult("/tmp/mock-audio.wav");
        }

        public Task<IReadOnlyList<string>> GetAvailableVoicesAsync()
        {
            return Task.FromResult<IReadOnlyList<string>>(new List<string> { "test-voice" });
        }
    }
}
