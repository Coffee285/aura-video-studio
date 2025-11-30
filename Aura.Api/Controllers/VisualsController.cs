using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Aura.Providers.Visuals;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Aura.Api.Controllers;

/// <summary>
/// API controller for visual generation providers
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class VisualsController : ControllerBase
{
    private readonly ILogger<VisualsController> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;

    public VisualsController(
        ILogger<VisualsController> logger,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// GET /api/visuals/providers - Lists all available visual providers
    /// </summary>
    [HttpGet("providers")]
    public async Task<IActionResult> GetProviders(CancellationToken ct)
    {
        try
        {
            var providers = CreateAllProviders();
            var providerInfos = new List<ProviderInfo>();

            foreach (var provider in providers)
            {
                var capabilities = provider.GetProviderCapabilities();
                var isAvailable = await provider.IsAvailableAsync(ct).ConfigureAwait(false);

                providerInfos.Add(new ProviderInfo
                {
                    Name = provider.ProviderName,
                    IsAvailable = isAvailable,
                    RequiresApiKey = provider.RequiresApiKey,
                    Capabilities = capabilities
                });
            }

            // CRITICAL: Sort providers so Placeholder (guaranteed fallback) appears first
            // This ensures it's always visible and selectable
            providerInfos = providerInfos
                .OrderBy(p => p.Name == "Placeholder" ? 0 : 1)
                .ThenBy(p => p.Name)
                .ToList();

            return Ok(new
            {
                providers = providerInfos,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing visual providers");
            return Problem("Failed to list visual providers", statusCode: 500);
        }
    }

    /// <summary>
    /// POST /api/visuals/generate - Generate image with automatic provider selection
    /// </summary>
    [HttpPost("generate")]
    public async Task<IActionResult> Generate(
        [FromBody] GenerateRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return BadRequest(new { error = "Prompt is required" });
        }

        try
        {
            _logger.LogInformation("Generating image for prompt: {Prompt}", request.Prompt);

            var options = new VisualGenerationOptions
            {
                Width = request.Width ?? 1024,
                Height = request.Height ?? 1024,
                Style = request.Style ?? "photorealistic",
                AspectRatio = request.AspectRatio ?? "16:9",
                Quality = request.Quality ?? 80,
                NegativePrompts = request.NegativePrompts
            };

            var providers = CreateAllProviders();
            string? imagePath = null;
            string? usedProvider = null;
            BaseVisualProvider? placeholderProvider = null;

            // CRITICAL: Try providers in order, but Placeholder is always last as guaranteed fallback
            // This ensures generation never fails - Placeholder will always succeed
            foreach (var provider in providers)
            {
                // Save Placeholder for last resort
                if (provider.ProviderName == "Placeholder")
                {
                    placeholderProvider = provider;
                    continue;
                }

                var isAvailable = await provider.IsAvailableAsync(ct).ConfigureAwait(false);
                if (!isAvailable)
                {
                    _logger.LogDebug("Provider {Provider} not available, trying next", provider.ProviderName);
                    continue;
                }

                _logger.LogInformation("Attempting generation with provider: {Provider}", provider.ProviderName);
                imagePath = await provider.GenerateImageAsync(request.Prompt, options, ct).ConfigureAwait(false);

                if (imagePath != null)
                {
                    usedProvider = provider.ProviderName;
                    break;
                }
            }

            // If no other provider succeeded, use Placeholder (guaranteed to work)
            if (imagePath == null && placeholderProvider != null)
            {
                _logger.LogInformation("All other providers failed, using Placeholder as guaranteed fallback");
                imagePath = await placeholderProvider.GenerateImageAsync(request.Prompt, options, ct).ConfigureAwait(false);
                usedProvider = "Placeholder";
            }

            if (imagePath == null)
            {
                return Problem("All visual providers failed to generate image (including Placeholder fallback)", statusCode: 500);
            }

            return Ok(new
            {
                imagePath = imagePath,
                provider = usedProvider,
                prompt = request.Prompt,
                generatedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating image");
            return Problem("Failed to generate image", statusCode: 500);
        }
    }

    /// <summary>
    /// GET /api/visuals/styles - Get available styles per provider
    /// </summary>
    [HttpGet("styles")]
    public IActionResult GetStyles()
    {
        try
        {
            var providers = CreateAllProviders();
            var stylesByProvider = providers.ToDictionary(
                p => p.ProviderName,
                p => p.GetProviderCapabilities().SupportedStyles
            );

            var allStyles = providers
                .SelectMany(p => p.GetProviderCapabilities().SupportedStyles)
                .Distinct()
                .OrderBy(s => s)
                .ToList();

            return Ok(new
            {
                allStyles = allStyles,
                stylesByProvider = stylesByProvider
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting visual styles");
            return Problem("Failed to get visual styles", statusCode: 500);
        }
    }

    /// <summary>
    /// POST /api/visuals/validate - Validate prompt safety and compatibility
    /// </summary>
    [HttpPost("validate")]
    public IActionResult ValidatePrompt([FromBody] ValidateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return BadRequest(new { error = "Prompt is required" });
        }

        try
        {
            var issues = new List<string>();

            if (request.Prompt.Length > 2000)
            {
                issues.Add("Prompt is too long (max 2000 characters)");
            }

            var lowercasePrompt = request.Prompt.ToLowerInvariant();
            var unsafeKeywords = new[] { "nsfw", "explicit", "violent", "gore", "sexual" };
            if (unsafeKeywords.Any(keyword => lowercasePrompt.Contains(keyword)))
            {
                issues.Add("Prompt contains potentially unsafe content");
            }

            var isValid = issues.Count == 0;

            return Ok(new
            {
                isValid = isValid,
                issues = issues,
                prompt = request.Prompt,
                characterCount = request.Prompt.Length
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating prompt");
            return Problem("Failed to validate prompt", statusCode: 500);
        }
    }

    /// <summary>
    /// POST /api/visuals/batch - Generate multiple images in batch
    /// </summary>
    [HttpPost("batch")]
    public async Task<IActionResult> BatchGenerate(
        [FromBody] BatchGenerateRequest request,
        CancellationToken ct)
    {
        if (request.Prompts == null || request.Prompts.Length == 0)
        {
            return BadRequest(new { error = "At least one prompt is required" });
        }

        try
        {
            _logger.LogInformation("Batch generating {Count} images", request.Prompts.Length);

            var options = new VisualGenerationOptions
            {
                Width = request.Width ?? 1024,
                Height = request.Height ?? 1024,
                Style = request.Style ?? "photorealistic",
                AspectRatio = request.AspectRatio ?? "16:9",
                Quality = request.Quality ?? 80,
                NegativePrompts = request.NegativePrompts
            };

            var providers = CreateAllProviders();
            var results = new List<GeneratedImageResult>();
            var failedCount = 0;
            string? usedProvider = null;

            foreach (var provider in providers)
            {
                var isAvailable = await provider.IsAvailableAsync(ct).ConfigureAwait(false);
                if (!isAvailable)
                {
                    _logger.LogDebug("Provider {Provider} not available, trying next", provider.ProviderName);
                    continue;
                }

                _logger.LogInformation("Attempting batch generation with provider: {Provider}", provider.ProviderName);
                
                var batchResults = await provider.BatchGenerateAsync(
                    request.Prompts.ToList(),
                    options,
                    null,
                    ct).ConfigureAwait(false);

                if (batchResults != null && batchResults.Count > 0)
                {
                    usedProvider = provider.ProviderName;
                    
                    for (int i = 0; i < batchResults.Count; i++)
                    {
                        results.Add(new GeneratedImageResult
                        {
                            ImagePath = batchResults[i],
                            Prompt = i < request.Prompts.Length ? request.Prompts[i] : "",
                            GeneratedAt = DateTime.UtcNow
                        });
                    }
                    
                    failedCount = request.Prompts.Length - batchResults.Count;
                    break;
                }
            }

            if (results.Count == 0)
            {
                return Problem("All visual providers failed to generate images", statusCode: 500);
            }

            return Ok(new
            {
                images = results,
                totalGenerated = results.Count,
                failedCount = failedCount,
                provider = usedProvider,
                completedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in batch image generation");
            return Problem("Failed to generate images in batch", statusCode: 500);
        }
    }

    private List<BaseVisualProvider> CreateAllProviders()
    {
        var httpClient = _httpClientFactory.CreateClient();
        var providers = new List<BaseVisualProvider>();

        // CRITICAL: Placeholder provider is always added first as guaranteed fallback
        // This ensures generations never fail due to lack of image providers
        providers.Add(new PlaceholderProvider(
            _loggerFactory.CreateLogger<PlaceholderProvider>()));

        var apiKeys = LoadApiKeys();

        if (apiKeys.TryGetValue("openai", out var openaiKey) && !string.IsNullOrWhiteSpace(openaiKey))
        {
            providers.Add(new DallE3Provider(
                _loggerFactory.CreateLogger<DallE3Provider>(),
                httpClient,
                openaiKey));
        }

        if (apiKeys.TryGetValue("stabilityai", out var stabilityKey) && !string.IsNullOrWhiteSpace(stabilityKey))
        {
            providers.Add(new StabilityAiProvider(
                _loggerFactory.CreateLogger<StabilityAiProvider>(),
                httpClient,
                stabilityKey));
        }

        if (apiKeys.TryGetValue("midjourney", out var midjourneyKey) && !string.IsNullOrWhiteSpace(midjourneyKey))
        {
            providers.Add(new MidjourneyProvider(
                _loggerFactory.CreateLogger<MidjourneyProvider>(),
                httpClient,
                midjourneyKey));
        }

        providers.Add(new LocalStableDiffusionProvider(
            _loggerFactory.CreateLogger<LocalStableDiffusionProvider>(),
            httpClient));

        if (apiKeys.TryGetValue("unsplash", out var unsplashKey) && !string.IsNullOrWhiteSpace(unsplashKey))
        {
            providers.Add(new UnsplashVisualProvider(
                _loggerFactory.CreateLogger<UnsplashVisualProvider>(),
                httpClient,
                unsplashKey));
        }

        // Add Pexels stock provider if API key is configured
        if (apiKeys.TryGetValue("pexels", out var pexelsKey) && !string.IsNullOrWhiteSpace(pexelsKey))
        {
            providers.Add(new PexelsVisualProvider(
                _loggerFactory.CreateLogger<PexelsVisualProvider>(),
                httpClient,
                pexelsKey));
        }

        // Add Pixabay stock provider if API key is configured
        if (apiKeys.TryGetValue("pixabay", out var pixabayKey) && !string.IsNullOrWhiteSpace(pixabayKey))
        {
            providers.Add(new PixabayVisualProvider(
                _loggerFactory.CreateLogger<PixabayVisualProvider>(),
                httpClient,
                pixabayKey));
        }

        return providers;
    }

    private static Dictionary<string, string> LoadApiKeys()
    {
        try
        {
            var apiKeysPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Aura",
                "apikeys.json");

            if (!System.IO.File.Exists(apiKeysPath))
            {
                return new Dictionary<string, string>();
            }

            var json = System.IO.File.ReadAllText(apiKeysPath);
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    public class GenerateRequest
    {
        public string Prompt { get; set; } = string.Empty;
        public int? Width { get; set; }
        public int? Height { get; set; }
        public string? Style { get; set; }
        public string? AspectRatio { get; set; }
        public int? Quality { get; set; }
        public string[]? NegativePrompts { get; set; }
    }

    public class BatchGenerateRequest
    {
        public string[] Prompts { get; set; } = Array.Empty<string>();
        public int? Width { get; set; }
        public int? Height { get; set; }
        public string? Style { get; set; }
        public string? AspectRatio { get; set; }
        public int? Quality { get; set; }
        public string[]? NegativePrompts { get; set; }
    }

    public class GeneratedImageResult
    {
        public string ImagePath { get; set; } = string.Empty;
        public string Prompt { get; set; } = string.Empty;
        public DateTime GeneratedAt { get; set; }
        public int? Quality { get; set; }
        public double? ClipScore { get; set; }
    }

    public class ValidateRequest
    {
        public string Prompt { get; set; } = string.Empty;
    }

    public class ProviderInfo
    {
        public string Name { get; set; } = string.Empty;
        public bool IsAvailable { get; set; }
        public bool RequiresApiKey { get; set; }
        public VisualProviderCapabilities? Capabilities { get; set; }
    }
}
