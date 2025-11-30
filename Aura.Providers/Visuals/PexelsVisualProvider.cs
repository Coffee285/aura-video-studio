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
/// Visual provider that uses Pexels stock photos for image generation.
/// Free tier: 200 requests/hour.
/// </summary>
public class PexelsVisualProvider : BaseVisualProvider
{
    private readonly EnhancedPexelsProvider _pexelsProvider;

    public PexelsVisualProvider(
        ILogger<PexelsVisualProvider> logger,
        HttpClient httpClient,
        string? apiKey) : base(logger)
    {
        // Use same logger instance for the underlying provider
        _pexelsProvider = new EnhancedPexelsProvider(
            (ILogger<EnhancedPexelsProvider>)logger,
            httpClient,
            apiKey);
    }

    /// <summary>
    /// Constructor with explicit logger factory for proper logging configuration
    /// </summary>
    public PexelsVisualProvider(
        ILogger<PexelsVisualProvider> logger,
        ILoggerFactory loggerFactory,
        HttpClient httpClient,
        string? apiKey) : base(logger)
    {
        _pexelsProvider = new EnhancedPexelsProvider(
            loggerFactory.CreateLogger<EnhancedPexelsProvider>(),
            httpClient,
            apiKey);
    }

    public override string ProviderName => "Pexels";

    public override bool RequiresApiKey => true;

    public override async Task<string?> GenerateImageAsync(
        string prompt,
        VisualGenerationOptions options,
        CancellationToken ct = default)
    {
        try
        {
            Logger.LogInformation("Searching Pexels for: {Prompt}", prompt);

            var searchRequest = new StockMediaSearchRequest
            {
                Query = StockProviderUtils.ExtractSearchKeywords(prompt),
                Count = 1,
                Page = 1,
                SafeSearchEnabled = true,
                Orientation = StockProviderUtils.GetPexelsOrientation(options.AspectRatio)
            };

            var results = await _pexelsProvider.SearchAsync(searchRequest, ct).ConfigureAwait(false);

            if (results == null || results.Count == 0)
            {
                Logger.LogWarning("No Pexels results found for: {Prompt}", prompt);
                return null;
            }

            var firstResult = results.First();
            var imageBytes = await _pexelsProvider.DownloadMediaAsync(firstResult.FullSizeUrl, ct).ConfigureAwait(false);

            var tempPath = Path.Combine(Path.GetTempPath(), $"pexels_{Guid.NewGuid()}.jpg");
            await File.WriteAllBytesAsync(tempPath, imageBytes, ct).ConfigureAwait(false);

            Logger.LogInformation("Downloaded Pexels image: {Id} to {Path}", firstResult.Id, tempPath);
            return tempPath;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to get image from Pexels for prompt: {Prompt}", prompt);
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
            SupportedStyles = new() { "photorealistic", "natural", "outdoor", "indoor", "portrait", "nature" },
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
            return await _pexelsProvider.ValidateAsync(ct).ConfigureAwait(false);
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
}
