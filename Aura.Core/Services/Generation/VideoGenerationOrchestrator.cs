using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aura.Core.Audio;
using Aura.Core.Models;
using Aura.Core.Models.Generation;
using Aura.Core.Orchestrator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TaskStatus = Aura.Core.Models.Generation.TaskStatus;

namespace Aura.Core.Services.Generation;

/// <summary>
/// Central coordinator for AI-generated video components.
/// Manages dependencies, optimizes task scheduling, and ensures efficient resource utilization.
/// </summary>
public class VideoGenerationOrchestrator
{
    // Task ID constants for critical pipeline tasks
    private const string ScriptTaskId = "script";
    private const string AudioTaskId = "audio";
    private const string CompositionTaskId = "composition";
    private const string CaptionsTaskId = "captions";
    private const string VisualTaskIdPrefix = "visual_";
    private const string RecoveryRootFolderName = "AuraVideoStudio";

    private readonly ILogger<VideoGenerationOrchestrator> _logger;
    private readonly ResourceMonitor _resourceMonitor;
    private readonly StrategySelector _strategySelector;
    private readonly OrchestratorOptions _options;
    private readonly ConcurrentDictionary<string, TaskResult> _taskResults = new();
    private readonly SemaphoreSlim _concurrencyLimiter;

    public VideoGenerationOrchestrator(
        ILogger<VideoGenerationOrchestrator> logger,
        ResourceMonitor resourceMonitor,
        StrategySelector strategySelector,
        OrchestratorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(resourceMonitor);
        ArgumentNullException.ThrowIfNull(strategySelector);

        _logger = logger;
        _resourceMonitor = resourceMonitor;
        _strategySelector = strategySelector;
        _options = options ?? OrchestratorOptions.CreateDefault();
        _concurrencyLimiter = new SemaphoreSlim(_options.MaxConcurrency, _options.MaxConcurrency);

        _logger.LogInformation(
            "VideoGenerationOrchestrator initialized with TaskTimeout={TaskTimeout}min, BatchTimeout={BatchTimeout}min, StuckThreshold={StuckThreshold}s",
            _options.TaskTimeout.TotalMinutes,
            _options.BatchTimeout.TotalMinutes,
            _options.StuckDetectionThresholdSeconds);
    }

    /// <summary>
    /// Orchestrates the generation of video components with intelligent scheduling
    /// </summary>
    public async Task<OrchestrationResult> OrchestrateGenerationAsync(
        Brief brief,
        PlanSpec planSpec,
        SystemProfile systemProfile,
        Func<GenerationNode, CancellationToken, Task<object>> taskExecutor,
        IProgress<OrchestrationProgress>? progress = null,
        CancellationToken ct = default,
        Action<string, object>? recoveryResultsCallback = null)
    {
        ArgumentNullException.ThrowIfNull(brief);
        ArgumentNullException.ThrowIfNull(planSpec);
        ArgumentNullException.ThrowIfNull(systemProfile);
        ArgumentNullException.ThrowIfNull(taskExecutor);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("Starting video generation orchestration for topic: {Topic}", brief.Topic);

            // Report initial progress immediately to show activity
            progress?.Report(new OrchestrationProgress("Starting orchestration", 0, 1, TimeSpan.Zero));

            // Select generation strategy
            var strategy = _strategySelector.SelectStrategy(brief, systemProfile, planSpec);
            UpdateConcurrencyLimit(strategy.MaxConcurrency);

            progress?.Report(new OrchestrationProgress("Analyzing dependencies and building task graph", 0, 1, stopwatch.Elapsed));

            // Build dependency graph
            var graph = BuildDependencyGraph(brief, planSpec, strategy);
            _logger.LogInformation("Built dependency graph with {NodeCount} tasks", graph.NodeCount);

            progress?.Report(new OrchestrationProgress($"Task graph ready with {graph.NodeCount} tasks", 0, graph.NodeCount, stopwatch.Elapsed));

            // Get optimal execution batches
            var batches = graph.GetOptimalExecutionBatches();
            _logger.LogInformation("Organized tasks into {BatchCount} execution batches", batches.Count);

            // Execute batches
            int totalTasks = batches.Sum(b => b.Count);
            int completedTasks = 0;
            int failedTasks = 0;

            foreach (var batch in batches)
            {
                _logger.LogInformation("Executing batch with {TaskCount} tasks", batch.Count);

                // Before executing batch, handle any failed visual dependencies by providing fallbacks
                // This allows composition to proceed even if some image generation failed
                await HandleFailedVisualDependenciesAsync(batch, graph).ConfigureAwait(false);

                // CRITICAL: Validate all dependencies are in succeeded state before executing batch
                foreach (var node in batch)
                {
                    var dependencies = graph.GetDependencies(node.TaskId);
                    _logger.LogDebug("Task {TaskId} has {DependencyCount} dependencies: [{Dependencies}]",
                        node.TaskId, dependencies.Count, string.Join(", ", dependencies));

                    foreach (var depTaskId in dependencies)
                    {
                        var depNode = graph.GetNode(depTaskId);
                        if (depNode == null)
                        {
                            var error = $"Dependency validation failed: Task '{node.TaskId}' depends on '{depTaskId}' which was not found in graph";
                            _logger.LogError(error);
                            throw new OrchestrationException(error);
                        }

                        if (depNode.Status != TaskStatus.Completed)
                        {
                            // Check if this is a non-critical visual dependency that we can skip
                            if (depNode.TaskType == GenerationTaskType.ImageGeneration)
                            {
                                _logger.LogWarning("Visual dependency {DependencyTaskId} has status {Status}, providing fallback for composition",
                                    depTaskId, depNode.Status);

                                // Provide fallback (empty asset list) for failed visual task
                                depNode.Status = TaskStatus.Completed;
                                depNode.Result = Array.Empty<object>();
                                depNode.CompletedAt = DateTime.UtcNow;
                                _taskResults[depNode.TaskId] = new TaskResult(depNode.TaskId, true, depNode.Result, null);
                            }
                            else
                            {
                                var error = $"Dependency validation failed: Task '{node.TaskId}' depends on '{depTaskId}' which has status '{depNode.Status}' (expected: Completed). Task execution order violation detected.";
                                _logger.LogError(error);
                                throw new OrchestrationException(error);
                            }
                        }

                        _logger.LogDebug("Dependency validation passed: Task {TaskId} <- {DependencyTaskId} (Status: {Status})",
                            node.TaskId, depTaskId, depNode.Status);
                    }
                }

                progress?.Report(new OrchestrationProgress(
                    $"Processing batch ({completedTasks}/{totalTasks} tasks completed)",
                    completedTasks,
                    totalTasks,
                    stopwatch.Elapsed));

                var batchResults = await ExecuteBatchAsync(
                    batch,
                    strategy,
                    taskExecutor,
                    progress,
                    completedTasks,
                    totalTasks,
                    stopwatch,
                    ct).ConfigureAwait(false);

                completedTasks += batchResults.Count(r => r.Succeeded);
                failedTasks += batchResults.Count(r => !r.Succeeded);

                // Report progress after batch completion
                progress?.Report(new OrchestrationProgress(
                    $"Batch completed ({completedTasks}/{totalTasks} tasks done)",
                    completedTasks,
                    totalTasks,
                    stopwatch.Elapsed));

                // Check for critical failures (visual task failures are NOT critical)
                if (HasCriticalFailures(batchResults, graph))
                {
                    _logger.LogError("Critical task failures detected, attempting recovery");

                    if (!await AttemptRecoveryAsync(batchResults, graph, planSpec, strategy, taskExecutor, recoveryResultsCallback, ct).ConfigureAwait(false))
                    {
                        _logger.LogError("Recovery failed, aborting orchestration");
                        throw new OrchestrationException("Critical task failures could not be recovered");
                    }

                    completedTasks++;
                }
            }

            stopwatch.Stop();

            var result = new OrchestrationResult(
                Succeeded: failedTasks == 0,
                TotalTasks: totalTasks,
                CompletedTasks: completedTasks,
                FailedTasks: failedTasks,
                ExecutionTime: stopwatch.Elapsed,
                Strategy: strategy,
                TaskResults: _taskResults);

            // Record strategy performance
            _strategySelector.RecordStrategyPerformance(
                strategy,
                stopwatch.Elapsed,
                result.Succeeded,
                result.QualityScore);

            _logger.LogInformation(
                "Orchestration completed: {Status}, {Completed}/{Total} tasks, Time: {Time}",
                result.Succeeded ? "Success" : "Failed",
                completedTasks,
                totalTasks,
                stopwatch.Elapsed);

            progress?.Report(new OrchestrationProgress(
                "Orchestration completed",
                completedTasks,
                totalTasks,
                stopwatch.Elapsed));

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Orchestration failed with exception");
            throw;
        }
    }

    /// <summary>
    /// Builds the dependency graph for video generation tasks
    /// </summary>
    private AssetDependencyGraph BuildDependencyGraph(Brief brief, PlanSpec planSpec, GenerationStrategy strategy)
    {
        var graph = new AssetDependencyGraph();

        // Estimate number of scenes
        int estimatedScenes = EstimateSceneCount(planSpec.TargetDuration);

        // Add script generation task (highest priority, must complete first)
        graph.AddNode(ScriptTaskId, GenerationTaskType.ScriptGeneration, priority: 100, estimatedResourceCost: 0.3);

        // Add audio generation task (depends on script)
        graph.AddNode(AudioTaskId, GenerationTaskType.AudioGeneration, priority: 90, estimatedResourceCost: 0.5);
        graph.AddDependency(ScriptTaskId, AudioTaskId);

        // Add visual generation tasks (can run in parallel, depend on script)
        for (int i = 0; i < estimatedScenes; i++)
        {
            string visualTaskId = $"{VisualTaskIdPrefix}{i}";
            graph.AddNode(visualTaskId, GenerationTaskType.ImageGeneration, priority: 50, estimatedResourceCost: 0.4);
            graph.AddDependency(ScriptTaskId, visualTaskId);
        }

        // Add caption generation task (depends on audio)
        if (planSpec.Style.Contains("caption", StringComparison.OrdinalIgnoreCase))
        {
            graph.AddNode(CaptionsTaskId, GenerationTaskType.CaptionGeneration, priority: 40, estimatedResourceCost: 0.2);
            graph.AddDependency(AudioTaskId, CaptionsTaskId);
        }

        // Add video composition task (depends on all assets)
        graph.AddNode(CompositionTaskId, GenerationTaskType.VideoComposition, priority: 10, estimatedResourceCost: 0.8);
        graph.AddDependency(AudioTaskId, CompositionTaskId);

        for (int i = 0; i < estimatedScenes; i++)
        {
            graph.AddDependency($"{VisualTaskIdPrefix}{i}", CompositionTaskId);
        }

        if (graph.ContainsTask(CaptionsTaskId))
        {
            graph.AddDependency(CaptionsTaskId, CompositionTaskId);
        }

        return graph;
    }

    /// <summary>
    /// Handles failed visual dependencies before a batch runs by providing fallback results.
    /// This allows composition to proceed even if image generation failed or timed out.
    /// </summary>
    private Task HandleFailedVisualDependenciesAsync(List<GenerationNode> batch, AssetDependencyGraph graph)
    {
        // Check if this batch contains the composition task
        var compositionTask = batch.Find(n => n.TaskId == CompositionTaskId);
        if (compositionTask == null)
        {
            return Task.CompletedTask;
        }

        // Get all dependencies of composition and check for failed visual tasks
        var dependencies = graph.GetDependencies(CompositionTaskId);
        foreach (var depTaskId in dependencies)
        {
            var depNode = graph.GetNode(depTaskId);
            if (depNode == null) continue;

            // Check if this is a failed/timed-out visual task
            if (depNode.TaskType == GenerationTaskType.ImageGeneration &&
                depNode.Status != TaskStatus.Completed)
            {
                _logger.LogWarning(
                    "Visual task {TaskId} has status {Status}, providing fallback for composition to proceed",
                    depTaskId, depNode.Status);

                // Provide empty fallback result so composition can proceed with placeholder
                depNode.Status = TaskStatus.Completed;
                depNode.Result = Array.Empty<object>();
                depNode.ErrorMessage = $"Original status: {depNode.Status} - using placeholder fallback";
                depNode.CompletedAt = DateTime.UtcNow;

                _taskResults[depNode.TaskId] = new TaskResult(depNode.TaskId, true, depNode.Result, null);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Executes a batch of tasks in parallel with resource management and configurable timeouts.
    /// Uses per-task timeout and batch-level timeout from OrchestratorOptions.
    /// </summary>
    private async Task<List<TaskResult>> ExecuteBatchAsync(
        List<GenerationNode> batch,
        GenerationStrategy strategy,
        Func<GenerationNode, CancellationToken, Task<object>> taskExecutor,
        IProgress<OrchestrationProgress>? progress,
        int baseCompletedTasks,
        int totalTasks,
        Stopwatch stopwatch,
        CancellationToken ct)
    {
        var results = new ConcurrentBag<TaskResult>();
        var batchCompletedCount = 0;

        // Use configurable timeouts from options, but shorten for visual batches to fail fast and allow placeholders.
        var isVisualBatch = batch.All(n => n.TaskType == GenerationTaskType.ImageGeneration);
        var taskTimeout = isVisualBatch ? TimeSpan.FromMinutes(2) : _options.TaskTimeout;
        var batchTimeout = isVisualBatch ? TimeSpan.FromMinutes(6) : _options.BatchTimeout;

        _logger.LogDebug(
            "Starting batch execution with {TaskCount} tasks, TaskTimeout={TaskTimeout}min, BatchTimeout={BatchTimeout}min, IsVisualBatch={IsVisualBatch}",
            batch.Count, taskTimeout.TotalMinutes, batchTimeout.TotalMinutes, isVisualBatch);

        // Create batch-level timeout to prevent indefinite hangs
        using var batchTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        batchTimeoutCts.CancelAfter(batchTimeout);

        var tasks = batch.Select(async node =>
        {
            bool resourcesAcquired = false;
            bool limiterAcquired = false;

            try
            {
                // Check if batch timeout has already expired
                batchTimeoutCts.Token.ThrowIfCancellationRequested();

                // Wait for resources with timeout
                using var resourceTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(batchTimeoutCts.Token);
                resourceTimeoutCts.CancelAfter(TimeSpan.FromMinutes(2)); // 2 minute timeout for resource acquisition

                try
                {
                    await _resourceMonitor.WaitForResourcesAsync(node.EstimatedResourceCost, resourceTimeoutCts.Token).ConfigureAwait(false);
                    resourcesAcquired = true;
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    _logger.LogWarning("Resource acquisition timeout for task {TaskId}", node.TaskId);
                    throw new TimeoutException($"Resource acquisition timeout for task {node.TaskId}");
                }

                // Acquire concurrency slot with timeout
                using var limiterTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(batchTimeoutCts.Token);
                limiterTimeoutCts.CancelAfter(TimeSpan.FromMinutes(1)); // 1 minute timeout for limiter

                try
                {
                    await _concurrencyLimiter.WaitAsync(limiterTimeoutCts.Token).ConfigureAwait(false);
                    limiterAcquired = true;
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    _logger.LogWarning("Concurrency limiter timeout for task {TaskId}", node.TaskId);
                    throw new TimeoutException($"Concurrency limiter timeout for task {node.TaskId}");
                }

                try
                {
                    node.Status = TaskStatus.Running;
                    node.StartedAt = DateTime.UtcNow;

                    // Report task starting
                    var stageName = GetTaskStageName(node.TaskType);
                    progress?.Report(new OrchestrationProgress(
                        $"Executing: {stageName}",
                        baseCompletedTasks + batchCompletedCount,
                        totalTasks,
                        stopwatch.Elapsed));

                    _logger.LogInformation("Executing task: {TaskId} ({TaskType}) - Priority: {Priority}, ResourceCost: {ResourceCost}",
                        node.TaskId, node.TaskType, node.Priority, node.EstimatedResourceCost);

                    // Execute task with timeout (uses batch timeout as base)
                    using var taskTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(batchTimeoutCts.Token);
                    taskTimeoutCts.CancelAfter(taskTimeout);

                    object result;
                    try
                    {
                        result = await taskExecutor(node, taskTimeoutCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        var timeoutMessage = batchTimeoutCts.IsCancellationRequested
                            ? $"Task {node.TaskId} aborted due to batch timeout ({batchTimeout.TotalMinutes} minutes)"
                            : $"Task {node.TaskId} timed out after {taskTimeout.TotalMinutes} minutes";
                        _logger.LogWarning("Task execution timeout for task {TaskId}: {Message}", node.TaskId, timeoutMessage);
                        throw new TimeoutException(timeoutMessage);
                    }

                    node.Status = TaskStatus.Completed;
                    node.Result = result;
                    node.CompletedAt = DateTime.UtcNow;

                    var taskResult = new TaskResult(node.TaskId, true, result, null);
                    _taskResults[node.TaskId] = taskResult;
                    results.Add(taskResult);

                    // CRITICAL FIX: Log what was stored for composition task to verify result storage
                    if (node.TaskId == CompositionTaskId)
                    {
                        _logger.LogInformation("[Composition Result Storage] Stored composition task result. Type: {Type}, Value: {Value}",
                            result?.GetType()?.Name ?? "null",
                            result?.ToString() ?? "null");
                    }

                    // Increment completed count and report progress
                    Interlocked.Increment(ref batchCompletedCount);
                    progress?.Report(new OrchestrationProgress(
                        $"Completed: {stageName}",
                        baseCompletedTasks + batchCompletedCount,
                        totalTasks,
                        stopwatch.Elapsed));

                    _logger.LogInformation("Task completed successfully: {TaskId} (Duration: {Duration}ms)",
                        node.TaskId, node.CompletedAt.HasValue && node.StartedAt.HasValue
                            ? (node.CompletedAt.Value - node.StartedAt.Value).TotalMilliseconds
                            : 0);
                }
                finally
                {
                    if (limiterAcquired)
                    {
                        _concurrencyLimiter.Release();
                    }
                }
            }
            catch (TimeoutException tex)
            {
                _logger.LogWarning(tex, "Task timed out: {TaskId}", node.TaskId);

                node.Status = TaskStatus.TimedOut;
                node.ErrorMessage = tex.Message;
                node.CompletedAt = DateTime.UtcNow;

                var taskResult = new TaskResult(node.TaskId, false, null, tex.Message);
                _taskResults[node.TaskId] = taskResult;
                results.Add(taskResult);
            }
            catch (OperationCanceledException) when (batchTimeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                // Batch timeout - mark task as timed out
                _logger.LogWarning("Task {TaskId} aborted due to batch timeout", node.TaskId);

                node.Status = TaskStatus.TimedOut;
                node.ErrorMessage = $"Batch timeout exceeded ({batchTimeout.TotalMinutes} minutes)";
                node.CompletedAt = DateTime.UtcNow;

                var taskResult = new TaskResult(node.TaskId, false, null, node.ErrorMessage);
                _taskResults[node.TaskId] = taskResult;
                results.Add(taskResult);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // User/external cancellation - mark as cancelled
                _logger.LogInformation("Task {TaskId} cancelled by user", node.TaskId);

                node.Status = TaskStatus.Cancelled;
                node.ErrorMessage = "Cancelled by user";
                node.CompletedAt = DateTime.UtcNow;

                var taskResult = new TaskResult(node.TaskId, false, null, node.ErrorMessage);
                _taskResults[node.TaskId] = taskResult;
                results.Add(taskResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Task failed: {TaskId}", node.TaskId);

                node.Status = TaskStatus.Failed;
                node.ErrorMessage = ex.Message;
                node.CompletedAt = DateTime.UtcNow;

                var taskResult = new TaskResult(node.TaskId, false, null, ex.Message);
                _taskResults[node.TaskId] = taskResult;
                results.Add(taskResult);
            }
        });

        try
        {
            // Wait for all tasks with batch timeout using WaitAsync for proper timeout handling
            await Task.WhenAll(tasks).WaitAsync(batchTimeout, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Batch execution timed out after {Timeout} minutes, marking remaining tasks as timed out", batchTimeout.TotalMinutes);

            // Mark any tasks that haven't completed yet as timed out
            MarkIncompleteTasksAsTimedOut(batch, results, batchTimeout);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("Batch execution cancelled by user");

            // Mark incomplete tasks as cancelled
            MarkIncompleteTasksAsCancelled(batch, results);
            throw;
        }

        return results.ToList();
    }

    /// <summary>
    /// Marks any tasks that haven't completed as timed out
    /// </summary>
    private void MarkIncompleteTasksAsTimedOut(
        List<GenerationNode> batch,
        ConcurrentBag<TaskResult> results,
        TimeSpan batchTimeout)
    {
        var completedTaskIds = results.Select(r => r.TaskId).ToHashSet();

        foreach (var node in batch)
        {
            if (!completedTaskIds.Contains(node.TaskId))
            {
                _logger.LogWarning("Marking incomplete task {TaskId} as timed out", node.TaskId);

                node.Status = TaskStatus.TimedOut;
                node.ErrorMessage = $"Task did not complete within batch timeout ({batchTimeout.TotalMinutes} minutes)";
                node.CompletedAt = DateTime.UtcNow;

                var taskResult = new TaskResult(node.TaskId, false, null, node.ErrorMessage);
                _taskResults[node.TaskId] = taskResult;
                results.Add(taskResult);
            }
        }
    }

    /// <summary>
    /// Marks any tasks that haven't completed as cancelled
    /// </summary>
    private void MarkIncompleteTasksAsCancelled(List<GenerationNode> batch, ConcurrentBag<TaskResult> results)
    {
        var completedTaskIds = results.Select(r => r.TaskId).ToHashSet();

        foreach (var node in batch)
        {
            if (!completedTaskIds.Contains(node.TaskId))
            {
                _logger.LogInformation("Marking incomplete task {TaskId} as cancelled", node.TaskId);

                node.Status = TaskStatus.Cancelled;
                node.ErrorMessage = "Cancelled before completion";
                node.CompletedAt = DateTime.UtcNow;

                var taskResult = new TaskResult(node.TaskId, false, null, node.ErrorMessage);
                _taskResults[node.TaskId] = taskResult;
                results.Add(taskResult);
            }
        }
    }

    /// <summary>
    /// Gets a human-readable stage name for a task type
    /// Uses centralized StageNames constants for consistency
    /// </summary>
    private static string GetTaskStageName(GenerationTaskType taskType)
    {
        return taskType switch
        {
            GenerationTaskType.ScriptGeneration => StageNames.ScriptGeneration,
            GenerationTaskType.AudioGeneration => StageNames.AudioGeneration,
            GenerationTaskType.ImageGeneration => StageNames.ImageGeneration,
            GenerationTaskType.CaptionGeneration => StageNames.CaptionGeneration,
            GenerationTaskType.VideoComposition => StageNames.VideoComposition,
            _ => taskType.ToString()
        };
    }

    /// <summary>
    /// Checks if any critical tasks have failed.
    /// Visual tasks (image generation) are NOT critical - composition can proceed with placeholders.
    /// </summary>
    private bool HasCriticalFailures(List<TaskResult> results, AssetDependencyGraph graph)
    {
        // Only script, audio, and composition are truly critical - the pipeline cannot proceed without them
        // Visual/image tasks are optional - composition can use placeholders for missing visuals
        var criticalTasks = new[] { ScriptTaskId, AudioTaskId, CompositionTaskId };

        var criticalFailures = results
            .Where(r => !r.Succeeded && criticalTasks.Contains(r.TaskId))
            .ToList();

        if (criticalFailures.Count > 0)
        {
            _logger.LogError("Critical task failures detected: {Tasks}",
                string.Join(", ", criticalFailures.Select(f => $"{f.TaskId}: {f.ErrorMessage}")));
            return true;
        }

        // Log non-critical (visual) failures as warnings
        var visualFailures = results
            .Where(r => !r.Succeeded && r.TaskId.StartsWith(VisualTaskIdPrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (visualFailures.Count > 0)
        {
            _logger.LogWarning("Non-critical visual task failures (will use placeholders): {Tasks}",
                string.Join(", ", visualFailures.Select(f => f.TaskId)));
        }

        return false;
    }

    /// <summary>
    /// Attempts to recover from task failures using fallback strategies.
    /// For visual tasks, provides empty asset list as fallback (composition will use placeholders).
    /// </summary>
    private async Task<bool> AttemptRecoveryAsync(
        List<TaskResult> failedResults,
        AssetDependencyGraph graph,
        PlanSpec planSpec,
        GenerationStrategy strategy,
        Func<GenerationNode, CancellationToken, Task<object>> taskExecutor,
        Action<string, object>? recoveryResultsCallback,
        CancellationToken ct)
    {
        _logger.LogInformation("Attempting recovery from {Count} failed tasks", failedResults.Count(r => !r.Succeeded));

        var failedTasks = failedResults.Where(r => !r.Succeeded).ToList();
        bool anyRecovered = false;
        SilentWavGenerator? silentGenerator = null;

        foreach (var failed in failedTasks)
        {
            var node = graph.GetNode(failed.TaskId);
            if (node == null) continue;

            // For visual/image tasks, provide a fallback (empty asset list) so composition can proceed
            if (node.TaskType == GenerationTaskType.ImageGeneration)
            {
                _logger.LogInformation("Using fallback for visual task {TaskId} - composition will use placeholders", failed.TaskId);

                // Mark as completed with empty result (fallback)
                node.Status = TaskStatus.Completed;
                node.Result = Array.Empty<object>();
                node.ErrorMessage = null;
                node.CompletedAt = DateTime.UtcNow;

                _taskResults[node.TaskId] = new TaskResult(node.TaskId, true, node.Result, null);
                anyRecovered = true;
                continue;
            }

            // For critical tasks, attempt actual retry
            try
            {
                if (node.TaskType == GenerationTaskType.AudioGeneration)
                {
                    silentGenerator ??= new SilentWavGenerator(NullLogger<SilentWavGenerator>.Instance);
                    var recoveryBaseDir = Path.Combine(Path.GetTempPath(), RecoveryRootFolderName, "Recovery");
                    var silentAudioDir = Path.Combine(recoveryBaseDir, "Audio");
                    Directory.CreateDirectory(silentAudioDir);

                    var duration = planSpec.TargetDuration;
                    var silentAudioPath = Path.GetFullPath(Path.Combine(silentAudioDir, $"silent-{Guid.NewGuid()}.wav"));
                    await silentGenerator.GenerateAsync(silentAudioPath, duration, ct: ct).ConfigureAwait(false);

                    node.Status = TaskStatus.Completed;
                    node.Result = silentAudioPath;
                    node.ErrorMessage = null;
                    node.CompletedAt = DateTime.UtcNow;

                    _taskResults[node.TaskId] = new TaskResult(node.TaskId, true, node.Result, null);
                    recoveryResultsCallback?.Invoke("audio", silentAudioPath);

                    _logger.LogWarning("[Recovery] Silent audio generated at: {Path}", silentAudioPath);
                    anyRecovered = true;
                    continue;
                }

                _logger.LogInformation("Retrying critical task: {TaskId}", failed.TaskId);

                // Reset node state
                node.Status = TaskStatus.Pending;
                node.ErrorMessage = null;

                // Retry with reduced resource requirements
                var result = await taskExecutor(node, ct).ConfigureAwait(false);

                node.Status = TaskStatus.Completed;
                node.Result = result;
                node.CompletedAt = DateTime.UtcNow;

                _taskResults[node.TaskId] = new TaskResult(node.TaskId, true, result, null);

                _logger.LogInformation("Recovery successful for task: {TaskId}", failed.TaskId);
                anyRecovered = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Recovery failed for task: {TaskId}", failed.TaskId);
            }
        }

        return anyRecovered;
    }

    /// <summary>
    /// Estimates the number of scenes based on target duration
    /// </summary>
    private int EstimateSceneCount(TimeSpan targetDuration)
    {
        // Rough estimate: 1 scene per 20-30 seconds
        int sceneCount = (int)Math.Ceiling(targetDuration.TotalSeconds / 25);
        return Math.Max(1, Math.Min(sceneCount, 20)); // Cap between 1-20 scenes
    }

    /// <summary>
    /// Updates the concurrency limiter based on strategy
    /// </summary>
    private void UpdateConcurrencyLimit(int maxConcurrency)
    {
        _logger.LogInformation("Setting max concurrency to {Concurrency}", maxConcurrency);
        // Note: SemaphoreSlim doesn't support dynamic resizing, so this is a simplified version
        // In production, we'd need to recreate the semaphore or use a different synchronization mechanism
    }
}

/// <summary>
/// Progress information for orchestration
/// </summary>
public record OrchestrationProgress(
    string CurrentStage,
    int CompletedTasks,
    int TotalTasks,
    TimeSpan ElapsedTime)
{
    public double ProgressPercentage => TotalTasks > 0 ? (double)CompletedTasks / TotalTasks * 100 : 0;
}

/// <summary>
/// Result of an orchestration run
/// </summary>
public record OrchestrationResult(
    bool Succeeded,
    int TotalTasks,
    int CompletedTasks,
    int FailedTasks,
    TimeSpan ExecutionTime,
    GenerationStrategy Strategy,
    IReadOnlyDictionary<string, TaskResult> TaskResults)
{
    public double QualityScore => TotalTasks > 0 ? (double)CompletedTasks / TotalTasks : 0;
    public IReadOnlyList<string> FailureReasons => TaskResults.Values
        .Where(r => !r.Succeeded && r.ErrorMessage != null)
        .Select(r => r.ErrorMessage!)
        .ToList();
}

/// <summary>
/// Result of a single task execution
/// </summary>
public record TaskResult(
    string TaskId,
    bool Succeeded,
    object? Result,
    string? ErrorMessage);

/// <summary>
/// Exception thrown when orchestration fails critically
/// </summary>
public class OrchestrationException : Exception
{
    public OrchestrationException(string message) : base(message)
    {
    }

    public OrchestrationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
