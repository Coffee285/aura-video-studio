using System;
using System.Linq;

namespace Aura.Providers.Visuals;

/// <summary>
/// Shared utility for stock visual providers with common functionality
/// </summary>
public static class StockProviderUtils
{
    private static readonly string[] StopWords = { "the", "and", "with", "that", "this", "from", "have", "will", "would", "could" };

    /// <summary>
    /// Extract search keywords from a prompt
    /// </summary>
    public static string ExtractSearchKeywords(string prompt)
    {
        var words = prompt.Split(new[] { ' ', ',', '.', ':', ';' }, StringSplitOptions.RemoveEmptyEntries);
        var keywords = words.Where(w => w.Length > 3 && !IsStopWord(w)).Take(5);
        return string.Join(" ", keywords);
    }

    /// <summary>
    /// Check if a word is a stop word
    /// </summary>
    public static bool IsStopWord(string word)
    {
        return StopWords.Contains(word.ToLowerInvariant());
    }

    /// <summary>
    /// Get orientation for Pexels API
    /// </summary>
    public static string? GetPexelsOrientation(string aspectRatio)
    {
        return aspectRatio switch
        {
            "16:9" or "4:3" => "landscape",
            "9:16" or "3:4" => "portrait",
            "1:1" => "square",
            _ => null
        };
    }

    /// <summary>
    /// Get orientation for Pixabay API
    /// </summary>
    public static string? GetPixabayOrientation(string aspectRatio)
    {
        return aspectRatio switch
        {
            "16:9" or "4:3" => "horizontal",
            "9:16" or "3:4" => "vertical",
            "1:1" => "all", // Pixabay doesn't have a square-only filter
            _ => "all"
        };
    }

    /// <summary>
    /// Get orientation for Unsplash API
    /// </summary>
    public static string? GetUnsplashOrientation(string aspectRatio)
    {
        return aspectRatio switch
        {
            "16:9" or "4:3" => "landscape",
            "9:16" or "3:4" => "portrait",
            "1:1" => "squarish",
            _ => null
        };
    }
}
