using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Aura.Core.Models;
using Aura.Core.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Aura.Api.Controllers;

/// <summary>
/// Controller for managing project templates
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class TemplatesController : ControllerBase
{
    private readonly ILogger<TemplatesController> _logger;
    private readonly TemplateService _templateService;

    public TemplatesController(ILogger<TemplatesController> logger, TemplateService templateService)
    {
        _logger = logger;
        _templateService = templateService;
    }

    /// <summary>
    /// Get all templates with optional filtering
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetTemplates(
        [FromQuery] string? category = null,
        [FromQuery] string? subCategory = null,
        [FromQuery] bool systemOnly = false,
        [FromQuery] bool communityOnly = false)
    {
        try
        {
            TemplateCategory? categoryEnum = null;
            if (!string.IsNullOrWhiteSpace(category) && Enum.TryParse<TemplateCategory>(category, true, out var parsed))
            {
                categoryEnum = parsed;
            }

            var templates = await _templateService.GetTemplatesAsync(
                categoryEnum,
                subCategory,
                systemOnly,
                communityOnly);

            return Ok(templates);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get templates");
            return StatusCode(500, new
            {
                type = "https://docs.aura.studio/errors/E500",
                title = "Internal Server Error",
                status = 500,
                detail = "Failed to retrieve templates",
                correlationId = HttpContext.TraceIdentifier
            });
        }
    }

    /// <summary>
    /// Get a specific template by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetTemplate(string id)
    {
        try
        {
            var template = await _templateService.GetTemplateByIdAsync(id);
            
            if (template == null)
            {
                return NotFound(new
                {
                    type = "https://docs.aura.studio/errors/E404",
                    title = "Template Not Found",
                    status = 404,
                    detail = $"Template with ID '{id}' was not found",
                    templateId = id
                });
            }

            return Ok(template);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get template {TemplateId}", id);
            return StatusCode(500, new
            {
                type = "https://docs.aura.studio/errors/E500",
                title = "Internal Server Error",
                status = 500,
                detail = "Failed to retrieve template",
                correlationId = HttpContext.TraceIdentifier
            });
        }
    }

    /// <summary>
    /// Create a new project from a template
    /// </summary>
    [HttpPost("create-from-template")]
    public async Task<IActionResult> CreateFromTemplate([FromBody] CreateFromTemplateRequest request)
    {
        try
        {
            var template = await _templateService.GetTemplateByIdAsync(request.TemplateId);
            
            if (template == null)
            {
                return NotFound(new
                {
                    type = "https://docs.aura.studio/errors/E404",
                    title = "Template Not Found",
                    status = 404,
                    detail = $"Template with ID '{request.TemplateId}' was not found",
                    templateId = request.TemplateId
                });
            }

            // Increment usage count
            await _templateService.IncrementUsageAsync(request.TemplateId);

            // Parse template structure
            var templateStructure = JsonSerializer.Deserialize<TemplateStructure>(template.TemplateData);
            
            if (templateStructure == null)
            {
                return BadRequest(new
                {
                    type = "https://docs.aura.studio/errors/E400",
                    title = "Invalid Template",
                    status = 400,
                    detail = "Template data is invalid",
                    templateId = request.TemplateId
                });
            }

            // Convert template to project file
            var projectFile = ConvertTemplateToProject(templateStructure, request.ProjectName);

            return Ok(new
            {
                projectFile,
                templateName = template.Name
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create project from template {TemplateId}", request.TemplateId);
            return StatusCode(500, new
            {
                type = "https://docs.aura.studio/errors/E500",
                title = "Internal Server Error",
                status = 500,
                detail = "Failed to create project from template",
                correlationId = HttpContext.TraceIdentifier
            });
        }
    }

    /// <summary>
    /// Save current project as a template
    /// </summary>
    [HttpPost("save-as-template")]
    public async Task<IActionResult> SaveAsTemplate([FromBody] SaveAsTemplateRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new
                {
                    type = "https://docs.aura.studio/errors/E400",
                    title = "Invalid Request",
                    status = 400,
                    detail = "Template name is required",
                    field = "Name"
                });
            }

            var template = await _templateService.CreateTemplateAsync(request);

            return Ok(new
            {
                id = template.Id,
                name = template.Name,
                message = "Template saved successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save template {TemplateName}", request.Name);
            return StatusCode(500, new
            {
                type = "https://docs.aura.studio/errors/E500",
                title = "Internal Server Error",
                status = 500,
                detail = "Failed to save template",
                correlationId = HttpContext.TraceIdentifier
            });
        }
    }

    /// <summary>
    /// Delete a template
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteTemplate(string id)
    {
        try
        {
            var deleted = await _templateService.DeleteTemplateAsync(id);
            
            if (!deleted)
            {
                return NotFound(new
                {
                    type = "https://docs.aura.studio/errors/E404",
                    title = "Template Not Found",
                    status = 404,
                    detail = $"Template with ID '{id}' was not found or cannot be deleted",
                    templateId = id
                });
            }

            return Ok(new { message = "Template deleted successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete template {TemplateId}", id);
            return StatusCode(500, new
            {
                type = "https://docs.aura.studio/errors/E500",
                title = "Internal Server Error",
                status = 500,
                detail = "Failed to delete template",
                correlationId = HttpContext.TraceIdentifier
            });
        }
    }

    /// <summary>
    /// Get effect presets
    /// </summary>
    [HttpGet("effect-presets")]
    public IActionResult GetEffectPresets()
    {
        try
        {
            var presets = _templateService.GetEffectPresets();
            return Ok(presets);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get effect presets");
            return StatusCode(500, new
            {
                type = "https://docs.aura.studio/errors/E500",
                title = "Internal Server Error",
                status = 500,
                detail = "Failed to retrieve effect presets",
                correlationId = HttpContext.TraceIdentifier
            });
        }
    }

    /// <summary>
    /// Get transition presets
    /// </summary>
    [HttpGet("transition-presets")]
    public IActionResult GetTransitionPresets()
    {
        try
        {
            var presets = _templateService.GetTransitionPresets();
            return Ok(presets);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get transition presets");
            return StatusCode(500, new
            {
                type = "https://docs.aura.studio/errors/E500",
                title = "Internal Server Error",
                status = 500,
                detail = "Failed to retrieve transition presets",
                correlationId = HttpContext.TraceIdentifier
            });
        }
    }

    /// <summary>
    /// Get title templates
    /// </summary>
    [HttpGet("title-templates")]
    public IActionResult GetTitleTemplates()
    {
        try
        {
            var templates = _templateService.GetTitleTemplates();
            return Ok(templates);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get title templates");
            return StatusCode(500, new
            {
                type = "https://docs.aura.studio/errors/E500",
                title = "Internal Server Error",
                status = 500,
                detail = "Failed to retrieve title templates",
                correlationId = HttpContext.TraceIdentifier
            });
        }
    }

    /// <summary>
    /// Seed sample templates (for development/testing)
    /// </summary>
    [HttpPost("seed")]
    public async Task<IActionResult> SeedTemplates()
    {
        try
        {
            await _templateService.SeedSampleTemplatesAsync();
            return Ok(new { message = "Sample templates seeded successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to seed sample templates");
            return StatusCode(500, new
            {
                type = "https://docs.aura.studio/errors/E500",
                title = "Internal Server Error",
                status = 500,
                detail = "Failed to seed sample templates",
                correlationId = HttpContext.TraceIdentifier
            });
        }
    }

    private object ConvertTemplateToProject(TemplateStructure templateStructure, string projectName)
    {
        // Convert template structure to project file format
        var now = DateTime.UtcNow.ToString("o");
        
        return new
        {
            version = "1.0.0",
            metadata = new
            {
                name = projectName,
                createdAt = now,
                lastModifiedAt = now,
                duration = templateStructure.Duration
            },
            settings = new
            {
                resolution = new
                {
                    width = templateStructure.Settings.Width,
                    height = templateStructure.Settings.Height
                },
                frameRate = templateStructure.Settings.FrameRate,
                sampleRate = 48000
            },
            tracks = templateStructure.Tracks.Select(t => new
            {
                id = t.Id,
                label = t.Label,
                type = t.Type,
                visible = true,
                locked = false
            }).ToList(),
            clips = templateStructure.Placeholders.Select(p => new
            {
                id = p.Id,
                trackId = p.TrackId,
                startTime = p.StartTime,
                duration = p.Duration,
                label = p.PlaceholderText,
                type = p.Type,
                isPlaceholder = true,
                preview = p.PreviewUrl
            }).ToList(),
            mediaLibrary = new List<object>(),
            playerPosition = 0
        };
    }
}
