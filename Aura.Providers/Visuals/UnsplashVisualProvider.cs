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
/// Visual provider that uses Unsplash stock photos as a fallback option
/// </summary>
public class UnsplashVisualProvider : BaseVisualProvider
{
    private readonly EnhancedUnsplashProvider _unsplashProvider;
    private readonly HttpClient _httpClient;

    public UnsplashVisualProvider(
        ILogger<UnsplashVisualProvider> logger,
        HttpClient httpClient,
        string? apiKey) : base(logger)
    {
        _httpClient = httpClient;
        _unsplashProvider = new EnhancedUnsplashProvider(
            Microsoft.Extensions.Logging.LoggerFactory.Create(builder => { }).CreateLogger<EnhancedUnsplashProvider>(),
            httpClient,
            apiKey);
    }

    public override string ProviderName => "Unsplash";

    public override bool RequiresApiKey => true;

    public override async Task<string?> GenerateImageAsync(
        string prompt,
        VisualGenerationOptions options,
        CancellationToken ct = default)
    {
        try
        {
            Logger.LogInformation("Searching Unsplash for: {Prompt}", prompt);

            var searchRequest = new StockMediaSearchRequest
            {
                Query = StockProviderUtils.ExtractSearchKeywords(prompt),
                Count = 1,
                Page = 1,
                SafeSearchEnabled = true,
                Orientation = GetOrientation(options.AspectRatio)
            };

            var results = await _unsplashProvider.SearchAsync(searchRequest, ct).ConfigureAwait(false);

            if (results == null || results.Count == 0)
            {
                Logger.LogWarning("No Unsplash results found for: {Prompt}", prompt);
                return null;
            }

            var firstResult = results.First();
            var imageBytes = await _unsplashProvider.DownloadMediaAsync(firstResult.FullSizeUrl, ct).ConfigureAwait(false);

            if (firstResult.DownloadUrl != null)
            {
                await _unsplashProvider.TrackDownloadAsync(firstResult.DownloadUrl, ct).ConfigureAwait(false);
            }

            var tempPath = Path.Combine(Path.GetTempPath(), $"unsplash_{Guid.NewGuid()}.jpg");
            await File.WriteAllBytesAsync(tempPath, imageBytes, ct).ConfigureAwait(false);

            Logger.LogInformation("Downloaded Unsplash image: {Id} to {Path}", firstResult.Id, tempPath);
            return tempPath;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to get image from Unsplash for prompt: {Prompt}", prompt);
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
            SupportedStyles = new() { "photorealistic", "natural", "outdoor", "indoor" },
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
            return await _unsplashProvider.ValidateAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    public override string AdaptPrompt(string prompt, VisualGenerationOptions options)
    {
        return StockProviderUtils.ExtractSearchKeywords(prompt);
    }

    private static string? GetOrientation(string aspectRatio)
    {
        return StockProviderUtils.GetUnsplashOrientation(aspectRatio);
    }
}
