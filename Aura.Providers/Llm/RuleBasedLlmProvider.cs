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
using Aura.Core.Services.AI;
using Aura.Core.Templates;
using Microsoft.Extensions.Logging;

namespace Aura.Providers.Llm;

/// <summary>
/// Rule-based LLM provider for offline script generation.
/// Supports prompt customization for additional instructions.
/// </summary>
public class RuleBasedLlmProvider : ILlmProvider
{
    private readonly ILogger<RuleBasedLlmProvider> _logger;
    private readonly Random _random;
    private readonly PromptCustomizationService _promptCustomizationService;

    public RuleBasedLlmProvider(
        ILogger<RuleBasedLlmProvider> logger,
        PromptCustomizationService? promptCustomizationService = null)
    {
        _logger = logger;
        _random = new Random(42); // Fixed seed for deterministic output
        
        // Create PromptCustomizationService if not provided (using logger factory pattern)
        if (promptCustomizationService == null)
        {
            var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
            var customizationLogger = loggerFactory.CreateLogger<PromptCustomizationService>();
            _promptCustomizationService = new PromptCustomizationService(customizationLogger);
        }
        else
        {
            _promptCustomizationService = promptCustomizationService;
        }
    }

    public Task<string> DraftScriptAsync(Brief brief, PlanSpec spec, CancellationToken ct)
    {
        _logger.LogInformation("Generating rule-based script for topic: {Topic}", brief.Topic);
        
        // Log if user has provided custom instructions
        if (brief.PromptModifiers?.AdditionalInstructions != null)
        {
            _logger.LogInformation("User provided additional instructions for script generation");
        }
        
        // Calculate the target word count based on duration, pacing, and density
        int targetWordCount = CalculateWordCount(spec.TargetDuration, spec.Pacing, spec.Density);
        
        // Determine the number of scenes based on the total duration
        int sceneCount = DetermineSceneCount(spec.TargetDuration);
        
        // Approximate words per scene
        int wordsPerScene = targetWordCount / sceneCount;
        
        _logger.LogDebug("Target word count: {WordCount}, Scenes: {SceneCount}, Words per scene: {WordsPerScene}",
            targetWordCount, sceneCount, wordsPerScene);

        // Generate the script (RuleBased provider uses templates, prompt modifiers logged but not directly applied)
        string script = GenerateScript(brief, spec, sceneCount, wordsPerScene);
        
        return Task.FromResult(script);
    }

    private int CalculateWordCount(TimeSpan duration, Pacing pacing, Density density)
    {
        // Base words per minute based on pacing
        int wpm = pacing switch
        {
            Pacing.Chill => 130,
            Pacing.Conversational => 160,
            Pacing.Fast => 190,
            _ => 160
        };

        // Apply density factor
        double densityFactor = density switch
        {
            Density.Sparse => 0.8,
            Density.Balanced => 1.0,
            Density.Dense => 1.2,
            _ => 1.0
        };

        // Calculate target word count
        return (int)(duration.TotalMinutes * wpm * densityFactor);
    }

    private int DetermineSceneCount(TimeSpan duration)
    {
        // Approximate 1 scene per 30 seconds, with some bounds
        int baseCount = (int)Math.Ceiling(duration.TotalSeconds / 30);
        
        // Ensure a minimum of 3 scenes and maximum of 20 scenes
        return Math.Clamp(baseCount, 3, 20);
    }

    private string GenerateScript(Brief brief, PlanSpec spec, int sceneCount, int wordsPerScene)
    {
        // Determine video type from topic keywords
        var videoType = ScriptTemplates.DetermineVideoType(brief.Topic);
        
        _logger.LogInformation("Detected video type: {VideoType} for topic: {Topic}", 
            videoType, brief.Topic);
        
        // Calculate target word count
        int targetWordCount = sceneCount * wordsPerScene;
        
        // Use template-based generation for professional content
        string templateScript = ScriptTemplates.GenerateFromTemplate(videoType, brief.Topic, targetWordCount);
        
        // If template is too short, supplement with additional content
        if (CountWords(templateScript) < targetWordCount)
        {
            templateScript = ExpandScriptToTargetLength(templateScript, brief, targetWordCount);
        }
        
        return templateScript;
    }
    
    private string ExpandScriptToTargetLength(string script, Brief brief, int targetWordCount)
    {
        var currentWords = CountWords(script);
        
        if (currentWords >= targetWordCount)
        {
            return script;
        }
        
        var sections = new List<string> { script };
        
        sections.Add("");
        sections.Add("## Additional Insights");
        sections.Add($"Let's explore some additional aspects of {brief.Topic} that enhance our understanding. These supplementary points provide extra context and depth to what we've already covered.");
        sections.Add("");
        sections.Add("The broader implications are worth considering. When we look at the bigger picture, we can see how this topic connects to related areas and affects various aspects of our work or lives.");
        
        return string.Join("\n", sections);
    }

    private int CountWords(string text)
    {
        return text.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    public Task<string> CompleteAsync(string prompt, CancellationToken ct)
    {
        _logger.LogWarning("RuleBasedLlmProvider.CompleteAsync: Raw prompt completion not supported for rule-based provider. " +
            "This provider only supports structured script generation via DraftScriptAsync. " +
            "For prompt completion, use an AI provider like Ollama, OpenAI, or Gemini.");
        
        // For rule-based provider, we can't meaningfully process arbitrary prompts
        // Throw an exception to signal that this provider cannot handle this operation
        // This will trigger the fallback chain in CompositeLlmProvider
        throw new NotSupportedException(
            "RuleBased provider does not support raw prompt completion. " +
            "Please ensure Ollama or another AI provider is running and configured.");
    }

    /// <summary>
    /// Generate a chat completion response for ideation, brainstorming, and other conversational tasks.
    /// RuleBased provider does not support this - throws NotSupportedException.
    /// </summary>
    public Task<string> GenerateChatCompletionAsync(
        string systemPrompt,
        string userPrompt,
        LlmParameters? parameters = null,
        CancellationToken ct = default)
    {
        _logger.LogWarning("RuleBasedLlmProvider.GenerateChatCompletionAsync: Chat completion not supported for rule-based provider. " +
            "For ideation and brainstorming, use an AI provider like Ollama, OpenAI, or Gemini.");
        
        throw new NotSupportedException(
            "RuleBased provider does not support chat completion. " +
            "Please ensure Ollama or another AI provider is running and configured for ideation features.");
    }

    public Task<SceneAnalysisResult?> AnalyzeSceneImportanceAsync(
        string sceneText,
        string? previousSceneText,
        string videoGoal,
        CancellationToken ct)
    {
        _logger.LogInformation("Analyzing scene with rule-based heuristics");
        
        // Implement deterministic analysis based on heuristics
        var wordCount = sceneText.Split(new[] { ' ', '\t', '\n', '\r' }, 
            StringSplitOptions.RemoveEmptyEntries).Length;

        // Heuristic complexity based on word count
        double complexity = wordCount switch
        {
            < 30 => 30.0,
            < 70 => 50.0,
            < 120 => 70.0,
            _ => 85.0
        };

        // Heuristic importance - default moderate importance
        // Can be enhanced with keyword detection
        double importance = 50.0;
        var importantKeywords = new[] { "introduction", "conclusion", "summary", "key", "important", "critical" };
        var importantCount = importantKeywords.Count(keyword => 
            sceneText.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        importance += Math.Min(importantCount * 10, 30);

        // Heuristic emotional intensity
        double emotionalIntensity = 50.0;
        var emotionalWords = new[] { "amazing", "incredible", "important", "critical", "exciting", 
                                      "fantastic", "wonderful", "terrible", "devastating", "shocking" };
        var emotionalCount = emotionalWords.Count(word => 
            sceneText.Contains(word, StringComparison.OrdinalIgnoreCase));
        emotionalIntensity += Math.Min(emotionalCount * 10, 30);

        // Information density based on word count
        string informationDensity = wordCount switch
        {
            < 50 => "low",
            < 100 => "medium",
            _ => "high"
        };

        // Optimal duration: ~2.5 words per second (conversational pace)
        double optimalDuration = Math.Max(5.0, wordCount / 2.5);

        // Default transition type based on content
        string transitionType = "cut";
        if (sceneText.Contains("meanwhile", StringComparison.OrdinalIgnoreCase) ||
            sceneText.Contains("later", StringComparison.OrdinalIgnoreCase))
        {
            transitionType = "fade";
        }
        else if (sceneText.Contains("gradually", StringComparison.OrdinalIgnoreCase) ||
                 sceneText.Contains("slowly", StringComparison.OrdinalIgnoreCase))
        {
            transitionType = "dissolve";
        }

        var result = new SceneAnalysisResult(
            Importance: Math.Clamp(importance, 0, 100),
            Complexity: Math.Clamp(complexity, 0, 100),
            EmotionalIntensity: Math.Clamp(emotionalIntensity, 0, 100),
            InformationDensity: informationDensity,
            OptimalDurationSeconds: optimalDuration,
            TransitionType: transitionType,
            Reasoning: "Rule-based heuristic analysis using word count, keyword detection, and content patterns"
        );

        _logger.LogDebug("Scene analyzed: Importance={Importance}, Complexity={Complexity}, Duration={Duration}s",
            result.Importance, result.Complexity, result.OptimalDurationSeconds);

        return Task.FromResult<SceneAnalysisResult?>(result);
    }

    public Task<VisualPromptResult?> GenerateVisualPromptAsync(
        string sceneText,
        string? previousSceneText,
        string videoTone,
        VisualStyle targetStyle,
        CancellationToken ct)
    {
        _logger.LogInformation("Generating visual prompt with rule-based heuristics");

        var wordCount = sceneText.Split(new[] { ' ', '\t', '\n', '\r' }, 
            StringSplitOptions.RemoveEmptyEntries).Length;

        var detailedDescription = $"A {targetStyle.ToString().ToLowerInvariant()} visual representation of: {sceneText}";
        
        var compositionGuidelines = wordCount > 50 
            ? "Rule of thirds, balanced framing, clear focal point" 
            : "Centered composition, minimal elements, clean framing";

        var lightingMood = videoTone.ToLowerInvariant() switch
        {
            var t when t.Contains("dramatic") => "dramatic",
            var t when t.Contains("professional") => "neutral",
            var t when t.Contains("warm") => "warm",
            var t when t.Contains("playful") => "bright",
            _ => "balanced"
        };

        var timeOfDay = lightingMood switch
        {
            "dramatic" => "golden hour",
            "warm" => "golden hour",
            "bright" => "day",
            _ => "day"
        };

        var colorPalette = lightingMood switch
        {
            "dramatic" => new[] { "#1a1a1a", "#8b0000", "#ffd700" },
            "professional" => new[] { "#2c3e50", "#ecf0f1", "#3498db" },
            "warm" => new[] { "#ffa500", "#ff8c00", "#ffd700" },
            "bright" => new[] { "#ff6b6b", "#4ecdc4", "#ffe66d" },
            _ => new[] { "#34495e", "#ecf0f1", "#3498db" }
        };

        var shotType = wordCount switch
        {
            < 30 => "wide shot",
            < 70 => "medium shot",
            _ => "medium close-up"
        };

        var cameraAngle = lightingMood switch
        {
            "dramatic" => "low angle",
            _ => "eye level"
        };

        var styleKeywords = targetStyle switch
        {
            VisualStyle.Cinematic => new[] { "cinematic", "film grain", "color graded", "atmospheric", "high quality" },
            VisualStyle.Realistic => new[] { "photorealistic", "natural lighting", "authentic", "detailed", "professional" },
            VisualStyle.Dramatic => new[] { "dramatic lighting", "high contrast", "moody", "intense", "powerful" },
            VisualStyle.Documentary => new[] { "documentary style", "natural", "authentic", "realistic", "informative" },
            _ => new[] { "high quality", "professional", "detailed", "clear", "engaging" }
        };

        var negativeElements = new[] 
        { 
            "blurry", "low quality", "distorted", "watermark", "text", "logo" 
        };

        var continuityElements = !string.IsNullOrEmpty(previousSceneText)
            ? new[] { "consistent color grading", "same location style", "matching tone" }
            : Array.Empty<string>();

        var result = new VisualPromptResult(
            DetailedDescription: detailedDescription,
            CompositionGuidelines: compositionGuidelines,
            LightingMood: lightingMood,
            LightingDirection: "front",
            LightingQuality: "soft",
            TimeOfDay: timeOfDay,
            ColorPalette: colorPalette,
            ShotType: shotType,
            CameraAngle: cameraAngle,
            DepthOfField: "medium",
            StyleKeywords: styleKeywords,
            NegativeElements: negativeElements,
            ContinuityElements: continuityElements,
            Reasoning: "Rule-based visual prompt generation using heuristics for tone, word count, and style"
        );

        _logger.LogDebug("Visual prompt generated: ShotType={ShotType}, Lighting={Lighting}",
            result.ShotType, result.LightingMood);

        return Task.FromResult<VisualPromptResult?>(result);
    }

    public Task<ContentComplexityAnalysisResult?> AnalyzeContentComplexityAsync(
        string sceneText,
        string? previousSceneText,
        string videoGoal,
        CancellationToken ct)
    {
        _logger.LogInformation("Analyzing content complexity with rule-based heuristics");

        var wordCount = sceneText.Split(new[] { ' ', '\t', '\n', '\r' }, 
            StringSplitOptions.RemoveEmptyEntries).Length;

        var technicalTerms = new[] { 
            "algorithm", "implementation", "architecture", "framework", "methodology", 
            "optimization", "integration", "configuration", "deployment", "infrastructure",
            "paradigm", "specification", "protocol", "authentication", "encryption"
        };
        var technicalCount = technicalTerms.Count(term => 
            sceneText.Contains(term, StringComparison.OrdinalIgnoreCase));

        var complexConcepts = new[] {
            "quantum", "molecular", "theoretical", "hypothetical", "abstract",
            "mathematical", "philosophical", "metaphysical", "paradox", "anomaly"
        };
        var complexConceptCount = complexConcepts.Count(term => 
            sceneText.Contains(term, StringComparison.OrdinalIgnoreCase));

        var conceptDifficulty = wordCount > 100 ? 60.0 : 40.0;
        conceptDifficulty += Math.Min(technicalCount * 10, 30);
        conceptDifficulty += Math.Min(complexConceptCount * 15, 20);

        var terminologyDensity = technicalCount > 3 ? 70.0 : 40.0;
        if (wordCount > 0)
        {
            var densityRatio = (double)technicalCount / wordCount * 100;
            terminologyDensity = Math.Min(100, 30 + densityRatio * 50);
        }

        var prerequisiteKnowledge = technicalCount > 2 ? 60.0 : 30.0;
        prerequisiteKnowledge += Math.Min(complexConceptCount * 10, 25);

        var multiStepReasoning = wordCount > 80 ? 50.0 : 30.0;
        var logicalConnectors = new[] { "therefore", "consequently", "however", "moreover", 
                                        "furthermore", "nevertheless", "thus", "hence" };
        var logicalConnectorCount = logicalConnectors.Count(conn => 
            sceneText.Contains(conn, StringComparison.OrdinalIgnoreCase));
        multiStepReasoning += Math.Min(logicalConnectorCount * 8, 30);

        var overallScore = (conceptDifficulty + terminologyDensity + 
            prerequisiteKnowledge + multiStepReasoning) / 4.0;

        var newConceptsIntroduced = Math.Max(1, technicalCount + complexConceptCount + (wordCount / 50));

        var cognitiveProcessingTime = wordCount / 2.0;
        cognitiveProcessingTime *= (1.0 + (overallScore / 200.0)); // More complex = more time

        var optimalAttentionWindow = Math.Min(15, Math.Max(5, wordCount / 15.0));
        if (overallScore > 70)
            optimalAttentionWindow = Math.Min(15, optimalAttentionWindow * 1.3);

        var result = new ContentComplexityAnalysisResult(
            OverallComplexityScore: Math.Clamp(overallScore, 0, 100),
            ConceptDifficulty: Math.Clamp(conceptDifficulty, 0, 100),
            TerminologyDensity: Math.Clamp(terminologyDensity, 0, 100),
            PrerequisiteKnowledgeLevel: Math.Clamp(prerequisiteKnowledge, 0, 100),
            MultiStepReasoningRequired: Math.Clamp(multiStepReasoning, 0, 100),
            NewConceptsIntroduced: newConceptsIntroduced,
            CognitiveProcessingTimeSeconds: cognitiveProcessingTime,
            OptimalAttentionWindowSeconds: optimalAttentionWindow,
            DetailedBreakdown: $"Rule-based complexity analysis: {technicalCount} technical terms, " +
                $"{complexConceptCount} complex concepts, {logicalConnectorCount} logical connectors. " +
                $"Word count: {wordCount}. Estimated as {(overallScore > 70 ? "high" : overallScore > 40 ? "medium" : "low")} complexity."
        );

        _logger.LogDebug("Content complexity analyzed: Overall={Overall:F0}, ConceptDifficulty={Concept:F0}, " +
            "TerminologyDensity={Terminology:F0}, NewConcepts={NewConcepts}",
            result.OverallComplexityScore, result.ConceptDifficulty, 
            result.TerminologyDensity, result.NewConceptsIntroduced);

        return Task.FromResult<ContentComplexityAnalysisResult?>(result);
    }

    public Task<SceneCoherenceResult?> AnalyzeSceneCoherenceAsync(
        string fromSceneText,
        string toSceneText,
        string videoGoal,
        CancellationToken ct)
    {
        _logger.LogDebug("Analyzing scene coherence with rule-based approach");

        var fromWords = GetSignificantWords(fromSceneText);
        var toWords = GetSignificantWords(toSceneText);
        var commonWords = fromWords.Intersect(toWords, StringComparer.OrdinalIgnoreCase).ToList();
        var overlapRatio = commonWords.Count / (double)Math.Max(fromWords.Count, 1);
        
        var coherenceScore = Math.Clamp(overlapRatio * 100, 0, 100);
        
        var connectionTypes = new List<string> { ConnectionType.Sequential };
        
        var transitionWords = new[] { "however", "but", "although", "despite" };
        if (transitionWords.Any(w => toSceneText.Contains(w, StringComparison.OrdinalIgnoreCase)))
        {
            connectionTypes.Add(ConnectionType.Contrast);
        }
        
        var callbackWords = new[] { "remember", "earlier", "as mentioned", "like we said" };
        if (callbackWords.Any(w => toSceneText.Contains(w, StringComparison.OrdinalIgnoreCase)))
        {
            connectionTypes.Add(ConnectionType.Callback);
        }
        
        if (overlapRatio > 0.3)
        {
            connectionTypes.Add(ConnectionType.Thematic);
        }

        var result = new SceneCoherenceResult(
            CoherenceScore: coherenceScore,
            ConnectionTypes: connectionTypes.ToArray(),
            ConfidenceScore: 0.6,
            Reasoning: $"Rule-based analysis: {commonWords.Count} shared significant words out of {fromWords.Count} total"
        );

        return Task.FromResult<SceneCoherenceResult?>(result);
    }

    public Task<NarrativeArcResult?> ValidateNarrativeArcAsync(
        IReadOnlyList<string> sceneTexts,
        string videoGoal,
        string videoType,
        CancellationToken ct)
    {
        _logger.LogDebug("Validating narrative arc with rule-based approach for {VideoType}", videoType);

        var expectedStructures = new Dictionary<string, string>
        {
            { "educational", "problem → explanation → solution" },
            { "entertainment", "setup → conflict → resolution" },
            { "documentary", "introduction → evidence → conclusion" },
            { "tutorial", "overview → steps → summary" },
            { "general", "introduction → body → conclusion" }
        };

        var expectedStructure = expectedStructures.GetValueOrDefault(
            videoType.ToLowerInvariant(), 
            expectedStructures["general"]);

        var result = new NarrativeArcResult(
            IsValid: true,
            DetectedStructure: "Rule-based: sequential structure detected",
            ExpectedStructure: expectedStructure,
            StructuralIssues: Array.Empty<string>(),
            Recommendations: new[] { "LLM-based analysis recommended for detailed arc validation" },
            Reasoning: "Rule-based provider provides basic validation only"
        );

        return Task.FromResult<NarrativeArcResult?>(result);
    }

    public Task<string?> GenerateTransitionTextAsync(
        string fromSceneText,
        string toSceneText,
        string videoGoal,
        CancellationToken ct)
    {
        _logger.LogDebug("Generating transition text with rule-based approach");

        var transitions = new[]
        {
            "Now, let's move on to the next point.",
            "Building on that idea...",
            "This leads us to...",
            "Next, we'll explore...",
            "Following from this..."
        };

        var randomIndex = _random.Next(transitions.Length);
        var transitionText = transitions[randomIndex];

        return Task.FromResult<string?>(transitionText);
    }

    private List<string> GetSignificantWords(string text)
    {
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "a", "an", "the", "and", "or", "but", "in", "on", "at", "to", "for",
            "of", "with", "by", "from", "is", "are", "was", "were", "be", "been",
            "this", "that", "these", "those", "we", "you", "they", "it"
        };

        return text
            .Split(new[] { ' ', '\t', '\n', '\r', '.', ',', '!', '?', ';', ':' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3 && !stopWords.Contains(w))
            .ToList();
    }

    /// <summary>
    /// Generate a complete Script object with scenes from a brief string and duration.
    /// This is the simplified offline fallback API that works without any external dependencies.
    /// Execution time is guaranteed to be under 1 second.
    /// Never throws exceptions - always returns a valid script.
    /// </summary>
    /// <param name="brief">Creative brief describing the video content</param>
    /// <param name="durationSeconds">Target duration in seconds</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Complete Script object with all scenes populated</returns>
    public Task<Core.Models.Generation.Script> GenerateScriptAsync(string brief, int durationSeconds, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("RuleBased provider generating script for {Duration}s video", durationSeconds);
            
            var startTime = DateTime.UtcNow;
            
            var keywords = ExtractKeywords(brief);
            var videoType = DetectVideoType(keywords, brief);
            
            var sceneCount = Math.Max(3, Math.Min(20, durationSeconds / 10));
            _logger.LogInformation("RuleBased provider generated {SceneCount} scenes for {Duration}s video", sceneCount, durationSeconds);
            
            var scenes = new List<Core.Models.Generation.ScriptScene>();
            var sceneDurations = CalculateSceneDurations(sceneCount, durationSeconds);
            
            for (int i = 0; i < sceneCount; i++)
            {
                var sceneNumber = i + 1;
                var isFirst = i == 0;
                var isLast = i == sceneCount - 1;
                
                var narration = GenerateSceneNarration(videoType, sceneNumber, sceneCount, isFirst, isLast, keywords, brief);
                var visualPrompt = GenerateVisualPrompt(narration, keywords);
                var sceneDuration = sceneDurations[i];
                var transition = DetermineTransition(isLast);
                
                var scene = new Core.Models.Generation.ScriptScene
                {
                    Number = sceneNumber,
                    Narration = narration,
                    VisualPrompt = visualPrompt,
                    Duration = TimeSpan.FromSeconds(sceneDuration),
                    Transition = transition
                };
                
                scenes.Add(scene);
            }
            
            var totalDuration = TimeSpan.FromSeconds(durationSeconds);
            var mainTopic = keywords.FirstOrDefault() ?? "content";
            
            var executionTime = DateTime.UtcNow - startTime;
            
            var script = new Core.Models.Generation.Script
            {
                Title = $"{mainTopic} - AI Generated Video",
                Scenes = scenes,
                TotalDuration = totalDuration,
                Metadata = new Core.Models.Generation.ScriptMetadata
                {
                    GeneratedAt = DateTime.UtcNow,
                    ProviderName = "RuleBased",
                    ModelUsed = "Template-Based",
                    TokensUsed = 0,
                    EstimatedCost = 0m,
                    Tier = Core.Models.Generation.ProviderTier.Free,
                    GenerationTime = executionTime
                },
                CorrelationId = Guid.NewGuid().ToString()
            };
            
            _logger.LogInformation("RuleBased script generation completed in {Ms}ms", executionTime.TotalMilliseconds);
            
            return Task.FromResult(script);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error in RuleBased script generation, returning fallback");
            
            var fallbackScene = new Core.Models.Generation.ScriptScene
            {
                Number = 1,
                Narration = "Welcome. This is a brief video about the requested topic.",
                VisualPrompt = "abstract gradient background, blue and purple colors",
                Duration = TimeSpan.FromSeconds(durationSeconds),
                Transition = Core.Models.Generation.TransitionType.Cut
            };
            
            return Task.FromResult(new Core.Models.Generation.Script
            {
                Title = "Generated Video",
                Scenes = new List<Core.Models.Generation.ScriptScene> { fallbackScene },
                TotalDuration = TimeSpan.FromSeconds(durationSeconds),
                Metadata = new Core.Models.Generation.ScriptMetadata
                {
                    GeneratedAt = DateTime.UtcNow,
                    ProviderName = "RuleBased",
                    ModelUsed = "Template-Based-Fallback",
                    TokensUsed = 0,
                    EstimatedCost = 0m,
                    Tier = Core.Models.Generation.ProviderTier.Free,
                    GenerationTime = TimeSpan.FromMilliseconds(1)
                },
                CorrelationId = Guid.NewGuid().ToString()
            });
        }
    }

    /// <summary>
    /// Extract top 5 keywords from brief using frequency analysis.
    /// Filters out stop words and keeps words with length >= 4.
    /// </summary>
    private List<string> ExtractKeywords(string brief)
    {
        if (string.IsNullOrWhiteSpace(brief) || brief.Length < 10)
        {
            return new List<string> { "video", "content", "information" };
        }
        
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "is", "are", "was", "were", "in", "on", "at", "to", "for", 
            "of", "and", "or", "but", "this", "that", "with", "from", "by", "about"
        };
        
        var words = brief.Split(new[] { ' ', '\t', '\n', '\r', '.', ',', '!', '?', ';', ':', '-', '_' }, 
            StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.ToLowerInvariant())
            .Where(w => w.Length >= 4 && !stopWords.Contains(w))
            .ToList();
        
        var wordFrequency = words
            .GroupBy(w => w)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => g.Key)
            .ToList();
        
        if (wordFrequency.Count == 0)
        {
            return new List<string> { "video", "content", "information" };
        }
        
        return wordFrequency;
    }

    /// <summary>
    /// Detect video type from keywords and brief content.
    /// Returns Tutorial, Marketing, Review, or General based on keyword analysis.
    /// </summary>
    private string DetectVideoType(List<string> keywords, string brief)
    {
        var briefLower = brief.ToLowerInvariant();
        var keywordsStr = string.Join(" ", keywords).ToLowerInvariant();
        var combinedText = $"{briefLower} {keywordsStr}";
        
        var tutorialKeywords = new[] { "tutorial", "how", "learn", "guide", "teach", "lesson", "course", "training" };
        var marketingKeywords = new[] { "product", "buy", "sale", "offer", "discount", "deal", "purchase", "customer" };
        var reviewKeywords = new[] { "review", "opinion", "thoughts", "rating", "recommend", "experience", "pros", "cons" };
        
        var tutorialCount = tutorialKeywords.Count(kw => combinedText.Contains(kw));
        var marketingCount = marketingKeywords.Count(kw => combinedText.Contains(kw));
        var reviewCount = reviewKeywords.Count(kw => combinedText.Contains(kw));
        
        string detectedType;
        if (tutorialCount > marketingCount && tutorialCount > reviewCount)
        {
            detectedType = "Tutorial";
        }
        else if (marketingCount > reviewCount)
        {
            detectedType = "Marketing";
        }
        else if (reviewCount > 0)
        {
            detectedType = "Review";
        }
        else
        {
            detectedType = "General";
        }
        
        _logger.LogInformation("Detected video type: {Type}", detectedType);
        return detectedType;
    }

    /// <summary>
    /// Generate narration for a specific scene using templates based on video type.
    /// </summary>
    private string GenerateSceneNarration(string videoType, int sceneNumber, int totalScenes, 
        bool isFirst, bool isLast, List<string> keywords, string originalBrief)
    {
        var mainTopic = keywords.FirstOrDefault() ?? "topic";
        var keyword1 = keywords.ElementAtOrDefault(1) ?? "concepts";
        var keyword2 = keywords.ElementAtOrDefault(2) ?? "ideas";
        var keyword = keywords.ElementAtOrDefault(sceneNumber % keywords.Count) ?? "aspect";
        
        if (isFirst)
        {
            return videoType switch
            {
                "Tutorial" => $"Welcome! Today we'll learn about {mainTopic}. We'll cover {keyword1}, {keyword2}, and more.",
                "Marketing" => $"Looking for the best {mainTopic}? You're in the right place!",
                "Review" => $"Today I'm reviewing {mainTopic}. Let's see if it lives up to the hype.",
                _ => $"Welcome. Today we're discussing {mainTopic}."
            };
        }
        
        if (isLast)
        {
            return videoType switch
            {
                "Tutorial" => $"That's everything about {mainTopic}. Thanks for watching, and happy learning!",
                "Marketing" => $"Ready to get started with {mainTopic}? Click the link below!",
                "Review" => $"Overall, {mainTopic} is worth considering. My rating: recommended.",
                _ => $"That concludes our look at {mainTopic}. Thank you for watching."
            };
        }
        
        var relatedConcept = keywords.ElementAtOrDefault((sceneNumber + 1) % keywords.Count) ?? "related concepts";
        var benefit = keywords.ElementAtOrDefault((sceneNumber + 2) % keywords.Count) ?? "advantages";
        var reason = $"it provides {benefit}";
        
        return videoType switch
        {
            "Tutorial" => $"Let's explore {keyword}. This is important because it helps you understand {relatedConcept}.",
            "Marketing" => $"Here's why {keyword} matters. Our solution offers {benefit}.",
            "Review" => $"The {keyword} feature is impressive because {reason}.",
            _ => $"An important aspect is {keyword}. This relates to {relatedConcept}."
        };
    }

    /// <summary>
    /// Generate visual prompt for scene based on narration and keywords.
    /// Keeps prompts under 100 characters with professional style descriptors.
    /// </summary>
    private string GenerateVisualPrompt(string narration, List<string> keywords)
    {
        if (string.IsNullOrWhiteSpace(narration) && keywords.Count == 0)
        {
            return "abstract gradient background, blue and purple colors";
        }
        
        var narrationWords = narration.Split(new[] { ' ', '\t', '\n', '\r', '.', ',', '!', '?', ';', ':' }, 
            StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 4)
            .Take(5)
            .ToList();
        
        var mainNoun = narrationWords.FirstOrDefault() ?? keywords.FirstOrDefault() ?? "scene";
        
        var styleDescriptor = _random.Next(2) == 0 ? "professional photograph of" : "modern illustration of";
        var subject = mainNoun.ToLowerInvariant();
        var context = "clean white background, studio lighting, high quality";
        
        var prompt = $"{styleDescriptor} {subject}, {context}";
        
        if (prompt.Length > 100)
        {
            prompt = $"{styleDescriptor} {subject}";
        }
        
        return prompt;
    }

    /// <summary>
    /// Calculate duration for each scene with proper intro/outro weighting.
    /// Intro gets 15%, outro gets 15%, middle scenes split remaining 70%.
    /// Includes 1 second buffer for transitions.
    /// </summary>
    private List<double> CalculateSceneDurations(int sceneCount, int totalDurationSeconds)
    {
        var durations = new List<double>();
        
        if (sceneCount == 1)
        {
            durations.Add(totalDurationSeconds);
            return durations;
        }
        
        if (sceneCount == 2)
        {
            durations.Add(totalDurationSeconds * 0.5);
            durations.Add(totalDurationSeconds * 0.5);
            return durations;
        }
        
        var introDuration = totalDurationSeconds * 0.15;
        var outroDuration = totalDurationSeconds * 0.15;
        var middleTotalDuration = totalDurationSeconds * 0.70;
        var middleSceneCount = sceneCount - 2;
        var middleSceneDuration = middleTotalDuration / middleSceneCount;
        
        durations.Add(introDuration);
        
        for (int i = 0; i < middleSceneCount; i++)
        {
            durations.Add(middleSceneDuration);
        }
        
        durations.Add(outroDuration);
        
        var currentSum = durations.Sum();
        var difference = totalDurationSeconds - currentSum;
        
        if (Math.Abs(difference) > totalDurationSeconds * 0.05)
        {
            var adjustment = difference / sceneCount;
            durations = durations.Select(d => d + adjustment).ToList();
        }
        
        return durations;
    }

    /// <summary>
    /// Determine appropriate transition type for scene.
    /// </summary>
    private Core.Models.Generation.TransitionType DetermineTransition(bool isLast)
    {
        if (isLast)
        {
            return Core.Models.Generation.TransitionType.Fade;
        }
        
        return Core.Models.Generation.TransitionType.Cut;
    }

    /// <summary>
    /// Whether this provider supports streaming (RuleBased does not support streaming)
    /// </summary>
    public bool SupportsStreaming => false;

    /// <summary>
    /// Get provider characteristics for adaptive UI
    /// </summary>
    public LlmProviderCharacteristics GetCharacteristics()
    {
        return new LlmProviderCharacteristics
        {
            IsLocal = true,
            ExpectedFirstTokenMs = 0,
            ExpectedTokensPerSec = 0,
            SupportsStreaming = false,
            ProviderTier = "Free",
            CostPer1KTokens = null
        };
    }

    /// <summary>
    /// RuleBased provider does not support streaming
    /// </summary>
    public async IAsyncEnumerable<LlmStreamChunk> DraftScriptStreamAsync(
        Brief brief, 
        PlanSpec spec, 
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        yield return new LlmStreamChunk
        {
            ProviderName = "RuleBased",
            Content = string.Empty,
            TokenIndex = 0,
            IsFinal = true,
            ErrorMessage = "RuleBased provider does not support streaming. Use DraftScriptAsync instead."
        };
    }
}