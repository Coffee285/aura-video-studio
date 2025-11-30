using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Aura.Core.Interfaces;
using Aura.Core.Models;
using Aura.Core.Models.Generation;
using Microsoft.Extensions.Logging;

namespace Aura.Providers.Llm;

/// <summary>
/// Base class for LLM script providers with common retry logic and error handling
/// </summary>
public abstract class BaseLlmScriptProvider : IScriptLlmProvider
{
    protected readonly ILogger _logger;
    protected readonly int _maxRetries;
    protected readonly TimeSpan _baseRetryDelay;

    // Static compiled regex patterns for better performance
    private static readonly Regex MarkdownHeaderRegex = new(@"^#{1,3}\s+(.+?)$", RegexOptions.Compiled);
    private static readonly Regex SceneMetadataRegex = new(@"^scene\s+\d+\s*[:\-]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex VisualMarkerRegex = new(@"\[VISUAL:[^\]]*\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PauseMarkerRegex = new(@"\[PAUSE[^\]]*\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MediaMarkerRegex = new(@"\[(MUSIC|SFX|CUT|FADE)[^\]]*\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MultiSpaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex ParagraphSplitRegex = new(@"\n\s*\n", RegexOptions.Compiled);
    
    /// <summary>
    /// Matches "Narration:" followed by content, stopping at standalone "Visual:" or "Transition:" labels.
    /// Pattern breakdown:
    /// - Narration:\s* - Match literal "Narration:" followed by optional whitespace
    /// - (.*?) - Capture group 1: non-greedy match of any characters (the narration content)
    /// - (?=\s*(?&lt;!\[)(?:Visual|Transition):|$) - Lookahead: stop at "Visual:" or "Transition:" 
    ///   NOT preceded by "[" (to avoid matching [VISUAL:...]), or at end of string
    /// </summary>
    private static readonly Regex NarrationExtractionRegex = new(
        @"Narration:\s*(.*?)(?=\s*(?<!\[)(?:Visual|Transition):|$)", 
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase
    );

    protected BaseLlmScriptProvider(ILogger logger, int maxRetries = 3, int baseRetryDelayMs = 1000)
    {
        _logger = logger;
        _maxRetries = maxRetries;
        _baseRetryDelay = TimeSpan.FromMilliseconds(baseRetryDelayMs);
    }

    /// <summary>
    /// Generate a script - implements retry logic and error handling
    /// </summary>
    public async Task<Script> GenerateScriptAsync(ScriptGenerationRequest request, CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        Exception? lastException = null;

        for (int attempt = 1; attempt <= _maxRetries; attempt++)
        {
            try
            {
                _logger.LogInformation(
                    "Attempting script generation with {Provider} (attempt {Attempt}/{MaxRetries}), CorrelationId: {CorrelationId}",
                    GetProviderMetadata().Name, attempt, _maxRetries, request.CorrelationId);

                var script = await GenerateScriptCoreAsync(request, cancellationToken).ConfigureAwait(false);

                var generationTime = DateTime.UtcNow - startTime;
                _logger.LogInformation(
                    "Script generation succeeded with {Provider} in {Duration}s, CorrelationId: {CorrelationId}",
                    GetProviderMetadata().Name, generationTime.TotalSeconds, request.CorrelationId);

                var updatedScript = script with 
                { 
                    Metadata = script.Metadata with { GenerationTime = generationTime }
                };

                return updatedScript;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Script generation cancelled, CorrelationId: {CorrelationId}", request.CorrelationId);
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogWarning(ex,
                    "Script generation attempt {Attempt}/{MaxRetries} failed with {Provider}: {Message}, CorrelationId: {CorrelationId}",
                    attempt, _maxRetries, GetProviderMetadata().Name, ex.Message, request.CorrelationId);

                if (attempt < _maxRetries)
                {
                    var delay = CalculateExponentialBackoff(attempt);
                    _logger.LogDebug("Retrying after {Delay}ms", delay.TotalMilliseconds);
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        _logger.LogError(lastException,
            "Script generation failed after {MaxRetries} attempts with {Provider}, CorrelationId: {CorrelationId}",
            _maxRetries, GetProviderMetadata().Name, request.CorrelationId);

        throw new InvalidOperationException(
            $"Script generation failed after {_maxRetries} attempts: {lastException?.Message}",
            lastException);
    }

    /// <summary>
    /// Core script generation logic - implemented by derived classes
    /// </summary>
    protected abstract Task<Script> GenerateScriptCoreAsync(ScriptGenerationRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Stream-based script generation for real-time updates (optional override)
    /// </summary>
    public virtual IAsyncEnumerable<ScriptGenerationProgress> StreamGenerateAsync(
        ScriptGenerationRequest request,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException($"Streaming not supported by {GetProviderMetadata().Name}");
    }

    /// <summary>
    /// Get available models for this provider
    /// </summary>
    public abstract Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Validate provider configuration
    /// </summary>
    public abstract Task<ProviderValidationResult> ValidateConfigurationAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Get provider metadata
    /// </summary>
    public abstract ProviderMetadata GetProviderMetadata();

    /// <summary>
    /// Check if provider is available
    /// </summary>
    public abstract Task<bool> IsAvailableAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Estimate token count for cost calculation (virtual for override)
    /// Uses simple heuristic: ~4 characters per token
    /// </summary>
    public virtual int EstimateTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        return (int)Math.Ceiling(text.Length / 4.0);
    }

    /// <summary>
    /// Calculate exponential backoff delay
    /// </summary>
    protected TimeSpan CalculateExponentialBackoff(int attempt)
    {
        var multiplier = Math.Pow(2, attempt - 1);
        var delayMs = _baseRetryDelay.TotalMilliseconds * multiplier;
        var jitter = Random.Shared.Next(0, 100);
        return TimeSpan.FromMilliseconds(delayMs + jitter);
    }

    /// <summary>
    /// Parse raw script text into structured scenes with TTS-aware duration calculation.
    /// Calculates duration based on word count at 150 WPM.
    /// Enforces minimum 3 seconds and maximum 30 seconds per scene.
    /// Common utility for text-based providers.
    /// </summary>
    protected List<ScriptScene> ParseScriptIntoScenes(string scriptText, PlanSpec planSpec)
    {
        var scenes = new List<ScriptScene>();
        
        if (string.IsNullOrWhiteSpace(scriptText))
        {
            return CreateFallbackScene(scriptText, planSpec);
        }

        // Try to parse structured scenes with markdown headers
        var parsedScenes = ParseMarkdownScenes(scriptText);
        
        // If no structured scenes found, try to intelligently segment the text
        if (parsedScenes.Count == 0)
        {
            parsedScenes = SegmentTextIntoScenes(scriptText, planSpec);
        }

        // If still no scenes, create a single scene
        if (parsedScenes.Count == 0)
        {
            return CreateFallbackScene(scriptText, planSpec);
        }

        // Calculate durations based on word count and validate scene count
        var targetSceneCount = planSpec.GetCalculatedSceneCount();
        
        int sceneNumber = 1;
        foreach (var (narration, heading) in parsedScenes)
        {
            if (string.IsNullOrWhiteSpace(narration))
                continue;

            var wordCount = CountWords(narration);
            var duration = CalculateTtsDuration(wordCount);
            
            var visualPrompt = GenerateDefaultVisualPrompt(narration);
            var transition = DetermineTransition(sceneNumber - 1, parsedScenes.Count, planSpec.Style);

            scenes.Add(new ScriptScene
            {
                Number = sceneNumber++,
                Narration = CleanNarration(narration),
                VisualPrompt = visualPrompt,
                Duration = duration,
                Transition = transition
            });
        }

        // Ensure we have at least one scene
        if (scenes.Count == 0)
        {
            return CreateFallbackScene(scriptText, planSpec);
        }

        _logger.LogInformation("Parsed {SceneCount} scenes from script (target: {TargetCount})",
            scenes.Count, targetSceneCount);

        return scenes;
    }

    /// <summary>
    /// Parse markdown-structured scenes (## Section headers)
    /// </summary>
    private List<(string narration, string heading)> ParseMarkdownScenes(string scriptText)
    {
        var scenes = new List<(string narration, string heading)>();
        var lines = scriptText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        
        string? currentHeading = null;
        var currentContent = new List<string>();

        foreach (var line in lines)
        {
            var headerMatch = MarkdownHeaderRegex.Match(line.Trim());
            if (headerMatch.Success)
            {
                // Save previous section if it has content
                if (currentHeading != null && currentContent.Count > 0)
                {
                    var narration = string.Join(" ", currentContent);
                    if (!string.IsNullOrWhiteSpace(narration))
                    {
                        scenes.Add((narration, currentHeading));
                    }
                }
                
                currentHeading = headerMatch.Groups[1].Value.Trim();
                currentContent.Clear();
            }
            else
            {
                // Skip metadata lines and visual markers
                var trimmedLine = line.Trim();
                if (!IsMetadataLine(trimmedLine) && !string.IsNullOrWhiteSpace(trimmedLine))
                {
                    currentContent.Add(trimmedLine);
                }
            }
        }

        // Add the last section
        if (currentHeading != null && currentContent.Count > 0)
        {
            var narration = string.Join(" ", currentContent);
            if (!string.IsNullOrWhiteSpace(narration))
            {
                scenes.Add((narration, currentHeading));
            }
        }

        return scenes;
    }

    /// <summary>
    /// Segment unstructured text into logical scenes based on content
    /// </summary>
    private List<(string narration, string heading)> SegmentTextIntoScenes(string scriptText, PlanSpec planSpec)
    {
        var scenes = new List<(string narration, string heading)>();
        
        // Split by paragraphs (double newlines)
        var paragraphs = ParagraphSplitRegex.Split(scriptText)
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrWhiteSpace(p) && !IsMetadataLine(p))
            .ToList();

        if (paragraphs.Count == 0)
        {
            return scenes;
        }

        // Calculate target scene count and words per scene
        var targetSceneCount = planSpec.GetCalculatedSceneCount();
        var totalWords = paragraphs.Sum(p => CountWords(p));
        var targetWordsPerScene = totalWords / targetSceneCount;

        // Group paragraphs into scenes based on word count targets
        var currentSceneWords = 0;
        var currentSceneContent = new List<string>();
        var sceneIndex = 1;

        foreach (var paragraph in paragraphs)
        {
            var paragraphWords = CountWords(paragraph);
            
            // If adding this paragraph would exceed target and we have content, start new scene
            if (currentSceneWords + paragraphWords > targetWordsPerScene * 1.5 && currentSceneContent.Count > 0)
            {
                var content = string.Join(" ", currentSceneContent);
                scenes.Add((content, $"Scene {sceneIndex}"));
                sceneIndex++;
                currentSceneContent.Clear();
                currentSceneWords = 0;
            }

            currentSceneContent.Add(paragraph);
            currentSceneWords += paragraphWords;

            // Check minimum words threshold to start a new scene
            if (currentSceneWords >= targetWordsPerScene * 0.7 && scenes.Count < targetSceneCount - 1)
            {
                var content = string.Join(" ", currentSceneContent);
                scenes.Add((content, $"Scene {sceneIndex}"));
                sceneIndex++;
                currentSceneContent.Clear();
                currentSceneWords = 0;
            }
        }

        // Add remaining content as final scene
        if (currentSceneContent.Count > 0)
        {
            var content = string.Join(" ", currentSceneContent);
            scenes.Add((content, $"Scene {sceneIndex}"));
        }

        return scenes;
    }

    /// <summary>
    /// Calculate TTS duration based on word count (150 WPM)
    /// with minimum 3 seconds and maximum 30 seconds bounds
    /// </summary>
    private TimeSpan CalculateTtsDuration(int wordCount)
    {
        const int wordsPerMinute = 150;
        const double minSeconds = 3.0;
        const double maxSeconds = 30.0;

        // Calculate duration based on word count
        var durationSeconds = (wordCount / (double)wordsPerMinute) * 60;
        
        // Apply bounds
        durationSeconds = Math.Clamp(durationSeconds, minSeconds, maxSeconds);
        
        return TimeSpan.FromSeconds(durationSeconds);
    }

    /// <summary>
    /// Check if a line is metadata/formatting that should be excluded from narration.
    /// Delegates to shared LlmScriptCleaner utility.
    /// </summary>
    private bool IsMetadataLine(string line)
    {
        return LlmScriptCleaner.IsMetadataLine(line);
    }

    /// <summary>
    /// Clean narration text by removing metadata and formatting artifacts.
    /// Delegates to shared LlmScriptCleaner utility.
    /// </summary>
    private string CleanNarration(string narration)
    {
        return LlmScriptCleaner.CleanNarration(narration);
    }

    /// <summary>
    /// Count words in text
    /// </summary>
    private int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        return text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    /// <summary>
    /// Create a fallback single scene when parsing fails
    /// </summary>
    private List<ScriptScene> CreateFallbackScene(string scriptText, PlanSpec planSpec)
    {
        var narration = CleanNarration(scriptText);
        if (string.IsNullOrWhiteSpace(narration))
        {
            narration = "Content could not be parsed.";
        }

        return new List<ScriptScene>
        {
            new ScriptScene
            {
                Number = 1,
                Narration = narration,
                VisualPrompt = GenerateDefaultVisualPrompt(narration),
                Duration = planSpec.TargetDuration,
                Transition = TransitionType.Cut
            }
        };
    }

    /// <summary>
    /// Extract narration text from scene content
    /// </summary>
    protected string ExtractNarration(string content)
    {
        var narrationMatch = NarrationExtractionRegex.Match(content);
        
        if (narrationMatch.Success && !string.IsNullOrWhiteSpace(narrationMatch.Groups[1].Value))
        {
            return CleanNarration(narrationMatch.Groups[1].Value.Trim());
        }

        // Fallback: return the entire content cleaned
        return CleanNarration(content.Trim());
    }

    /// <summary>
    /// Extract visual prompt from scene content
    /// </summary>
    protected string? ExtractVisualPrompt(string content)
    {
        var visualMatch = Regex.Match(content, @"Visual:\s*(.+?)(?=Narration:|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (visualMatch.Success)
        {
            return visualMatch.Groups[1].Value.Trim();
        }

        return null;
    }

    /// <summary>
    /// Generate a default visual prompt from narration
    /// </summary>
    protected string GenerateDefaultVisualPrompt(string narration)
    {
        var firstSentence = narration.Split(new[] { '.', '!', '?' }, 2)[0].Trim();
        return $"Visual representation of: {firstSentence}";
    }

    /// <summary>
    /// Determine appropriate transition based on position and style
    /// </summary>
    protected TransitionType DetermineTransition(int sceneIndex, int totalScenes, string style)
    {
        if (sceneIndex == totalScenes - 1)
        {
            return TransitionType.Fade;
        }

        return style.ToLowerInvariant() switch
        {
            var s when s.Contains("dynamic") || s.Contains("fast") => TransitionType.Cut,
            var s when s.Contains("smooth") || s.Contains("cinematic") => TransitionType.Dissolve,
            _ => TransitionType.Cut
        };
    }
}

/// <summary>
/// Progress update during streaming script generation
/// </summary>
public record ScriptGenerationProgress
{
    public string Stage { get; init; } = string.Empty;
    public int PercentComplete { get; init; }
    public string? PartialScript { get; init; }
    public string Message { get; init; } = string.Empty;
}
