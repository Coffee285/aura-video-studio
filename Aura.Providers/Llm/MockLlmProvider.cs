using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aura.Core.Models;
using Aura.Core.Models.Narrative;
using Aura.Core.Models.Streaming;
using Aura.Core.Models.Visual;
using Aura.Core.Providers;
using Microsoft.Extensions.Logging;

namespace Aura.Providers.Llm;

/// <summary>
/// Mock LLM provider for testing purposes.
/// Provides predictable responses without requiring API keys or network calls.
/// </summary>
public partial class MockLlmProvider : ILlmProvider
{
    private readonly ILogger<MockLlmProvider> _logger;
    private readonly MockBehavior _behavior;
    private readonly List<string> _callHistory = new();
    
    /// <summary>
    /// Number of times each method has been called
    /// </summary>
    public Dictionary<string, int> CallCounts { get; } = new();
    
    /// <summary>
    /// Access to call history for test assertions
    /// </summary>
    public IReadOnlyList<string> CallHistory => _callHistory;

    /// <summary>
    /// Configurable delay to simulate network latency
    /// </summary>
    public TimeSpan SimulatedLatency { get; set; } = TimeSpan.Zero;

    public MockLlmProvider(ILogger<MockLlmProvider> logger, MockBehavior behavior = MockBehavior.Success)
    {
        _logger = logger;
        _behavior = behavior;
    }

    public async Task<string> DraftScriptAsync(Brief brief, PlanSpec spec, CancellationToken ct)
    {
        RecordCall(nameof(DraftScriptAsync));
        _logger.LogInformation("Mock LLM generating script for topic: {Topic}", brief.Topic);

        if (SimulatedLatency > TimeSpan.Zero)
        {
            await Task.Delay(SimulatedLatency, ct).ConfigureAwait(false);
        }

        return _behavior switch
        {
            MockBehavior.Failure => throw new InvalidOperationException("Mock LLM provider configured to fail"),
            MockBehavior.Timeout => throw new TaskCanceledException("Mock LLM provider timed out"),
            MockBehavior.EmptyResponse => string.Empty,
            _ => GenerateMockScript(brief, spec)
        };
    }

    public async Task<string> CompleteAsync(string prompt, CancellationToken ct)
    {
        RecordCall(nameof(CompleteAsync));
        _logger.LogInformation("Mock LLM completing prompt (length: {Length})", prompt.Length);

        if (SimulatedLatency > TimeSpan.Zero)
        {
            await Task.Delay(SimulatedLatency, ct).ConfigureAwait(false);
        }

        return _behavior switch
        {
            MockBehavior.Failure => throw new InvalidOperationException("Mock LLM provider configured to fail"),
            MockBehavior.Timeout => throw new TaskCanceledException("Mock LLM provider timed out"),
            MockBehavior.EmptyResponse => string.Empty,
            _ => GenerateMockCompletion(prompt)
        };
    }

    /// <summary>
    /// Generate a chat completion response for ideation, brainstorming, and other conversational tasks.
    /// </summary>
    public async Task<string> GenerateChatCompletionAsync(
        string systemPrompt,
        string userPrompt,
        LlmParameters? parameters = null,
        CancellationToken ct = default)
    {
        RecordCall(nameof(GenerateChatCompletionAsync));
        _logger.LogInformation("Mock LLM generating chat completion (system: {SystemLength}, user: {UserLength})",
            systemPrompt.Length, userPrompt.Length);

        if (SimulatedLatency > TimeSpan.Zero)
        {
            await Task.Delay(SimulatedLatency, ct).ConfigureAwait(false);
        }

        return _behavior switch
        {
            MockBehavior.Failure => throw new InvalidOperationException("Mock LLM provider configured to fail"),
            MockBehavior.Timeout => throw new TaskCanceledException("Mock LLM provider timed out"),
            MockBehavior.EmptyResponse => string.Empty,
            _ => GenerateMockChatCompletion(systemPrompt, userPrompt)
        };
    }

    private string GenerateMockChatCompletion(string systemPrompt, string userPrompt)
    {
        // Generate mock JSON response for ideation requests
        if (userPrompt.Contains("video concept", StringComparison.OrdinalIgnoreCase) ||
            userPrompt.Contains("brainstorm", StringComparison.OrdinalIgnoreCase) ||
            systemPrompt.Contains("concepts", StringComparison.OrdinalIgnoreCase))
        {
            return @"{
  ""concepts"": [
    {
      ""title"": ""Mock Concept 1"",
      ""description"": ""This is a mock concept for testing purposes."",
      ""angle"": ""Tutorial"",
      ""targetAudience"": ""Developers"",
      ""pros"": [""Easy to test"", ""Predictable""],
      ""cons"": [""Not real""],
      ""appealScore"": 85,
      ""hook"": ""Mock hook for testing"",
      ""talkingPoints"": [""Point 1"", ""Point 2"", ""Point 3""]
    }
  ]
}";
        }

        return GenerateMockCompletion($"{systemPrompt}\n{userPrompt}");
    }

    public async Task<SceneAnalysisResult?> AnalyzeSceneImportanceAsync(
        string sceneText,
        string? previousSceneText,
        string videoGoal,
        CancellationToken ct)
    {
        RecordCall(nameof(AnalyzeSceneImportanceAsync));
        _logger.LogInformation("Mock LLM analyzing scene importance");

        if (SimulatedLatency > TimeSpan.Zero)
        {
            await Task.Delay(SimulatedLatency, ct).ConfigureAwait(false);
        }

        if (_behavior == MockBehavior.Failure)
        {
            throw new InvalidOperationException("Mock LLM provider configured to fail");
        }

        if (_behavior == MockBehavior.Timeout)
        {
            throw new TaskCanceledException("Mock LLM provider timed out");
        }

        if (_behavior == MockBehavior.NullResponse)
        {
            return null;
        }

        return new SceneAnalysisResult(
            Importance: 75.0,
            Complexity: 60.0,
            EmotionalIntensity: 50.0,
            InformationDensity: "medium",
            OptimalDurationSeconds: 10.0,
            TransitionType: "cut",
            Reasoning: "Mock analysis result for testing"
        );
    }

    public async Task<VisualPromptResult?> GenerateVisualPromptAsync(
        string sceneText,
        string? previousSceneText,
        string videoTone,
        VisualStyle targetStyle,
        CancellationToken ct)
    {
        RecordCall(nameof(GenerateVisualPromptAsync));
        _logger.LogInformation("Mock LLM generating visual prompt");

        if (SimulatedLatency > TimeSpan.Zero)
        {
            await Task.Delay(SimulatedLatency, ct).ConfigureAwait(false);
        }

        if (_behavior == MockBehavior.Failure)
        {
            throw new InvalidOperationException("Mock LLM provider configured to fail");
        }

        if (_behavior == MockBehavior.Timeout)
        {
            throw new TaskCanceledException("Mock LLM provider timed out");
        }

        if (_behavior == MockBehavior.NullResponse)
        {
            return null;
        }

        return new VisualPromptResult(
            DetailedDescription: $"Mock visual description for scene about {sceneText.Substring(0, Math.Min(50, sceneText.Length))}",
            CompositionGuidelines: "rule of thirds, leading lines",
            LightingMood: "soft",
            LightingDirection: "front",
            LightingQuality: "diffused",
            TimeOfDay: "day",
            ColorPalette: new[] { "#FFFFFF", "#000000", "#FF0000" },
            ShotType: "medium shot",
            CameraAngle: "eye level",
            DepthOfField: "medium",
            StyleKeywords: new[] { "modern", "clean", "professional" },
            NegativeElements: new[] { "blur", "distortion" },
            ContinuityElements: new[] { "consistent lighting", "same location" },
            Reasoning: "Mock visual prompt for testing"
        );
    }

    public async Task<ContentComplexityAnalysisResult?> AnalyzeContentComplexityAsync(
        string sceneText,
        string? previousSceneText,
        string videoGoal,
        CancellationToken ct)
    {
        RecordCall(nameof(AnalyzeContentComplexityAsync));
        _logger.LogInformation("Mock LLM analyzing content complexity");

        if (SimulatedLatency > TimeSpan.Zero)
        {
            await Task.Delay(SimulatedLatency, ct).ConfigureAwait(false);
        }

        if (_behavior == MockBehavior.Failure)
        {
            throw new InvalidOperationException("Mock LLM provider configured to fail");
        }

        if (_behavior == MockBehavior.Timeout)
        {
            throw new TaskCanceledException("Mock LLM provider timed out");
        }

        if (_behavior == MockBehavior.NullResponse)
        {
            return null;
        }

        return new ContentComplexityAnalysisResult(
            OverallComplexityScore: 65.0,
            ConceptDifficulty: 55.0,
            TerminologyDensity: 45.0,
            PrerequisiteKnowledgeLevel: 50.0,
            MultiStepReasoningRequired: 60.0,
            NewConceptsIntroduced: 3,
            CognitiveProcessingTimeSeconds: 8.0,
            OptimalAttentionWindowSeconds: 12.0,
            DetailedBreakdown: "Mock complexity analysis for testing purposes"
        );
    }

    public async Task<SceneCoherenceResult?> AnalyzeSceneCoherenceAsync(
        string fromSceneText,
        string toSceneText,
        string videoGoal,
        CancellationToken ct)
    {
        RecordCall(nameof(AnalyzeSceneCoherenceAsync));
        _logger.LogInformation("Mock LLM analyzing scene coherence");

        if (SimulatedLatency > TimeSpan.Zero)
        {
            await Task.Delay(SimulatedLatency, ct).ConfigureAwait(false);
        }

        if (_behavior == MockBehavior.Failure)
        {
            throw new InvalidOperationException("Mock LLM provider configured to fail");
        }

        if (_behavior == MockBehavior.Timeout)
        {
            throw new TaskCanceledException("Mock LLM provider timed out");
        }

        if (_behavior == MockBehavior.NullResponse)
        {
            return null;
        }

        return new SceneCoherenceResult(
            CoherenceScore: 80.0,
            ConnectionTypes: new[] { "sequential", "thematic" },
            ConfidenceScore: 0.85,
            Reasoning: "Mock coherence analysis showing good flow between scenes"
        );
    }

    public async Task<NarrativeArcResult?> ValidateNarrativeArcAsync(
        IReadOnlyList<string> sceneTexts,
        string videoGoal,
        string videoType,
        CancellationToken ct)
    {
        RecordCall(nameof(ValidateNarrativeArcAsync));
        _logger.LogInformation("Mock LLM validating narrative arc for {Count} scenes", sceneTexts.Count);

        if (SimulatedLatency > TimeSpan.Zero)
        {
            await Task.Delay(SimulatedLatency, ct).ConfigureAwait(false);
        }

        if (_behavior == MockBehavior.Failure)
        {
            throw new InvalidOperationException("Mock LLM provider configured to fail");
        }

        if (_behavior == MockBehavior.Timeout)
        {
            throw new TaskCanceledException("Mock LLM provider timed out");
        }

        if (_behavior == MockBehavior.NullResponse)
        {
            return null;
        }

        return new NarrativeArcResult(
            IsValid: true,
            DetectedStructure: "introduction → body → conclusion",
            ExpectedStructure: "introduction → body → conclusion",
            StructuralIssues: Array.Empty<string>(),
            Recommendations: new[] { "Consider adding more transitions between scenes" },
            Reasoning: "Mock narrative arc validation with good structure"
        );
    }

    public async Task<string?> GenerateTransitionTextAsync(
        string fromSceneText,
        string toSceneText,
        string videoGoal,
        CancellationToken ct)
    {
        RecordCall(nameof(GenerateTransitionTextAsync));
        _logger.LogInformation("Mock LLM generating transition text");

        if (SimulatedLatency > TimeSpan.Zero)
        {
            await Task.Delay(SimulatedLatency, ct).ConfigureAwait(false);
        }

        if (_behavior == MockBehavior.Failure)
        {
            throw new InvalidOperationException("Mock LLM provider configured to fail");
        }

        if (_behavior == MockBehavior.Timeout)
        {
            throw new TaskCanceledException("Mock LLM provider timed out");
        }

        if (_behavior == MockBehavior.NullResponse || _behavior == MockBehavior.EmptyResponse)
        {
            return null;
        }

        return "This leads us naturally to our next point...";
    }

    private string GenerateMockScript(Brief brief, PlanSpec spec)
    {
        var wordCount = (int)(spec.TargetDuration.TotalMinutes * 150); // ~150 WPM
        var sceneCount = Math.Max(3, (int)(spec.TargetDuration.TotalMinutes * 2)); // ~2 scenes per minute

        var script = $@"# {brief.Topic} - A Test Video

## Hook
This is a mock script generated for testing purposes about {brief.Topic}.

## Introduction  
We're going to explore this topic in depth, covering all the key points you need to know.

";

        for (int i = 1; i <= sceneCount; i++)
        {
            script += $@"## Section {i}: Key Point {i}
This is scene {i} of our mock script. It contains relevant information about {brief.Topic}.
[VISUAL: Mock visual description for scene {i}]

";
        }

        script += @"## Conclusion
That's everything you need to know about this topic. Thank you for watching!";

        return script;
    }

    private string GenerateMockCompletion(string prompt)
    {
        // Return a simple JSON-like response for testing
        return @"{
  ""response"": ""This is a mock completion response for testing purposes."",
  ""confidence"": 0.95,
  ""reasoning"": ""Generated by MockLlmProvider""
}";
    }

    private void RecordCall(string methodName)
    {
        _callHistory.Add($"{methodName} at {DateTime.UtcNow:O}");
        
        if (!CallCounts.TryGetValue(methodName, out var value))
        {
            value = 0;
            CallCounts[methodName] = value;
        }
        CallCounts[methodName] = ++value;
    }

    /// <summary>
    /// Reset call tracking (useful between tests)
    /// </summary>
    public void ResetCallTracking()
    {
        _callHistory.Clear();
        CallCounts.Clear();
    }
}

/// <summary>
/// Behavior modes for MockLlmProvider
/// </summary>
public enum MockBehavior
{
    /// <summary>
    /// Return successful mock responses
    /// </summary>
    Success,

    /// <summary>
    /// Throw exceptions to simulate failures
    /// </summary>
    Failure,

    /// <summary>
    /// Simulate timeout/cancellation
    /// </summary>
    Timeout,

    /// <summary>
    /// Return null responses
    /// </summary>
    NullResponse,

    /// <summary>
    /// Return empty string responses
    /// </summary>
    EmptyResponse
}

public partial class MockLlmProvider
{
    /// <summary>
    /// Whether this provider supports streaming (mock can simulate streaming)
    /// </summary>
    public bool SupportsStreaming => true;

    /// <summary>
    /// Get provider characteristics for adaptive UI
    /// </summary>
    public LlmProviderCharacteristics GetCharacteristics()
    {
        return new LlmProviderCharacteristics
        {
            IsLocal = true,
            ExpectedFirstTokenMs = 0,
            ExpectedTokensPerSec = 100,
            SupportsStreaming = true,
            ProviderTier = "Test",
            CostPer1KTokens = null
        };
    }

    /// <summary>
    /// Mock streaming implementation for testing
    /// </summary>
    public async IAsyncEnumerable<LlmStreamChunk> DraftScriptStreamAsync(
        Brief brief, 
        PlanSpec spec, 
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        RecordCall(nameof(DraftScriptStreamAsync));
        _logger.LogInformation("Mock LLM streaming script generation for topic: {Topic}", brief.Topic);

        if (_behavior == MockBehavior.Failure)
        {
            yield return new LlmStreamChunk
            {
                ProviderName = "Mock",
                Content = string.Empty,
                TokenIndex = 0,
                IsFinal = true,
                ErrorMessage = "Mock LLM provider configured to fail"
            };
            yield break;
        }

        if (_behavior == MockBehavior.Timeout)
        {
            await Task.Delay(TimeSpan.FromMinutes(10), ct).ConfigureAwait(false);
        }

        var result = GenerateMockScript(brief, spec);
        var words = result.Split(' ');
        var accumulated = new StringBuilder();

        for (int i = 0; i < words.Length; i++)
        {
            if (SimulatedLatency > TimeSpan.Zero)
            {
                await Task.Delay(SimulatedLatency, ct).ConfigureAwait(false);
            }

            var word = words[i] + (i < words.Length - 1 ? " " : "");
            accumulated.Append(word);

            yield return new LlmStreamChunk
            {
                ProviderName = "Mock",
                Content = word,
                AccumulatedContent = accumulated.ToString(),
                TokenIndex = i + 1,
                IsFinal = false
            };
        }

        yield return new LlmStreamChunk
        {
            ProviderName = "Mock",
            Content = string.Empty,
            AccumulatedContent = accumulated.ToString(),
            TokenIndex = words.Length,
            IsFinal = true,
            Metadata = new LlmStreamMetadata
            {
                TotalTokens = words.Length,
                EstimatedCost = null,
                TokensPerSecond = SimulatedLatency > TimeSpan.Zero ? 1.0 / SimulatedLatency.TotalSeconds : 100.0,
                IsLocalModel = true,
                ModelName = "mock",
                FinishReason = "stop"
            }
        };
    }
}
