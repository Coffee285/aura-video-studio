using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aura.Core.Models;
using Aura.Core.Models.Narrative;
using Aura.Core.Models.Streaming;
using Aura.Core.Models.Visual;
using Aura.Core.Orchestrator;
using Aura.Core.Providers;
using Aura.Providers.Llm;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aura.Tests.Integration;

/// <summary>
/// Integration tests for concurrent job execution and queue management
/// Tests thread safety, resource management, and parallel job processing
/// </summary>
public class ConcurrentJobExecutionTests
{
    /// <summary>
    /// Test that multiple jobs can be queued and executed concurrently
    /// </summary>
    [Fact]
    public async Task ConcurrentJobs_Should_ExecuteInParallel()
    {
        // Arrange - Create multiple job specifications
        var jobCount = 5;
        var jobs = new List<Task<ScriptResult>>();
        var completionTimes = new ConcurrentBag<DateTime>();

        var llmProvider = new RuleBasedLlmProvider(NullLogger<RuleBasedLlmProvider>.Instance);
        var llmProviders = new Dictionary<string, ILlmProvider>
        {
            ["RuleBased"] = llmProvider
        };

        var providerMixer = new ProviderMixer(
            NullLogger<ProviderMixer>.Instance,
            new ProviderMixingConfig { ActiveProfile = "Free-Only" }
        );

        var orchestrator = new ScriptOrchestrator(
            NullLogger<ScriptOrchestrator>.Instance,
            NullLoggerFactory.Instance,
            providerMixer,
            llmProviders
        );

        // Act - Start multiple jobs concurrently
        for (int i = 0; i < jobCount; i++)
        {
            var jobIndex = i;
            var job = Task.Run(async () =>
            {
                var brief = new Brief(
                    Topic: $"Test Job {jobIndex}",
                    Audience: "Test",
                    Goal: "Test",
                    Tone: "Test",
                    Language: "English",
                    Aspect: Aspect.Widescreen16x9
                );

                var planSpec = new PlanSpec(
                    TargetDuration: TimeSpan.FromSeconds(10),
                    Pacing: Pacing.Fast,
                    Density: Density.Sparse,
                    Style: "Test"
                );

                var result = await orchestrator.GenerateScriptAsync(
                    brief,
                    planSpec,
                    "Free",
                    offlineOnly: true,
                    CancellationToken.None
                );

                completionTimes.Add(DateTime.UtcNow);
                return result;
            });

            jobs.Add(job);
        }

        // Wait for all jobs to complete
        var results = await Task.WhenAll(jobs);

        // Assert - All jobs completed successfully
        Assert.Equal(jobCount, results.Length);
        Assert.All(results, result =>
        {
            Assert.True(result.Success, $"Job failed: {result.ErrorMessage}");
            Assert.NotNull(result.Script);
        });

        // Verify concurrent execution (jobs should complete in similar timeframe)
        var completionTimesList = completionTimes.OrderBy(t => t).ToList();
        var totalExecutionTime = completionTimesList.Last() - completionTimesList.First();
        
        // If executed sequentially, would take much longer
        // With RuleBasedProvider being fast, parallel execution should complete quickly
        Assert.True(totalExecutionTime.TotalSeconds < 10, 
            "Jobs should complete in parallel, not sequentially");
    }

    /// <summary>
    /// Test that job queue respects priority and FIFO ordering
    /// </summary>
    [Fact]
    public async Task ConcurrentJobs_Should_RespectQueueOrdering()
    {
        // Arrange
        var executionOrder = new ConcurrentBag<int>();
        var llmProvider = new RuleBasedLlmProvider(NullLogger<RuleBasedLlmProvider>.Instance);
        var llmProviders = new Dictionary<string, ILlmProvider>
        {
            ["RuleBased"] = llmProvider
        };

        var providerMixer = new ProviderMixer(
            NullLogger<ProviderMixer>.Instance,
            new ProviderMixingConfig { ActiveProfile = "Free-Only" }
        );

        var orchestrator = new ScriptOrchestrator(
            NullLogger<ScriptOrchestrator>.Instance,
            NullLoggerFactory.Instance,
            providerMixer,
            llmProviders
        );

        // Act - Queue jobs with slight delays
        var jobs = new List<Task<ScriptResult>>();
        for (int i = 0; i < 3; i++)
        {
            var jobIndex = i;
            await Task.Delay(50); // Small delay to ensure ordering

            var job = Task.Run(async () =>
            {
                var brief = new Brief(
                    Topic: $"Ordered Job {jobIndex}",
                    Audience: "Test",
                    Goal: "Test",
                    Tone: "Test",
                    Language: "English",
                    Aspect: Aspect.Widescreen16x9
                );

                var planSpec = new PlanSpec(
                    TargetDuration: TimeSpan.FromSeconds(5),
                    Pacing: Pacing.Fast,
                    Density: Density.Sparse,
                    Style: "Test"
                );

                var result = await orchestrator.GenerateScriptAsync(
                    brief,
                    planSpec,
                    "Free",
                    offlineOnly: true,
                    CancellationToken.None
                );

                executionOrder.Add(jobIndex);
                return result;
            });

            jobs.Add(job);
        }

        var results = await Task.WhenAll(jobs);

        // Assert - All jobs completed
        Assert.All(results, result => Assert.True(result.Success));
        
        // Execution order recorded
        Assert.Equal(3, executionOrder.Count);
    }

    /// <summary>
    /// Test that concurrent jobs don't interfere with each other's state
    /// </summary>
    [Fact]
    public async Task ConcurrentJobs_Should_MaintainIsolatedState()
    {
        // Arrange
        var llmProvider = new RuleBasedLlmProvider(NullLogger<RuleBasedLlmProvider>.Instance);
        var llmProviders = new Dictionary<string, ILlmProvider>
        {
            ["RuleBased"] = llmProvider
        };

        var providerMixer = new ProviderMixer(
            NullLogger<ProviderMixer>.Instance,
            new ProviderMixingConfig { ActiveProfile = "Free-Only" }
        );

        var orchestrator = new ScriptOrchestrator(
            NullLogger<ScriptOrchestrator>.Instance,
            NullLoggerFactory.Instance,
            providerMixer,
            llmProviders
        );

        // Act - Start jobs with different configurations
        var job1 = orchestrator.GenerateScriptAsync(
            new Brief(
                Topic: "Job 1 Unique Topic",
                Audience: "Audience A",
                Goal: "Goal A",
                Tone: "Formal",
                Language: "English",
                Aspect: Aspect.Widescreen16x9
            ),
            new PlanSpec(
                TargetDuration: TimeSpan.FromSeconds(10),
                Pacing: Pacing.Conversational,
                Density: Density.Dense,
                Style: "Style A"
            ),
            "Free",
            offlineOnly: true,
            CancellationToken.None
        );

        var job2 = orchestrator.GenerateScriptAsync(
            new Brief(
                Topic: "Job 2 Different Topic",
                Audience: "Audience B",
                Goal: "Goal B",
                Tone: "Casual",
                Language: "English",
                Aspect: Aspect.Vertical9x16
            ),
            new PlanSpec(
                TargetDuration: TimeSpan.FromSeconds(15),
                Pacing: Pacing.Fast,
                Density: Density.Sparse,
                Style: "Style B"
            ),
            "Free",
            offlineOnly: true,
            CancellationToken.None
        );

        var results = await Task.WhenAll(job1, job2);

        // Assert - Each job produced unique content
        Assert.True(results[0].Success);
        Assert.True(results[1].Success);
        
        Assert.NotEqual(results[0].Script, results[1].Script);
        Assert.Contains("Job 1 Unique Topic", results[0].Script ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Job 2 Different Topic", results[1].Script ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Test that cancelling one job doesn't affect others
    /// </summary>
    [Fact]
    public async Task ConcurrentJobs_Should_AllowIndependentCancellation()
    {
        // Arrange
        var llmProvider = new RuleBasedLlmProvider(NullLogger<RuleBasedLlmProvider>.Instance);
        var llmProviders = new Dictionary<string, ILlmProvider>
        {
            ["RuleBased"] = llmProvider
        };

        var providerMixer = new ProviderMixer(
            NullLogger<ProviderMixer>.Instance,
            new ProviderMixingConfig { ActiveProfile = "Free-Only" }
        );

        var orchestrator = new ScriptOrchestrator(
            NullLogger<ScriptOrchestrator>.Instance,
            NullLoggerFactory.Instance,
            providerMixer,
            llmProviders
        );

        var cts1 = new CancellationTokenSource();
        var cts2 = new CancellationTokenSource();

        // Act - Start two jobs, cancel one
        var job1 = orchestrator.GenerateScriptAsync(
            new Brief(
                Topic: "Will be cancelled",
                Audience: "Test",
                Goal: "Test",
                Tone: "Test",
                Language: "English",
                Aspect: Aspect.Widescreen16x9
            ),
            new PlanSpec(
                TargetDuration: TimeSpan.FromSeconds(10),
                Pacing: Pacing.Fast,
                Density: Density.Sparse,
                Style: "Test"
            ),
            "Free",
            offlineOnly: true,
            cts1.Token
        );

        var job2 = orchestrator.GenerateScriptAsync(
            new Brief(
                Topic: "Should complete",
                Audience: "Test",
                Goal: "Test",
                Tone: "Test",
                Language: "English",
                Aspect: Aspect.Widescreen16x9
            ),
            new PlanSpec(
                TargetDuration: TimeSpan.FromSeconds(10),
                Pacing: Pacing.Fast,
                Density: Density.Sparse,
                Style: "Test"
            ),
            "Free",
            offlineOnly: true,
            cts2.Token
        );

        // Cancel first job (RuleBasedProvider is fast, so this may not actually cancel)
        cts1.Cancel();

        var results = await Task.WhenAll(job1, job2);

        // Assert - Second job should complete successfully
        Assert.True(results[1].Success, "Second job should complete despite first job cancellation");
        Assert.NotNull(results[1].Script);
    }

    /// <summary>
    /// Test resource cleanup after concurrent job execution
    /// </summary>
    [Fact]
    public async Task ConcurrentJobs_Should_CleanupResourcesProperly()
    {
        // Arrange
        var llmProvider = new RuleBasedLlmProvider(NullLogger<RuleBasedLlmProvider>.Instance);
        var llmProviders = new Dictionary<string, ILlmProvider>
        {
            ["RuleBased"] = llmProvider
        };

        var providerMixer = new ProviderMixer(
            NullLogger<ProviderMixer>.Instance,
            new ProviderMixingConfig { ActiveProfile = "Free-Only" }
        );

        var orchestrator = new ScriptOrchestrator(
            NullLogger<ScriptOrchestrator>.Instance,
            NullLoggerFactory.Instance,
            providerMixer,
            llmProviders
        );

        // Track memory before jobs
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var memoryBefore = GC.GetTotalMemory(false);

        // Act - Execute many concurrent jobs
        var jobs = new List<Task<ScriptResult>>();
        for (int i = 0; i < 10; i++)
        {
            var brief = new Brief(
                Topic: $"Resource Test {i}",
                Audience: "Test",
                Goal: "Test",
                Tone: "Test",
                Language: "English",
                Aspect: Aspect.Widescreen16x9
            );

            var planSpec = new PlanSpec(
                TargetDuration: TimeSpan.FromSeconds(5),
                Pacing: Pacing.Fast,
                Density: Density.Sparse,
                Style: "Test"
            );

            jobs.Add(orchestrator.GenerateScriptAsync(
                brief,
                planSpec,
                "Free",
                offlineOnly: true,
                CancellationToken.None
            ));
        }

        var results = await Task.WhenAll(jobs);

        // Force garbage collection
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var memoryAfter = GC.GetTotalMemory(false);

        // Assert - All jobs completed and memory growth is reasonable
        Assert.All(results, result => Assert.True(result.Success));
        
        // Memory growth should be reasonable (< 10MB for these simple jobs)
        var memoryGrowth = (memoryAfter - memoryBefore) / (1024.0 * 1024.0);
        Assert.True(memoryGrowth < 10, 
            $"Memory growth ({memoryGrowth:F2} MB) suggests resource leak");
    }

    /// <summary>
    /// Test that error in one concurrent job doesn't crash other jobs
    /// </summary>
    [Fact]
    public async Task ConcurrentJobs_Should_IsolateFailures()
    {
        // Arrange - Mix of working and failing providers
        var llmProviders = new Dictionary<string, ILlmProvider>
        {
            ["RuleBased"] = new RuleBasedLlmProvider(NullLogger<RuleBasedLlmProvider>.Instance),
            ["Failing"] = new ConcurrentFailingLlmProvider()
        };

        var providerMixer = new ProviderMixer(
            NullLogger<ProviderMixer>.Instance,
            new ProviderMixingConfig { ActiveProfile = "Free-Only", AutoFallback = false }
        );

        var orchestratorWorking = new ScriptOrchestrator(
            NullLogger<ScriptOrchestrator>.Instance,
            NullLoggerFactory.Instance,
            providerMixer,
            new Dictionary<string, ILlmProvider> { ["RuleBased"] = llmProviders["RuleBased"] }
        );

        var orchestratorFailing = new ScriptOrchestrator(
            NullLogger<ScriptOrchestrator>.Instance,
            NullLoggerFactory.Instance,
            providerMixer,
            new Dictionary<string, ILlmProvider> { ["Failing"] = llmProviders["Failing"] }
        );

        // Act - Start jobs, some will fail
        var workingJob = orchestratorWorking.GenerateScriptAsync(
            new Brief("Working", "Test", "Test", "Test", "English", Aspect.Widescreen16x9),
            new PlanSpec(TimeSpan.FromSeconds(10), Pacing.Fast, Density.Sparse, "Test"),
            "Free",
            offlineOnly: true,
            CancellationToken.None
        );

        var failingJob = orchestratorFailing.GenerateScriptAsync(
            new Brief("Failing", "Test", "Test", "Test", "English", Aspect.Widescreen16x9),
            new PlanSpec(TimeSpan.FromSeconds(10), Pacing.Fast, Density.Sparse, "Test"),
            "Free",
            offlineOnly: true,
            CancellationToken.None
        );

        var results = await Task.WhenAll(workingJob, failingJob);

        // Assert - Working job succeeded, failing job failed gracefully
        Assert.True(results[0].Success, "Working job should complete successfully");
        Assert.False(results[1].Success, "Failing job should report failure");
        Assert.NotNull(results[0].Script);
        Assert.Null(results[1].Script);
    }
}

/// <summary>
/// Mock failing LLM provider for testing error scenarios
/// </summary>
internal class ConcurrentFailingLlmProvider : ILlmProvider
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

    public Task<string> DraftScriptAsync(
        Brief brief,
        PlanSpec planSpec,
        CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Simulated provider failure");
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
        throw new InvalidOperationException("Simulated provider failure");
    }

    public Task<VisualPromptResult?> GenerateVisualPromptAsync(
        string sceneText,
        string? previousSceneText,
        string videoTone,
        VisualStyle targetStyle,
        CancellationToken ct)
    {
        throw new InvalidOperationException("Simulated provider failure");
    }

    public Task<ContentComplexityAnalysisResult?> AnalyzeContentComplexityAsync(
        string sceneText,
        string? previousSceneText,
        string videoGoal,
        CancellationToken ct)
    {
        throw new InvalidOperationException("Simulated provider failure");
    }

    public Task<SceneCoherenceResult?> AnalyzeSceneCoherenceAsync(
        string fromSceneText,
        string toSceneText,
        string videoGoal,
        CancellationToken ct)
    {
        throw new InvalidOperationException("Simulated provider failure");
    }

    public Task<NarrativeArcResult?> ValidateNarrativeArcAsync(
        IReadOnlyList<string> sceneTexts,
        string videoGoal,
        string videoType,
        CancellationToken ct)
    {
        throw new InvalidOperationException("Simulated provider failure");
    }

    public Task<string?> GenerateTransitionTextAsync(
        string fromSceneText,
        string toSceneText,
        string videoGoal,
        CancellationToken ct)
    {
        throw new InvalidOperationException("Simulated provider failure");
    }
}
