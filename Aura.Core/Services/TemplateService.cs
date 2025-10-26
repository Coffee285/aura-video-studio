using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Aura.Core.Data;
using Aura.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aura.Core.Services;

/// <summary>
/// Service for managing project templates
/// </summary>
public class TemplateService
{
    private readonly ILogger<TemplateService> _logger;
    private readonly AuraDbContext _context;

    public TemplateService(ILogger<TemplateService> logger, AuraDbContext context)
    {
        _logger = logger;
        _context = context;
    }

    /// <summary>
    /// Get all templates with optional filtering
    /// </summary>
    public async Task<List<TemplateListItem>> GetTemplatesAsync(
        TemplateCategory? category = null,
        string? subCategory = null,
        bool systemOnly = false,
        bool communityOnly = false)
    {
        try
        {
            var query = _context.Templates.AsQueryable();

            if (category.HasValue)
            {
                query = query.Where(t => t.Category == category.Value.ToString());
            }

            if (!string.IsNullOrWhiteSpace(subCategory))
            {
                query = query.Where(t => t.SubCategory == subCategory);
            }

            if (systemOnly)
            {
                query = query.Where(t => t.IsSystemTemplate);
            }

            if (communityOnly)
            {
                query = query.Where(t => t.IsCommunityTemplate);
            }

            var templates = await query
                .OrderByDescending(t => t.UsageCount)
                .ThenByDescending(t => t.Rating)
                .ToListAsync();

            return templates.Select(MapToListItem).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get templates");
            throw;
        }
    }

    /// <summary>
    /// Get template by ID
    /// </summary>
    public async Task<ProjectTemplate?> GetTemplateByIdAsync(string id)
    {
        try
        {
            var entity = await _context.Templates.FindAsync(id);
            return entity != null ? MapToModel(entity) : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get template {TemplateId}", id);
            throw;
        }
    }

    /// <summary>
    /// Create new template
    /// </summary>
    public async Task<ProjectTemplate> CreateTemplateAsync(SaveAsTemplateRequest request)
    {
        try
        {
            var entity = new TemplateEntity
            {
                Id = Guid.NewGuid().ToString(),
                Name = request.Name,
                Description = request.Description,
                Category = request.Category.ToString(),
                SubCategory = request.SubCategory,
                Tags = string.Join(",", request.Tags),
                TemplateData = request.ProjectData,
                PreviewImage = request.PreviewImage,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Author = "User",
                IsSystemTemplate = false,
                IsCommunityTemplate = true,
                UsageCount = 0,
                Rating = 0.0,
                RatingCount = 0
            };

            _context.Templates.Add(entity);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Created template {TemplateId} - {TemplateName}", entity.Id, entity.Name);

            return MapToModel(entity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create template {TemplateName}", request.Name);
            throw;
        }
    }

    /// <summary>
    /// Delete template
    /// </summary>
    public async Task<bool> DeleteTemplateAsync(string id)
    {
        try
        {
            var entity = await _context.Templates.FindAsync(id);
            if (entity == null)
            {
                return false;
            }

            // Don't allow deleting system templates
            if (entity.IsSystemTemplate)
            {
                _logger.LogWarning("Attempted to delete system template {TemplateId}", id);
                return false;
            }

            _context.Templates.Remove(entity);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Deleted template {TemplateId}", id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete template {TemplateId}", id);
            throw;
        }
    }

    /// <summary>
    /// Increment usage count for template
    /// </summary>
    public async Task IncrementUsageAsync(string id)
    {
        try
        {
            var entity = await _context.Templates.FindAsync(id);
            if (entity != null)
            {
                entity.UsageCount++;
                await _context.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to increment usage for template {TemplateId}", id);
            // Don't throw - this is not critical
        }
    }

    /// <summary>
    /// Get effect presets
    /// </summary>
    public List<EffectPreset> GetEffectPresets()
    {
        return new List<EffectPreset>
        {
            new EffectPreset
            {
                Id = "cinematic",
                Name = "Cinematic",
                Description = "Professional film look with color grading and vignette",
                Category = "Professional",
                Effects = new List<TemplateEffect>
                {
                    new TemplateEffect
                    {
                        Id = "colorgrade1",
                        Name = "Color Grade",
                        Type = "colorGrade",
                        Parameters = new Dictionary<string, object>
                        {
                            { "temperature", 5800 },
                            { "tint", 10 },
                            { "saturation", 1.2 },
                            { "contrast", 1.15 }
                        }
                    },
                    new TemplateEffect
                    {
                        Id = "vignette1",
                        Name = "Vignette",
                        Type = "vignette",
                        Parameters = new Dictionary<string, object>
                        {
                            { "intensity", 0.3 },
                            { "roundness", 0.5 }
                        }
                    }
                }
            },
            new EffectPreset
            {
                Id = "retro",
                Name = "Retro",
                Description = "Vintage look with film grain and desaturation",
                Category = "Vintage",
                Effects = new List<TemplateEffect>
                {
                    new TemplateEffect
                    {
                        Id = "grain1",
                        Name = "Film Grain",
                        Type = "grain",
                        Parameters = new Dictionary<string, object>
                        {
                            { "intensity", 0.15 },
                            { "size", 1.5 }
                        }
                    },
                    new TemplateEffect
                    {
                        Id = "desat1",
                        Name = "Desaturation",
                        Type = "colorGrade",
                        Parameters = new Dictionary<string, object>
                        {
                            { "saturation", 0.6 },
                            { "temperature", 6200 }
                        }
                    }
                }
            },
            new EffectPreset
            {
                Id = "dynamic",
                Name = "Dynamic",
                Description = "High-energy look with motion blur and chromatic aberration",
                Category = "Action",
                Effects = new List<TemplateEffect>
                {
                    new TemplateEffect
                    {
                        Id = "motionblur1",
                        Name = "Motion Blur",
                        Type = "motionBlur",
                        Parameters = new Dictionary<string, object>
                        {
                            { "amount", 0.4 },
                            { "angle", 0 }
                        }
                    },
                    new TemplateEffect
                    {
                        Id = "chromatic1",
                        Name = "Chromatic Aberration",
                        Type = "chromaticAberration",
                        Parameters = new Dictionary<string, object>
                        {
                            { "amount", 0.02 }
                        }
                    }
                }
            }
        };
    }

    /// <summary>
    /// Get transition presets
    /// </summary>
    public List<TransitionPreset> GetTransitionPresets()
    {
        return new List<TransitionPreset>
        {
            new TransitionPreset
            {
                Id = "crossdissolve",
                Name = "Cross Dissolve",
                Type = "crossDissolve",
                DefaultDuration = 1.0
            },
            new TransitionPreset
            {
                Id = "wipe-left",
                Name = "Wipe Left",
                Type = "wipe",
                Direction = "left",
                DefaultDuration = 0.8
            },
            new TransitionPreset
            {
                Id = "wipe-right",
                Name = "Wipe Right",
                Type = "wipe",
                Direction = "right",
                DefaultDuration = 0.8
            },
            new TransitionPreset
            {
                Id = "wipe-up",
                Name = "Wipe Up",
                Type = "wipe",
                Direction = "up",
                DefaultDuration = 0.8
            },
            new TransitionPreset
            {
                Id = "wipe-down",
                Name = "Wipe Down",
                Type = "wipe",
                Direction = "down",
                DefaultDuration = 0.8
            },
            new TransitionPreset
            {
                Id = "zoom",
                Name = "Zoom",
                Type = "zoom",
                DefaultDuration = 1.0
            },
            new TransitionPreset
            {
                Id = "slide-left",
                Name = "Slide Left",
                Type = "slide",
                Direction = "left",
                DefaultDuration = 0.7
            },
            new TransitionPreset
            {
                Id = "slide-right",
                Name = "Slide Right",
                Type = "slide",
                Direction = "right",
                DefaultDuration = 0.7
            },
            new TransitionPreset
            {
                Id = "fade-black",
                Name = "Fade to Black",
                Type = "fade",
                Direction = "black",
                DefaultDuration = 1.5
            },
            new TransitionPreset
            {
                Id = "fade-white",
                Name = "Fade to White",
                Type = "fade",
                Direction = "white",
                DefaultDuration = 1.5
            }
        };
    }

    /// <summary>
    /// Get title templates
    /// </summary>
    public List<TitleTemplate> GetTitleTemplates()
    {
        return new List<TitleTemplate>
        {
            new TitleTemplate
            {
                Id = "lower-third",
                Name = "Lower Third",
                Category = "Informational",
                Duration = 5.0,
                TextLayers = new List<TemplateTextOverlay>
                {
                    new TemplateTextOverlay
                    {
                        Id = "lt-title",
                        Text = "Main Title",
                        FontSize = 36,
                        Color = "#FFFFFF",
                        Animation = "slideIn",
                        Position = new TemplatePosition { X = 0.1, Y = 0.85, Alignment = "left" },
                        StartTime = 0,
                        Duration = 5.0
                    },
                    new TemplateTextOverlay
                    {
                        Id = "lt-subtitle",
                        Text = "Subtitle",
                        FontSize = 24,
                        Color = "#CCCCCC",
                        Animation = "slideIn",
                        Position = new TemplatePosition { X = 0.1, Y = 0.9, Alignment = "left" },
                        StartTime = 0.2,
                        Duration = 4.8
                    }
                }
            },
            new TitleTemplate
            {
                Id = "end-credits",
                Name = "End Credits",
                Category = "Credits",
                Duration = 10.0,
                TextLayers = new List<TemplateTextOverlay>
                {
                    new TemplateTextOverlay
                    {
                        Id = "credits-text",
                        Text = "Credits text goes here",
                        FontSize = 32,
                        Color = "#FFFFFF",
                        Animation = "scrollUp",
                        Position = new TemplatePosition { X = 0.5, Y = 0.5, Alignment = "center" },
                        StartTime = 0,
                        Duration = 10.0
                    }
                }
            },
            new TitleTemplate
            {
                Id = "chapter-marker",
                Name = "Chapter Marker",
                Category = "Navigation",
                Duration = 3.0,
                TextLayers = new List<TemplateTextOverlay>
                {
                    new TemplateTextOverlay
                    {
                        Id = "chapter-text",
                        Text = "Chapter Title",
                        FontSize = 48,
                        Color = "#FFFFFF",
                        Animation = "fadeIn",
                        Position = new TemplatePosition { X = 0.5, Y = 0.5, Alignment = "center" },
                        StartTime = 0,
                        Duration = 3.0
                    }
                }
            },
            new TitleTemplate
            {
                Id = "subscribe-reminder",
                Name = "Subscribe Reminder",
                Category = "Call-to-Action",
                Duration = 5.0,
                TextLayers = new List<TemplateTextOverlay>
                {
                    new TemplateTextOverlay
                    {
                        Id = "subscribe-text",
                        Text = "Don't forget to subscribe!",
                        FontSize = 40,
                        Color = "#FF0000",
                        Animation = "bounce",
                        Position = new TemplatePosition { X = 0.5, Y = 0.2, Alignment = "center" },
                        StartTime = 0,
                        Duration = 5.0
                    }
                }
            }
        };
    }

    /// <summary>
    /// Seed database with sample templates
    /// </summary>
    public async Task SeedSampleTemplatesAsync()
    {
        try
        {
            // Check if templates already exist
            if (await _context.Templates.AnyAsync())
            {
                _logger.LogInformation("Templates already exist, skipping seed");
                return;
            }

            var sampleTemplates = GetSampleTemplates();

            foreach (var template in sampleTemplates)
            {
                _context.Templates.Add(template);
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation("Seeded {Count} sample templates", sampleTemplates.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to seed sample templates");
            throw;
        }
    }

    private List<TemplateEntity> GetSampleTemplates()
    {
        var templates = new List<TemplateEntity>();

        // YouTube Intro
        templates.Add(new TemplateEntity
        {
            Id = Guid.NewGuid().ToString(),
            Name = "YouTube Intro",
            Description = "Quick 5-second intro for YouTube videos",
            Category = "YouTube",
            SubCategory = "Intro",
            Tags = "intro,youtube,branding",
            PreviewImage = "/assets/templates/youtube-intro-preview.png",
            TemplateData = JsonSerializer.Serialize(new TemplateStructure
            {
                Duration = 5.0,
                Settings = new TemplateSettings { Width = 1920, Height = 1080, FrameRate = 30 },
                Tracks = new List<TemplateTrack>
                {
                    new TemplateTrack { Id = "video1", Label = "Video 1", Type = "video" },
                    new TemplateTrack { Id = "audio1", Label = "Audio 1", Type = "audio" }
                },
                Placeholders = new List<TemplatePlaceholder>
                {
                    new TemplatePlaceholder
                    {
                        Id = "intro-bg",
                        TrackId = "video1",
                        StartTime = 0,
                        Duration = 5.0,
                        Type = "video",
                        PlaceholderText = "Add your intro background"
                    }
                },
                TextOverlays = new List<TemplateTextOverlay>
                {
                    new TemplateTextOverlay
                    {
                        Id = "title",
                        TrackId = "video1",
                        StartTime = 1.0,
                        Duration = 3.0,
                        Text = "Your Channel Name",
                        FontSize = 72,
                        Color = "#FFFFFF",
                        Animation = "fadeIn",
                        Position = new TemplatePosition { X = 0.5, Y = 0.5, Alignment = "center" }
                    }
                },
                MusicTrack = new TemplateMusicTrack
                {
                    TrackId = "audio1",
                    StartTime = 0,
                    Duration = 5.0,
                    Volume = 0.6,
                    FadeIn = true,
                    FadeOut = true
                }
            }),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Author = "System",
            IsSystemTemplate = true,
            IsCommunityTemplate = false,
            UsageCount = 0,
            Rating = 4.5,
            RatingCount = 100
        });

        // Instagram Story
        templates.Add(new TemplateEntity
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Instagram Story",
            Description = "Vertical format template for Instagram Stories",
            Category = "SocialMedia",
            SubCategory = "Instagram Story",
            Tags = "instagram,story,social,vertical",
            PreviewImage = "/assets/templates/instagram-story-preview.png",
            TemplateData = JsonSerializer.Serialize(new TemplateStructure
            {
                Duration = 15.0,
                Settings = new TemplateSettings { Width = 1080, Height = 1920, FrameRate = 30, AspectRatio = "9:16" },
                Tracks = new List<TemplateTrack>
                {
                    new TemplateTrack { Id = "video1", Label = "Video 1", Type = "video" }
                },
                Placeholders = new List<TemplatePlaceholder>
                {
                    new TemplatePlaceholder
                    {
                        Id = "story-content",
                        TrackId = "video1",
                        StartTime = 0,
                        Duration = 15.0,
                        Type = "video",
                        PlaceholderText = "Add your story content"
                    }
                },
                TextOverlays = new List<TemplateTextOverlay>
                {
                    new TemplateTextOverlay
                    {
                        Id = "story-title",
                        TrackId = "video1",
                        StartTime = 2.0,
                        Duration = 5.0,
                        Text = "Swipe Up!",
                        FontSize = 48,
                        Color = "#FFFFFF",
                        Animation = "bounce",
                        Position = new TemplatePosition { X = 0.5, Y = 0.85, Alignment = "center" }
                    }
                }
            }),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Author = "System",
            IsSystemTemplate = true,
            IsCommunityTemplate = false,
            UsageCount = 0,
            Rating = 4.7,
            RatingCount = 150
        });

        // Product Demo
        templates.Add(new TemplateEntity
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Product Demo",
            Description = "Professional product demonstration template",
            Category = "Business",
            SubCategory = "Product Demo",
            Tags = "product,demo,business,marketing",
            PreviewImage = "/assets/templates/product-demo-preview.png",
            TemplateData = JsonSerializer.Serialize(new TemplateStructure
            {
                Duration = 30.0,
                Settings = new TemplateSettings { Width = 1920, Height = 1080, FrameRate = 30 },
                Tracks = new List<TemplateTrack>
                {
                    new TemplateTrack { Id = "video1", Label = "Video 1", Type = "video" },
                    new TemplateTrack { Id = "video2", Label = "Video 2", Type = "video" }
                },
                Placeholders = new List<TemplatePlaceholder>
                {
                    new TemplatePlaceholder
                    {
                        Id = "product-shot1",
                        TrackId = "video1",
                        StartTime = 0,
                        Duration = 10.0,
                        Type = "video",
                        PlaceholderText = "Product shot 1"
                    },
                    new TemplatePlaceholder
                    {
                        Id = "product-shot2",
                        TrackId = "video1",
                        StartTime = 10.0,
                        Duration = 10.0,
                        Type = "video",
                        PlaceholderText = "Product shot 2"
                    },
                    new TemplatePlaceholder
                    {
                        Id = "product-shot3",
                        TrackId = "video1",
                        StartTime = 20.0,
                        Duration = 10.0,
                        Type = "video",
                        PlaceholderText = "Product shot 3"
                    }
                },
                TextOverlays = new List<TemplateTextOverlay>
                {
                    new TemplateTextOverlay
                    {
                        Id = "product-name",
                        TrackId = "video2",
                        StartTime = 0,
                        Duration = 30.0,
                        Text = "Product Name",
                        FontSize = 54,
                        Color = "#FFFFFF",
                        Animation = "slideIn",
                        Position = new TemplatePosition { X = 0.1, Y = 0.1, Alignment = "left" }
                    }
                }
            }),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Author = "System",
            IsSystemTemplate = true,
            IsCommunityTemplate = false,
            UsageCount = 0,
            Rating = 4.3,
            RatingCount = 75
        });

        return templates;
    }

    private TemplateListItem MapToListItem(TemplateEntity entity)
    {
        return new TemplateListItem
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            Category = Enum.Parse<TemplateCategory>(entity.Category),
            SubCategory = entity.SubCategory,
            PreviewImage = entity.PreviewImage,
            PreviewVideo = entity.PreviewVideo,
            Tags = entity.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
            UsageCount = entity.UsageCount,
            Rating = entity.Rating,
            IsSystemTemplate = entity.IsSystemTemplate,
            IsCommunityTemplate = entity.IsCommunityTemplate
        };
    }

    private ProjectTemplate MapToModel(TemplateEntity entity)
    {
        return new ProjectTemplate
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            Category = Enum.Parse<TemplateCategory>(entity.Category),
            SubCategory = entity.SubCategory,
            PreviewImage = entity.PreviewImage,
            PreviewVideo = entity.PreviewVideo,
            Tags = entity.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
            TemplateData = entity.TemplateData,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            Author = entity.Author,
            IsSystemTemplate = entity.IsSystemTemplate,
            IsCommunityTemplate = entity.IsCommunityTemplate,
            UsageCount = entity.UsageCount,
            Rating = entity.Rating,
            RatingCount = entity.RatingCount
        };
    }
}
