using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Aura.Providers.Llm;

/// <summary>
/// Shared utility for cleaning LLM script output across all providers.
/// Removes metadata, formatting artifacts, and LLM meta-commentary that should not be read by TTS.
/// </summary>
public static class LlmScriptCleaner
{
    // Static compiled regex patterns for better performance
    private static readonly Regex MarkdownHeaderRegex = new(@"^#{1,3}\s+(.+?)$", RegexOptions.Compiled);
    private static readonly Regex SceneMetadataRegex = new(@"^scene\s+\d+\s*[:\-]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    // Match [VISUAL] markers with optional colon, whitespace, or content - e.g., [VISUAL], [VISUAL:], [VISUAL: description]
    private static readonly Regex VisualMarkerRegex = new(@"\[VISUAL(?::\s*[^\]]*|)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PauseMarkerRegex = new(@"\[PAUSE[^\]]*\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MediaMarkerRegex = new(@"\[(MUSIC|SFX|CUT|FADE)[^\]]*\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MultiSpaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex ParagraphSplitRegex = new(@"\n\s*\n", RegexOptions.Compiled);
    
    // Regex patterns for LLM meta-information that should not be included in TTS output
    private static readonly Regex WordCountRegex = new(@"^(?:Word\s*Count|Words?)\s*[:=]\s*\d+.*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TtsPacingRegex = new(@"^TTS\s*Pacing\s*(?:Check)?[:=].*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AiDetectionRegex = new(@"^AI\s*Detection\s*(?:Avoided)?[:=].*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex VisualSynergyRegex = new(@"^Visual\s*Synergy[:=].*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex EmotionalFlowRegex = new(@"^Emotional\s*Flow[:=].*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AccuracyNoteRegex = new(@"^Accuracy[:=].*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HorizontalRuleRegex = new(@"^[-_=]{3,}$", RegexOptions.Compiled);
    private static readonly Regex MetaLabelRegex = new(@"^(?:P\.?\s*S\.?|Note|Notes?|Tip|Tips?|Warning|Disclaimer|Summary|Conclusion|Format|Structure|Style|Tone|Target|Audience|Duration|Length|Keywords?|Tags?|SEO|Metadata|Meta|Credits?|Sources?|References?|Citations?)[:=].*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex WpmRegex = new(@"^\d+\s*WPM\b.*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BracketedMetaRegex = new(@"\[(?:Visual|Music|Sound|SFX|Cut|Fade|Transition|B-?Roll|Note|Source|Ref|Citation)[^\]]*\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Check if a line is metadata/formatting that should be excluded from narration
    /// </summary>
    public static bool IsMetadataLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return true;

        var trimmed = line.Trim();
        var trimmedLower = trimmed.ToLowerInvariant();

        // Basic metadata markers (original checks)
        if (trimmedLower.StartsWith("[visual:") ||
            trimmedLower.StartsWith("[visual]") ||
            trimmedLower.StartsWith("[pause") ||
            trimmedLower.StartsWith("[music") ||
            trimmedLower.StartsWith("[sfx") ||
            trimmedLower.StartsWith("[b-roll") ||
            trimmedLower.StartsWith("[broll") ||
            (trimmedLower.StartsWith("scene ") && SceneMetadataRegex.IsMatch(trimmedLower)) ||
            trimmedLower.StartsWith("duration:") ||
            trimmedLower.StartsWith("narration:") ||
            trimmedLower.StartsWith("visual:"))
        {
            return true;
        }

        // LLM meta-information that should not be read by TTS
        if (WordCountRegex.IsMatch(trimmed) ||
            TtsPacingRegex.IsMatch(trimmed) ||
            AiDetectionRegex.IsMatch(trimmed) ||
            VisualSynergyRegex.IsMatch(trimmed) ||
            EmotionalFlowRegex.IsMatch(trimmed) ||
            AccuracyNoteRegex.IsMatch(trimmed) ||
            HorizontalRuleRegex.IsMatch(trimmed) ||
            MetaLabelRegex.IsMatch(trimmed) ||
            WpmRegex.IsMatch(trimmed))
        {
            return true;
        }

        // Check for lines that are clearly metadata descriptions (no actual narrative content)
        // These often follow patterns like "Something: description" where Something is a meta label
        if (trimmedLower.StartsWith("word count") ||
            trimmedLower.StartsWith("tts pacing") ||
            trimmedLower.StartsWith("ai detection") ||
            trimmedLower.StartsWith("visual synergy") ||
            trimmedLower.StartsWith("emotional flow") ||
            trimmedLower.StartsWith("p.s.") ||
            trimmedLower.StartsWith("ps:") ||
            trimmedLower.StartsWith("note:") ||
            trimmedLower.StartsWith("notes:") ||
            trimmedLower.StartsWith("disclaimer:") ||
            trimmedLower.StartsWith("sources:") ||
            trimmedLower.StartsWith("references:") ||
            trimmedLower.StartsWith("citations:") ||
            trimmedLower.StartsWith("accuracy:") ||
            trimmedLower.StartsWith("---"))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Clean narration text by removing metadata and formatting artifacts
    /// </summary>
    public static string CleanNarration(string narration)
    {
        if (string.IsNullOrWhiteSpace(narration))
            return string.Empty;

        // Remove bracketed markers that shouldn't be read aloud
        var cleaned = VisualMarkerRegex.Replace(narration, "");
        cleaned = PauseMarkerRegex.Replace(cleaned, "");
        cleaned = MediaMarkerRegex.Replace(cleaned, "");
        cleaned = BracketedMetaRegex.Replace(cleaned, "");

        // Process line by line to remove metadata lines from within the text
        var lines = cleaned.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        var cleanedLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            
            // Skip lines that are clearly metadata
            if (IsMetadataLine(trimmedLine))
            {
                continue;
            }

            // Also filter out any lines that appear to be LLM meta-commentary
            // These often appear at the end of scripts
            if (IsLlmMetaCommentary(trimmedLine))
            {
                continue;
            }

            cleanedLines.Add(trimmedLine);
        }

        // Rejoin the cleaned lines
        var result = string.Join(" ", cleanedLines);
        
        // Final cleanup: remove multiple spaces
        result = MultiSpaceRegex.Replace(result, " ");

        return result.Trim();
    }

    /// <summary>
    /// Check if a line is LLM meta-commentary that should not be included in narration
    /// This catches things like "Word Count: X", "TTS Pacing Check:", etc.
    /// </summary>
    public static bool IsLlmMetaCommentary(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return true;

        var trimmed = line.Trim();

        // Skip lines that are just separators
        if (trimmed.All(c => c == '-' || c == '_' || c == '=' || c == '*'))
            return true;

        // Skip lines that start with common meta patterns
        var metaPatterns = new[]
        {
            "word count",
            "tts pacing",
            "ai detection",
            "visual synergy",
            "emotional flow",
            "accuracy:",
            "accuracy based",
            "based on common",
            "no hallucinated",
            "uses personal",
            "natural pauses",
            "formulaic phrases",
            "marketing fluff",
            "repetitive structures",
            "builds from",
            "starts with",
            "optimal for clarity",
            "under 25 words",
            "wpm"
        };

        var lowerLine = trimmed.ToLowerInvariant();
        foreach (var pattern in metaPatterns)
        {
            if (lowerLine.Contains(pattern))
            {
                // Make sure it's not part of actual narrative (check for common narrative starters)
                if (!lowerLine.StartsWith("i ") &&
                    !lowerLine.StartsWith("you ") &&
                    !lowerLine.StartsWith("we ") &&
                    !lowerLine.StartsWith("the ") &&
                    !lowerLine.StartsWith("a ") &&
                    !lowerLine.StartsWith("an "))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Get the markdown header regex for parsing
    /// </summary>
    public static Regex GetMarkdownHeaderRegex() => MarkdownHeaderRegex;

    /// <summary>
    /// Get the scene metadata regex for parsing
    /// </summary>
    public static Regex GetSceneMetadataRegex() => SceneMetadataRegex;

    /// <summary>
    /// Get the paragraph split regex for parsing
    /// </summary>
    public static Regex GetParagraphSplitRegex() => ParagraphSplitRegex;

    /// <summary>
    /// Get the multi-space regex for cleanup
    /// </summary>
    public static Regex GetMultiSpaceRegex() => MultiSpaceRegex;

    /// <summary>
    /// Get the visual marker regex for parsing
    /// </summary>
    public static Regex GetVisualMarkerRegex() => VisualMarkerRegex;

    /// <summary>
    /// Get the pause marker regex for parsing
    /// </summary>
    public static Regex GetPauseMarkerRegex() => PauseMarkerRegex;

    /// <summary>
    /// Get the media marker regex for parsing
    /// </summary>
    public static Regex GetMediaMarkerRegex() => MediaMarkerRegex;
}
