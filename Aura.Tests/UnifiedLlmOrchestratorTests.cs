using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Aura.Core.AI.Cache;
using Aura.Core.AI.Orchestration;
using Aura.Core.AI.Validation;
using Aura.Core.Models;
using Aura.Core.Models.Narrative;
using Aura.Core.Models.Streaming;
using Aura.Core.Models.Visual;
using Aura.Core.Providers;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Aura.Tests;

public class UnifiedLlmOrchestratorTests
{
    [Fact]
    public async Task ExecuteAsync_WithValidResponse_ReturnsSuccess()
    {
        var logger = new LoggerFactory().CreateLogger<UnifiedLlmOrchestrator>();
        var cache = new MemoryLlmCache(
            new LoggerFactory().CreateLogger<MemoryLlmCache>(), 
            Microsoft.Extensions.Options.Options.Create(new LlmCacheOptions()));
        var budgetManager = new LlmBudgetManager(new LoggerFactory().CreateLogger<LlmBudgetManager>());
        var telemetryCollector = new LlmTelemetryCollector();
        var schemaValidator = new SchemaValidator(new LoggerFactory().CreateLogger<SchemaValidator>());
        
        var orchestrator = new UnifiedLlmOrchestrator(
            logger,
            cache,
            budgetManager,
            telemetryCollector,
            schemaValidator);
        
        var provider = new MockLlmProvider("Test response content");
        
        var request = new LlmOperationRequest
        {
            SessionId = "test-session",
            OperationType = LlmOperationType.Completion,
            Prompt = "Test prompt",
            EnableCache = false
        };
        
        var response = await orchestrator.ExecuteAsync(request, provider);
        
        Assert.True(response.Success);
        Assert.Equal("Test response content", response.Content);
        Assert.False(response.WasCached);
        Assert.NotNull(response.Telemetry);
        Assert.Equal(LlmOperationType.Completion, response.Telemetry.OperationType);
    }
    
    [Fact]
    public async Task ExecuteAsync_WithCache_ReturnsCachedResult()
    {
        var logger = new LoggerFactory().CreateLogger<UnifiedLlmOrchestrator>();
        var cache = new MemoryLlmCache(
            new LoggerFactory().CreateLogger<MemoryLlmCache>(), 
            Microsoft.Extensions.Options.Options.Create(new LlmCacheOptions()));
        var budgetManager = new LlmBudgetManager(new LoggerFactory().CreateLogger<LlmBudgetManager>());
        var telemetryCollector = new LlmTelemetryCollector();
        var schemaValidator = new SchemaValidator(new LoggerFactory().CreateLogger<SchemaValidator>());
        
        var orchestrator = new UnifiedLlmOrchestrator(
            logger,
            cache,
            budgetManager,
            telemetryCollector,
            schemaValidator);
        
        var provider = new MockLlmProvider("Test response");
        
        var request = new LlmOperationRequest
        {
            SessionId = "test-session",
            OperationType = LlmOperationType.Completion,
            Prompt = "Test prompt",
            EnableCache = true
        };
        
        var response1 = await orchestrator.ExecuteAsync(request, provider);
        Assert.True(response1.Success);
        Assert.False(response1.WasCached);
        
        var response2 = await orchestrator.ExecuteAsync(request, provider);
        Assert.True(response2.Success);
        Assert.True(response2.WasCached);
    }
    
    [Fact]
    public async Task ExecuteAsync_WithBudgetExceeded_ReturnsFailure()
    {
        var logger = new LoggerFactory().CreateLogger<UnifiedLlmOrchestrator>();
        var cache = new MemoryLlmCache(
            new LoggerFactory().CreateLogger<MemoryLlmCache>(), 
            Microsoft.Extensions.Options.Options.Create(new LlmCacheOptions()));
        var budgetConstraint = new LlmBudgetConstraint
        {
            MaxTokensPerOperation = 10,
            EnforceHardLimits = true
        };
        var budgetManager = new LlmBudgetManager(new LoggerFactory().CreateLogger<LlmBudgetManager>(), budgetConstraint);
        var telemetryCollector = new LlmTelemetryCollector();
        var schemaValidator = new SchemaValidator(new LoggerFactory().CreateLogger<SchemaValidator>());
        
        var orchestrator = new UnifiedLlmOrchestrator(
            logger,
            cache,
            budgetManager,
            telemetryCollector,
            schemaValidator);
        
        var provider = new MockLlmProvider("Test response");
        
        var request = new LlmOperationRequest
        {
            SessionId = "test-session",
            OperationType = LlmOperationType.Completion,
            Prompt = "This is a very long prompt that will exceed the token budget limit",
            EnableCache = false,
            BudgetConstraint = budgetConstraint
        };
        
        var response = await orchestrator.ExecuteAsync(request, provider);
        
        Assert.False(response.Success);
        Assert.Contains("Budget exceeded", response.ErrorMessage);
    }
    
    [Fact]
    public void GetSessionStatistics_ReturnsCorrectStats()
    {
        var logger = new LoggerFactory().CreateLogger<UnifiedLlmOrchestrator>();
        var cache = new MemoryLlmCache(
            new LoggerFactory().CreateLogger<MemoryLlmCache>(), 
            Microsoft.Extensions.Options.Options.Create(new LlmCacheOptions()));
        var budgetManager = new LlmBudgetManager(new LoggerFactory().CreateLogger<LlmBudgetManager>());
        var telemetryCollector = new LlmTelemetryCollector();
        var schemaValidator = new SchemaValidator(new LoggerFactory().CreateLogger<SchemaValidator>());
        
        var orchestrator = new UnifiedLlmOrchestrator(
            logger,
            cache,
            budgetManager,
            telemetryCollector,
            schemaValidator);
        
        var telemetry = new LlmOperationTelemetry
        {
            SessionId = "test-session",
            OperationType = LlmOperationType.Planning,
            ProviderName = "OpenAI",
            ModelName = "gpt-4",
            TokensIn = 100,
            TokensOut = 200,
            Success = true,
            CacheHit = false,
            LatencyMs = 1500,
            EstimatedCost = 0.05m
        };
        
        telemetryCollector.Record(telemetry);
        
        var stats = orchestrator.GetSessionStatistics("test-session");
        
        Assert.Equal(1, stats.TotalOperations);
        Assert.Equal(1, stats.SuccessfulOperations);
        Assert.Equal(100, stats.TotalTokensIn);
        Assert.Equal(200, stats.TotalTokensOut);
        Assert.Equal(0.05m, stats.TotalEstimatedCost);
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

        private readonly string _response;
        
        public MockLlmProvider(string response)
        {
            _response = response;
        }
        
        public Task<string> CompleteAsync(string prompt, CancellationToken ct)
        {
            return Task.FromResult(_response);
        }
        
        public Task<string> DraftScriptAsync(Brief brief, PlanSpec spec, CancellationToken ct)
        {
            return Task.FromResult(_response);
        }
        
        public Task<SceneAnalysisResult?> AnalyzeSceneImportanceAsync(
            string sceneText, string? previousSceneText, string videoGoal, CancellationToken ct)
        {
            return Task.FromResult<SceneAnalysisResult?>(null);
        }
        
        public Task<VisualPromptResult?> GenerateVisualPromptAsync(
            string sceneText, string? previousSceneText, string videoTone, 
            VisualStyle targetStyle, CancellationToken ct)
        {
            return Task.FromResult<VisualPromptResult?>(null);
        }
        
        public Task<ContentComplexityAnalysisResult?> AnalyzeContentComplexityAsync(
            string sceneText, string? previousSceneText, string videoGoal, CancellationToken ct)
        {
            return Task.FromResult<ContentComplexityAnalysisResult?>(null);
        }
        
        public Task<SceneCoherenceResult?> AnalyzeSceneCoherenceAsync(
            string fromSceneText, string toSceneText, string videoGoal, CancellationToken ct)
        {
            return Task.FromResult<SceneCoherenceResult?>(null);
        }
        
        public Task<NarrativeArcResult?> ValidateNarrativeArcAsync(
            System.Collections.Generic.IReadOnlyList<string> sceneTexts, 
            string videoGoal, string videoType, CancellationToken ct)
        {
            return Task.FromResult<NarrativeArcResult?>(null);
        }
        
        public Task<string?> GenerateTransitionTextAsync(
            string fromSceneText, string toSceneText, string videoGoal, CancellationToken ct)
        {
            return Task.FromResult<string?>(_response);
        }
    }
}
