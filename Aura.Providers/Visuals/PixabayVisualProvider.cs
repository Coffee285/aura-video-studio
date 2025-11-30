using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Aura.Core.Models.StockMedia;
using Aura.Providers.Images;
using Microsoft.Extensions.Logging;

namespace Aura.Providers.Visuals;

/// <summary>
/// Visual provider that uses Pixabay stock photos for image generation.
/// Free tier with rate limiting.
/// </summary>
public class PixabayVisualProvider : BaseVisualProvider
{
    private readonly EnhancedPixabayProvider _pixabayProvider;
    private readonly HttpClient _httpClient;

    public PixabayVisualProvider(
        ILogger<PixabayVisualProvider> logger,
        HttpClient httpClient,
        string? apiKey) : base(logger)
    {
        _httpClient = httpClient;
        _pixabayProvider = new EnhancedPixabayProvider(
            Microsoft.Extensions.Logging.LoggerFactory.Create(builder => { }).CreateLogger<EnhancedPixabayProvider>(),
            httpClient,
            apiKey);
    }

    public override string ProviderName => "Pixabay";

    public override bool RequiresApiKey => true;

    public override async Task<string?> GenerateImageAsync(
        string prompt,
        VisualGenerationOptions options,
        CancellationToken ct = default)
    {
        try
        {
            Logger.LogInformation("Searching Pixabay for: {Prompt}", prompt);

            var searchRequest = new StockMediaSearchRequest
            {
                Query = ExtractSearchKeywords(prompt),
                Count = 1,
                Page = 1,
                SafeSearchEnabled = true,
                Orientation = GetOrientation(options.AspectRatio)
            };

            var results = await _pixabayProvider.SearchAsync(searchRequest, ct).ConfigureAwait(false);

            if (results == null || results.Count == 0)
            {
                Logger.LogWarning("No Pixabay results found for: {Prompt}", prompt);
                return null;
            }

            var firstResult = results.First();
            var imageBytes = await _pixabayProvider.DownloadMediaAsync(firstResult.FullSizeUrl, ct).ConfigureAwait(false);

            var tempPath = Path.Combine(Path.GetTempPath(), $"pixabay_{Guid.NewGuid()}.jpg");
            await File.WriteAllBytesAsync(tempPath, imageBytes, ct).ConfigureAwait(false);

            Logger.LogInformation("Downloaded Pixabay image: {Id} to {Path}", firstResult.Id, tempPath);
            return tempPath;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to get image from Pixabay for prompt: {Prompt}", prompt);
            return null;
        }
    }

    public override VisualProviderCapabilities GetProviderCapabilities()
    {
        return new VisualProviderCapabilities
        {
            ProviderName = ProviderName,
            SupportsNegativePrompts = false,
            SupportsBatchGeneration = true,
            SupportsStylePresets = false,
            SupportedAspectRatios = new() { "16:9", "9:16", "1:1", "4:3" },
            SupportedStyles = new() { "photorealistic", "natural", "illustration", "vector", "photo" },
            MaxWidth = 6000,
            MaxHeight = 4000,
            IsLocal = false,
            IsFree = true,
            CostPerImage = 0m,
            Tier = "Free"
        };
    }

    public override async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            return await _pixabayProvider.ValidateAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    public override string AdaptPrompt(string prompt, VisualGenerationOptions options)
    {
        return ExtractSearchKeywords(prompt);
    }

    private static string ExtractSearchKeywords(string prompt)
    {
        var words = prompt.Split(new[] { ' ', ',', '.', ':', ';' }, StringSplitOptions.RemoveEmptyEntries);
        var keywords = words.Where(w => w.Length > 3 && !IsStopWord(w)).Take(5);
        return string.Join(" ", keywords);
    }

    private static bool IsStopWord(string word)
    {
        var stopWords = new[] { "the", "and", "with", "that", "this", "from", "have", "will", "would", "could" };
        return stopWords.Contains(word.ToLowerInvariant());
    }

    private static string? GetOrientation(string aspectRatio)
    {
        return aspectRatio switch
        {
            "16:9" or "4:3" => "horizontal",
            "9:16" or "3:4" => "vertical",
            "1:1" => "all", // Pixabay doesn't have a square-only filter
            _ => null
        };
    }
}
