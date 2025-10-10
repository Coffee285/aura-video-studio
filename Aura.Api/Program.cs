using Aura.Api.ErrorHandling;
using Aura.Api.Logging;
using Aura.Api.Middleware;
using Aura.Api.Serialization;
using Aura.Core.Hardware;
using Aura.Core.Models;
using Aura.Core.Orchestrator;
using Aura.Core.Planner;
using Aura.Core.Providers;
using Aura.Providers.Images;
using Aura.Providers.Llm;
using Aura.Providers.Tts;
using Aura.Providers.Video;
using Aura.Providers.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Serilog;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Configure JSON options to handle string enum conversion
builder.Services.ConfigureHttpJsonOptions(options =>
{
    // Add tolerant converters for enums that accept aliases
    options.SerializerOptions.Converters.Add(new TolerantDensityConverter());
    options.SerializerOptions.Converters.Add(new TolerantAspectConverter());
    // Use standard converter for other enums
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});

// Configure Serilog with correlation ID support
Log.Logger = SerilogConfig.ConfigureLogger(builder.Configuration);

builder.Host.UseSerilog();

// Add services to the container
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Register core services
builder.Services.AddSingleton<HardwareDetector>();
builder.Services.AddSingleton<Aura.Core.Configuration.ProviderSettings>();
builder.Services.AddHttpClient(); // For LLM providers
builder.Services.AddSingleton<Aura.Core.Orchestrator.LlmProviderFactory>();

// Provider mixing configuration
builder.Services.AddSingleton(sp =>
{
    var config = new ProviderMixingConfig
    {
        ActiveProfile = "Free-Only", // Default to free-only
        AutoFallback = true,
        LogProviderSelection = true
    };
    return config;
});

// Provider mixer
builder.Services.AddSingleton<Aura.Core.Orchestrator.ProviderMixer>();

// Script orchestrator with dynamic provider creation
builder.Services.AddSingleton<Aura.Core.Orchestrator.ScriptOrchestrator>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<Aura.Core.Orchestrator.ScriptOrchestrator>>();
    var mixer = sp.GetRequiredService<Aura.Core.Orchestrator.ProviderMixer>();
    var factory = sp.GetRequiredService<Aura.Core.Orchestrator.LlmProviderFactory>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    
    // Create available providers
    var providers = factory.CreateAvailableProviders(loggerFactory);
    
    return new Aura.Core.Orchestrator.ScriptOrchestrator(logger, mixer, providers);
});

// Keep backward compatibility - single ILlmProvider for simple use cases
builder.Services.AddSingleton<Aura.Core.Configuration.IKeyStore, Aura.Core.Configuration.KeyStore>();
builder.Services.AddSingleton<ILlmProvider, RuleBasedLlmProvider>();

// Register TTS provider factory and default provider
builder.Services.AddHttpClient();
builder.Services.AddSingleton<Aura.Core.Providers.TtsProviderFactory>();
builder.Services.AddSingleton<ITtsProvider>(sp =>
{
    var factory = sp.GetRequiredService<Aura.Core.Providers.TtsProviderFactory>();
    return factory.GetDefaultProvider();
});

builder.Services.AddSingleton<IVideoComposer>(sp => 
{
    var logger = sp.GetRequiredService<ILogger<FfmpegVideoComposer>>();
    var providerSettings = sp.GetRequiredService<Aura.Core.Configuration.ProviderSettings>();
    var ffmpegPath = providerSettings.GetFfmpegPath();
    var outputDirectory = providerSettings.GetOutputDirectory();
    return new FfmpegVideoComposer(logger, ffmpegPath, outputDirectory);
});
builder.Services.AddSingleton<VideoOrchestrator>();
builder.Services.AddSingleton<IRecommendationService, HeuristicRecommendationService>();
builder.Services.AddSingleton<Aura.Providers.Validation.ProviderValidationService>();

// Register DependencyManager
builder.Services.AddHttpClient<Aura.Core.Dependencies.DependencyManager>();
builder.Services.AddSingleton<Aura.Core.Dependencies.DependencyManager>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<Aura.Core.Dependencies.DependencyManager>>();
    var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
    var manifestPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aura", "manifest.json");
    var downloadDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aura", "dependencies");
    return new Aura.Core.Dependencies.DependencyManager(logger, httpClient, manifestPath, downloadDirectory);
});

// Configure Kestrel to listen on specific port
builder.WebHost.UseUrls("http://127.0.0.1:5005");

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

// Add correlation ID middleware
app.UseMiddleware<CorrelationIdMiddleware>();

// Serve static files from wwwroot (must be before routing)
var wwwrootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
if (Directory.Exists(wwwrootPath))
{
    Log.Information("Serving static files from: {Path}", wwwrootPath);
    
    app.UseDefaultFiles(); // Serve index.html as default file
    app.UseStaticFiles();
}
else
{
    Log.Warning("wwwroot directory not found at: {Path}", wwwrootPath);
    Log.Warning("Static file serving is disabled. Web UI will not be available.");
}

// API endpoints are grouped under /api prefix
var apiGroup = app.MapGroup("/api");

// Health check endpoint
apiGroup.MapGet("/healthz", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
    .WithName("HealthCheck")
    .WithOpenApi();

// Capabilities endpoint
apiGroup.MapGet("/capabilities", async (HardwareDetector detector, HttpContext context) =>
{
    try
    {
        var profile = await detector.DetectSystemAsync();
        return Results.Ok(new
        {
            tier = profile.Tier.ToString(),
            cpu = new { cores = profile.PhysicalCores, threads = profile.LogicalCores },
            ram = new { gb = profile.RamGB },
            gpu = profile.Gpu != null ? new { model = profile.Gpu.Model, vramGB = profile.Gpu.VramGB, vendor = profile.Gpu.Vendor } : null,
            enableNVENC = profile.EnableNVENC,
            enableSD = profile.EnableSD,
            offlineOnly = profile.OfflineOnly
        });
    }
    catch (Exception ex)
    {
        return ProblemDetailsHelper.CreateProblemWithCorrelation(
            context, 
            ex, 
            ErrorCodes.InternalError, 
            "Hardware Detection Failed",
            "Error detecting system capabilities. Please check logs for details.",
            500
        );
    }
})
.WithName("GetCapabilities")
.WithOpenApi();

// Plan endpoint - create or update timeline plan
apiGroup.MapPost("/plan", ([FromBody] PlanRequest request) =>
{
    try
    {
        var plan = new PlanSpec(
            TargetDuration: TimeSpan.FromMinutes(request.TargetDurationMinutes),
            Pacing: request.Pacing,
            Density: request.Density,
            Style: request.Style
        );
        
        return Results.Ok(new { success = true, plan });
    }
    catch (JsonException ex)
    {
        Log.Error(ex, "Invalid enum value in plan request");
        return Results.Problem(
            detail: ex.Message,
            statusCode: 400,
            title: "Invalid Enum Value",
            type: "https://docs.aura.studio/errors/E303");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error creating plan");
        return Results.Problem("Error creating plan", statusCode: 500);
    }
})
.WithName("CreatePlan")
.WithOpenApi();

// Planner recommendations endpoint
apiGroup.MapPost("/planner/recommendations", async (
    [FromBody] RecommendationsRequestDto request, 
    IRecommendationService recommendationService,
    CancellationToken ct) =>
{
    try
    {
        // Validate required fields
        if (string.IsNullOrWhiteSpace(request.Topic))
        {
            return Results.Problem(
                detail: "Topic is required",
                statusCode: 400,
                title: "Invalid Brief",
                type: "https://docs.aura.studio/errors/E303");
        }
        
        if (request.TargetDurationMinutes <= 0 || request.TargetDurationMinutes > 120)
        {
            return Results.Problem(
                detail: "Target duration must be between 0 and 120 minutes",
                statusCode: 400,
                title: "Invalid Plan",
                type: "https://docs.aura.studio/errors/E304");
        }

        var brief = new Brief(
            Topic: request.Topic,
            Audience: request.Audience ?? "General",
            Goal: request.Goal ?? "Inform",
            Tone: request.Tone ?? "Informative",
            Language: request.Language ?? "en-US",
            Aspect: request.Aspect ?? Aspect.Widescreen16x9
        );
        
        var planSpec = new PlanSpec(
            TargetDuration: TimeSpan.FromMinutes(request.TargetDurationMinutes),
            Pacing: request.Pacing ?? Pacing.Conversational,
            Density: request.Density ?? Density.Balanced,
            Style: request.Style ?? "Standard"
        );

        var constraints = request.Constraints != null 
            ? new RecommendationConstraints(
                MaxSceneCount: request.Constraints.MaxSceneCount,
                MinSceneCount: request.Constraints.MinSceneCount,
                MaxBRollPercentage: request.Constraints.MaxBRollPercentage,
                MaxReadingLevel: request.Constraints.MaxReadingLevel)
            : null;

        var recommendationRequest = new RecommendationRequest(
            Brief: brief,
            PlanSpec: planSpec,
            AudiencePersona: request.AudiencePersona,
            Constraints: constraints
        );

        Log.Information("Generating recommendations for topic: {Topic}, duration: {Duration} min", 
            request.Topic, request.TargetDurationMinutes);
        
        var recommendations = await recommendationService.GenerateRecommendationsAsync(recommendationRequest, ct);
        
        Log.Information("Recommendations generated successfully");
        return Results.Ok(new { success = true, recommendations });
    }
    catch (TaskCanceledException)
    {
        Log.Warning("Recommendation generation was cancelled");
        return Results.Problem(
            detail: "Recommendation generation was cancelled",
            statusCode: 408,
            title: "Request Timeout",
            type: "https://docs.aura.studio/errors/E301");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error generating recommendations: {Message}", ex.Message);
        return Results.Problem(
            detail: $"Error generating recommendations: {ex.Message}",
            statusCode: 500,
            title: "Recommendation Service Failed",
            type: "https://docs.aura.studio/errors/E305");
    }
})
.WithName("GetPlannerRecommendations")
.WithOpenApi();

// Script generation endpoint
apiGroup.MapPost("/script", async (
    [FromBody] ScriptRequest request, 
    Aura.Core.Orchestrator.ScriptOrchestrator orchestrator,
    HardwareDetector hardwareDetector,
    CancellationToken ct) =>
{
    try
    {
        // Validate required fields
        if (string.IsNullOrWhiteSpace(request.Topic))
        {
            return Results.Problem(
                detail: "Topic is required",
                statusCode: 400,
                title: "Invalid Brief",
                type: "https://docs.aura.studio/errors/E303");
        }
        
        if (request.TargetDurationMinutes <= 0 || request.TargetDurationMinutes > 120)
        {
            return Results.Problem(
                detail: "Target duration must be between 0 and 120 minutes",
                statusCode: 400,
                title: "Invalid Plan",
                type: "https://docs.aura.studio/errors/E304");
        }
        
        var brief = new Brief(
            Topic: request.Topic,
            Audience: request.Audience,
            Goal: request.Goal,
            Tone: request.Tone,
            Language: request.Language,
            Aspect: request.Aspect
        );
        
        var planSpec = new PlanSpec(
            TargetDuration: TimeSpan.FromMinutes(request.TargetDurationMinutes),
            Pacing: request.Pacing,
            Density: request.Density,
            Style: request.Style
        );
        
        // Determine provider tier from request or use default
        string preferredTier = request.ProviderTier ?? "Free";
        
        // Get system offline status
        var profile = await hardwareDetector.DetectSystemAsync();
        bool offlineOnly = profile.OfflineOnly;
        
        Log.Information("Generating script for topic: {Topic}, duration: {Duration} min, tier: {Tier}, offline: {Offline}", 
            request.Topic, request.TargetDurationMinutes, preferredTier, offlineOnly);
        
        var result = await orchestrator.GenerateScriptAsync(brief, planSpec, preferredTier, offlineOnly, ct);
        
        if (!result.Success)
        {
            Log.Error("Script generation failed: {ErrorCode} - {ErrorMessage}", result.ErrorCode, result.ErrorMessage);
            
            // Handle specific error codes
            if (result.ErrorCode == "E307")
            {
                return Results.Problem(
                    detail: result.ErrorMessage,
                    statusCode: 403,
                    title: "Offline Mode Restriction",
                    type: "https://docs.aura.studio/errors/E307");
            }
            
            return Results.Problem(
                detail: result.ErrorMessage,
                statusCode: 500,
                title: "Script Generation Failed",
                type: $"https://docs.aura.studio/errors/{result.ErrorCode}");
        }
        
        Log.Information("Script generated successfully with {Provider}: {Length} characters (fallback: {IsFallback})", 
            result.ProviderUsed, result.Script?.Length ?? 0, result.IsFallback);
        
        return Results.Ok(new 
        { 
            success = true, 
            script = result.Script,
            provider = result.ProviderUsed,
            isFallback = result.IsFallback
        });
    }
    catch (JsonException ex)
    {
        Log.Error(ex, "Invalid enum value in script request");
        return Results.Problem(
            detail: ex.Message,
            statusCode: 400,
            title: "Invalid Enum Value",
            type: "https://docs.aura.studio/errors/E303");
    }
    catch (ArgumentException ex)
    {
        Log.Error(ex, "Invalid argument for script generation");
        return Results.Problem(
            detail: ex.Message,
            statusCode: 400,
            title: "Invalid Request",
            type: "https://docs.aura.studio/errors/E303");
    }
    catch (TaskCanceledException)
    {
        Log.Warning("Script generation was cancelled");
        return Results.Problem(
            detail: "Script generation was cancelled",
            statusCode: 408,
            title: "Request Timeout",
            type: "https://docs.aura.studio/errors/E301");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error generating script: {Message}", ex.Message);
        return Results.Problem(
            detail: $"Error generating script: {ex.Message}",
            statusCode: 500,
            title: "Script Provider Failed",
            type: "https://docs.aura.studio/errors/E300");
    }
})
.WithName("GenerateScript")
.WithOpenApi();

// TTS endpoint
apiGroup.MapPost("/tts", async ([FromBody] TtsRequest request, ITtsProvider ttsProvider, CancellationToken ct) =>
{
    try
    {
        var lines = request.Lines.Select(l => new ScriptLine(
            SceneIndex: l.SceneIndex,
            Text: l.Text,
            Start: TimeSpan.FromSeconds(l.StartSeconds),
            Duration: TimeSpan.FromSeconds(l.DurationSeconds)
        )).ToList();
        
        var voiceSpec = new VoiceSpec(
            VoiceName: request.VoiceName,
            Rate: request.Rate,
            Pitch: request.Pitch,
            Pause: request.PauseStyle
        );
        
        var result = await ttsProvider.SynthesizeAsync(lines, voiceSpec, ct);
        
        return Results.Ok(new { success = true, audioPath = result });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error synthesizing audio");
        return Results.Problem("Error synthesizing audio", statusCode: 500);
    }
})
.WithName("SynthesizeAudio")
.WithOpenApi();

// Captions endpoint
apiGroup.MapPost("/captions/generate", async ([FromBody] CaptionsRequest request) =>
{
    try
    {
        var lines = request.Lines.Select(l => new ScriptLine(
            SceneIndex: l.SceneIndex,
            Text: l.Text,
            Start: TimeSpan.FromSeconds(l.StartSeconds),
            Duration: TimeSpan.FromSeconds(l.DurationSeconds)
        )).ToList();
        
        var audioProcessor = new Aura.Core.Audio.AudioProcessor(
            LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<Aura.Core.Audio.AudioProcessor>());
        
        string captions = request.Format.ToUpperInvariant() == "VTT"
            ? audioProcessor.GenerateVttSubtitles(lines)
            : audioProcessor.GenerateSrtSubtitles(lines);
        
        // Optionally save to file if path is provided
        string? filePath = null;
        if (!string.IsNullOrEmpty(request.OutputPath))
        {
            filePath = request.OutputPath;
            await System.IO.File.WriteAllTextAsync(filePath, captions);
            Log.Information("Captions saved to {Path}", filePath);
        }
        
        return Results.Ok(new { success = true, captions, filePath });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error generating captions");
        return Results.Problem("Error generating captions", statusCode: 500);
    }
})
.WithName("GenerateCaptions")
.WithOpenApi();

// Downloads manifest endpoint
apiGroup.MapGet("/downloads/manifest", async (Aura.Core.Dependencies.DependencyManager depManager) =>
{
    try
    {
        var manifest = await depManager.LoadManifestAsync();
        return Results.Ok(manifest);
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error reading manifest");
        return Results.Problem("Error reading manifest", statusCode: 500);
    }
})
.WithName("GetManifest")
.WithOpenApi();

// Check if component is installed
apiGroup.MapGet("/downloads/{component}/status", async (string component, Aura.Core.Dependencies.DependencyManager depManager) =>
{
    try
    {
        var isInstalled = await depManager.IsComponentInstalledAsync(component);
        return Results.Ok(new { component, isInstalled });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error checking component status");
        return Results.Problem("Error checking component status", statusCode: 500);
    }
})
.WithName("CheckComponentStatus")
.WithOpenApi();

// Download and install component
apiGroup.MapPost("/downloads/{component}/install", async (string component, Aura.Core.Dependencies.DependencyManager depManager, CancellationToken ct) =>
{
    try
    {
        // Create progress handler
        var progress = new Progress<Aura.Core.Dependencies.DownloadProgress>(p =>
        {
            Log.Information("Download progress: {Component} - {Percent}%", component, p.PercentComplete);
        });

        await depManager.DownloadComponentAsync(component, progress, ct);
        
        return Results.Ok(new { success = true, message = $"{component} installed successfully" });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error installing component");
        return Results.Problem($"Error installing {component}", statusCode: 500);
    }
})
.WithName("InstallComponent")
.WithOpenApi();

// Verify component integrity
apiGroup.MapGet("/downloads/{component}/verify", async (string component, Aura.Core.Dependencies.DependencyManager depManager) =>
{
    try
    {
        var result = await depManager.VerifyComponentAsync(component);
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error verifying component");
        return Results.Problem($"Error verifying {component}", statusCode: 500);
    }
})
.WithName("VerifyComponent")
.WithOpenApi();

// Repair component
apiGroup.MapPost("/downloads/{component}/repair", async (string component, Aura.Core.Dependencies.DependencyManager depManager, CancellationToken ct) =>
{
    try
    {
        var progress = new Progress<Aura.Core.Dependencies.DownloadProgress>(p =>
        {
            Log.Information("Repair progress: {Component} - {Percent}%", component, p.PercentComplete);
        });

        await depManager.RepairComponentAsync(component, progress, ct);
        
        return Results.Ok(new { success = true, message = $"{component} repaired successfully" });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error repairing component");
        return Results.Problem($"Error repairing {component}", statusCode: 500);
    }
})
.WithName("RepairComponent")
.WithOpenApi();

// Remove component
apiGroup.MapDelete("/downloads/{component}", async (string component, Aura.Core.Dependencies.DependencyManager depManager) =>
{
    try
    {
        await depManager.RemoveComponentAsync(component);
        return Results.Ok(new { success = true, message = $"{component} removed successfully" });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error removing component");
        return Results.Problem($"Error removing {component}", statusCode: 500);
    }
})
.WithName("RemoveComponent")
.WithOpenApi();

// Get component directory path
apiGroup.MapGet("/downloads/{component}/folder", (string component, Aura.Core.Dependencies.DependencyManager depManager) =>
{
    try
    {
        var path = depManager.GetComponentDirectory(component);
        return Results.Ok(new { path });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error getting component folder");
        return Results.Problem($"Error getting folder for {component}", statusCode: 500);
    }
})
.WithName("GetComponentFolder")
.WithOpenApi();

// Get manual install instructions (for offline mode)
apiGroup.MapGet("/downloads/{component}/manual", (string component, Aura.Core.Dependencies.DependencyManager depManager) =>
{
    try
    {
        var instructions = depManager.GetManualInstallInstructions(component);
        return Results.Ok(instructions);
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error getting manual instructions");
        return Results.Problem($"Error getting manual instructions for {component}", statusCode: 500);
    }
})
.WithName("GetManualInstructions")
.WithOpenApi();

// Settings endpoints
apiGroup.MapPost("/settings/save", ([FromBody] Dictionary<string, object> settings) =>
{
    try
    {
        var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aura", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        return Results.Ok(new { success = true });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error saving settings");
        return Results.Problem("Error saving settings", statusCode: 500);
    }
})
.WithName("SaveSettings")
.WithOpenApi();


apiGroup.MapGet("/settings/load", () =>
{
    try
    {
        var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aura", "settings.json");
        if (File.Exists(settingsPath))
        {
            var json = File.ReadAllText(settingsPath);
            var settings = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
            return Results.Ok(settings);
        }
        return Results.Ok(new Dictionary<string, object>());
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error loading settings");
        return Results.Problem("Error loading settings", statusCode: 500);
    }
})
.WithName("LoadSettings")
.WithOpenApi();

// Compose/Render endpoints - stub implementations for UI development
var renderJobs = new Dictionary<string, RenderJobDto>();

apiGroup.MapPost("/compose", ([FromBody] ComposeRequest request) =>
{
    try
    {
        var jobId = Guid.NewGuid().ToString();
        renderJobs[jobId] = new RenderJobDto(
            Id: jobId,
            Status: "queued",
            Progress: 0,
            OutputPath: null,
            CreatedAt: DateTime.UtcNow
        );
        
        return Results.Ok(new { success = true, jobId });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error composing timeline");
        return Results.Problem("Error composing timeline", statusCode: 500);
    }
})
.WithName("ComposeTimeline")
.WithOpenApi();


apiGroup.MapPost("/render", ([FromBody] RenderRequest request) =>
{
    try
    {
        var jobId = Guid.NewGuid().ToString();
        renderJobs[jobId] = new RenderJobDto(
            Id: jobId,
            Status: "queued",
            Progress: 0,
            OutputPath: null,
            CreatedAt: DateTime.UtcNow
        );
        
        return Results.Ok(new { success = true, jobId });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error starting render");
        return Results.Problem("Error starting render", statusCode: 500);
    }
})
.WithName("StartRender")
.WithOpenApi();


apiGroup.MapGet("/render/{id}/progress", (string id) =>
{
    if (!renderJobs.ContainsKey(id))
    {
        return Results.NotFound(new { error = "Render job not found" });
    }
    
    var job = renderJobs[id];
    return Results.Ok(new 
    { 
        id = job.Id,
        status = job.Status,
        progress = job.Progress,
        outputPath = job.OutputPath,
        createdAt = job.CreatedAt
    });
})
.WithName("GetRenderProgress")
.WithOpenApi();


apiGroup.MapPost("/render/{id}/cancel", (string id) =>
{
    if (!renderJobs.ContainsKey(id))
    {
        return Results.NotFound(new { error = "Render job not found" });
    }
    
    renderJobs[id] = renderJobs[id] with { Status = "cancelled" };
    return Results.Ok(new { success = true });
})
.WithName("CancelRender")
.WithOpenApi();


apiGroup.MapGet("/queue", () =>
{
    try
    {
        var jobs = renderJobs.Values.OrderByDescending(j => j.CreatedAt).ToList();
        return Results.Ok(new { jobs });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error retrieving queue");
        return Results.Problem("Error retrieving queue", statusCode: 500);
    }
})
.WithName("GetRenderQueue")
.WithOpenApi();


apiGroup.MapGet("/logs/stream", async (HttpContext context) =>
{
    context.Response.Headers.Append("Content-Type", "text/event-stream");
    context.Response.Headers.Append("Cache-Control", "no-cache");
    context.Response.Headers.Append("Connection", "keep-alive");
    
    try
    {
        // Simple log streaming - send a test message
        var message = $"data: {{\"timestamp\":\"{DateTime.UtcNow:O}\",\"level\":\"INFO\",\"message\":\"Log stream connected\"}}\n\n";
        await context.Response.WriteAsync(message);
        await context.Response.Body.FlushAsync();
        
        // Keep connection alive
        await Task.Delay(Timeout.Infinite, context.RequestAborted);
    }
    catch (OperationCanceledException)
    {
        // Client disconnected
    }
})
.WithName("StreamLogs")
.WithOpenApi();


apiGroup.MapPost("/probes/run", async (HardwareDetector detector) =>
{
    try
    {
        await detector.RunHardwareProbeAsync();
        var profile = await detector.DetectSystemAsync();
        return Results.Ok(new { success = true, profile });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error running probes");
        return Results.Problem("Error running hardware probes", statusCode: 500);
    }
})
.WithName("RunProbes")
.WithOpenApi();


apiGroup.MapGet("/profiles/list", () =>
{
    try
    {
        var profiles = new[]
        {
            new { name = "Free-Only", description = "Uses only free providers (no API keys required)" },
            new { name = "Balanced Mix", description = "Pro providers with free fallbacks" },
            new { name = "Pro-Max", description = "All pro providers (requires API keys)" }
        };
        return Results.Ok(new { profiles });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error listing profiles");
        return Results.Problem("Error listing profiles", statusCode: 500);
    }
})
.WithName("ListProfiles")
.WithOpenApi();


apiGroup.MapPost("/profiles/apply", ([FromBody] ApplyProfileRequest request) =>
{
    try
    {
        // Store profile selection in settings
        var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aura", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        
        var settings = new Dictionary<string, object> { ["profile"] = request.ProfileName };
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        
        return Results.Ok(new { success = true });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error applying profile");
        return Results.Problem("Error applying profile", statusCode: 500);
    }
})
.WithName("ApplyProfile")
.WithOpenApi();

// API Key Management
apiGroup.MapPost("/apikeys/save", ([FromBody] ApiKeysRequest request) =>
{
    try
    {
        var keysPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aura", "apikeys.json");
        Directory.CreateDirectory(Path.GetDirectoryName(keysPath)!);
        
        // In production, these should be encrypted using DPAPI or similar
        var keys = new Dictionary<string, string>
        {
            ["openai"] = request.OpenAiKey ?? "",
            ["elevenlabs"] = request.ElevenLabsKey ?? "",
            ["pexels"] = request.PexelsKey ?? "",
            ["stabilityai"] = request.StabilityAiKey ?? ""
        };
        
        File.WriteAllText(keysPath, JsonSerializer.Serialize(keys, new JsonSerializerOptions { WriteIndented = true }));
        
        return Results.Ok(new { success = true, message = "API keys saved successfully" });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error saving API keys");
        return Results.Problem("Error saving API keys", statusCode: 500);
    }
})
.WithName("SaveApiKeys")
.WithOpenApi();

apiGroup.MapGet("/apikeys/load", () =>
{
    try
    {
        var keysPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aura", "apikeys.json");
        if (File.Exists(keysPath))
        {
            var json = File.ReadAllText(keysPath);
            var keys = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            
            // Return masked keys (only show first 8 characters)
            var maskedKeys = keys?.ToDictionary(
                k => k.Key,
                k => string.IsNullOrEmpty(k.Value) ? "" : k.Value.Substring(0, Math.Min(8, k.Value.Length)) + "..."
            );
            
            return Results.Ok(maskedKeys);
        }
        return Results.Ok(new Dictionary<string, string>
        {
            ["openai"] = "",
            ["elevenlabs"] = "",
            ["pexels"] = "",
            ["stabilityai"] = ""
        });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error loading API keys");
        return Results.Problem("Error loading API keys", statusCode: 500);
    }
})
.WithName("LoadApiKeys")
.WithOpenApi();

// Local Provider Paths Configuration
apiGroup.MapPost("/providers/paths/save", ([FromBody] ProviderPathsRequest request) =>
{
    try
    {
        var pathsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aura", "provider-paths.json");
        Directory.CreateDirectory(Path.GetDirectoryName(pathsConfigPath)!);
        
        var paths = new Dictionary<string, object>
        {
            ["stableDiffusionUrl"] = request.StableDiffusionUrl ?? "http://127.0.0.1:7860",
            ["ollamaUrl"] = request.OllamaUrl ?? "http://127.0.0.1:11434",
            ["ffmpegPath"] = request.FfmpegPath ?? "",
            ["ffprobePath"] = request.FfprobePath ?? "",
            ["outputDirectory"] = request.OutputDirectory ?? ""
        };
        
        File.WriteAllText(pathsConfigPath, JsonSerializer.Serialize(paths, new JsonSerializerOptions { WriteIndented = true }));
        
        return Results.Ok(new { success = true, message = "Provider paths saved successfully" });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error saving provider paths");
        return Results.Problem("Error saving provider paths", statusCode: 500);
    }
})
.WithName("SaveProviderPaths")
.WithOpenApi();

apiGroup.MapGet("/providers/paths/load", () =>
{
    try
    {
        var pathsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aura", "provider-paths.json");
        if (File.Exists(pathsConfigPath))
        {
            var json = File.ReadAllText(pathsConfigPath);
            var paths = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
            return Results.Ok(paths);
        }
        
        // Return defaults
        return Results.Ok(new Dictionary<string, object>
        {
            ["stableDiffusionUrl"] = "http://127.0.0.1:7860",
            ["ollamaUrl"] = "http://127.0.0.1:11434",
            ["ffmpegPath"] = "",
            ["ffprobePath"] = "",
            ["outputDirectory"] = ""
        });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error loading provider paths");
        return Results.Problem("Error loading provider paths", statusCode: 500);
    }
})
.WithName("LoadProviderPaths")
.WithOpenApi();

// Assets search endpoint - search stock providers
apiGroup.MapPost("/assets/search", async ([FromBody] AssetSearchRequest request, HardwareDetector detector, IHttpClientFactory httpClientFactory, CancellationToken ct) =>
{
    try
    {
        var profile = await detector.DetectSystemAsync();
        
        // Check if offline only mode
        if (profile.OfflineOnly && request.Provider != "local")
        {
            return Results.Ok(new 
            { 
                success = false, 
                gated = true,
                reason = "Offline mode enabled - only local assets are available",
                assets = Array.Empty<object>()
            });
        }

        var httpClient = httpClientFactory.CreateClient();
        var assets = new List<object>();

        switch (request.Provider.ToLowerInvariant())
        {
            case "pexels":
                if (string.IsNullOrEmpty(request.ApiKey))
                {
                    return Results.Ok(new 
                    { 
                        success = false, 
                        gated = true,
                        reason = "Pexels API key required",
                        assets = Array.Empty<object>()
                    });
                }
                var pexelsProvider = new Aura.Providers.Images.PexelsStockProvider(
                    LoggerFactory.Create(b => b.AddConsole()).CreateLogger<Aura.Providers.Images.PexelsStockProvider>(),
                    httpClient,
                    request.ApiKey);
                var pexelsAssets = await pexelsProvider.SearchAsync(request.Query, request.Count, ct);
                assets.AddRange(pexelsAssets.Select(a => new { a.Kind, a.PathOrUrl, a.License, a.Attribution }));
                break;

            case "pixabay":
                if (string.IsNullOrEmpty(request.ApiKey))
                {
                    return Results.Ok(new 
                    { 
                        success = false, 
                        gated = true,
                        reason = "Pixabay API key required",
                        assets = Array.Empty<object>()
                    });
                }
                var pixabayProvider = new Aura.Providers.Images.PixabayStockProvider(
                    LoggerFactory.Create(b => b.AddConsole()).CreateLogger<Aura.Providers.Images.PixabayStockProvider>(),
                    httpClient,
                    request.ApiKey);
                var pixabayAssets = await pixabayProvider.SearchAsync(request.Query, request.Count, ct);
                assets.AddRange(pixabayAssets.Select(a => new { a.Kind, a.PathOrUrl, a.License, a.Attribution }));
                break;

            case "unsplash":
                if (string.IsNullOrEmpty(request.ApiKey))
                {
                    return Results.Ok(new 
                    { 
                        success = false, 
                        gated = true,
                        reason = "Unsplash API key required",
                        assets = Array.Empty<object>()
                    });
                }
                var unsplashProvider = new Aura.Providers.Images.UnsplashStockProvider(
                    LoggerFactory.Create(b => b.AddConsole()).CreateLogger<Aura.Providers.Images.UnsplashStockProvider>(),
                    httpClient,
                    request.ApiKey);
                var unsplashAssets = await unsplashProvider.SearchAsync(request.Query, request.Count, ct);
                assets.AddRange(unsplashAssets.Select(a => new { a.Kind, a.PathOrUrl, a.License, a.Attribution }));
                break;

            case "local":
                var localDirectory = request.LocalDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aura", "Assets");
                var localProvider = new Aura.Providers.Images.LocalStockProvider(
                    LoggerFactory.Create(b => b.AddConsole()).CreateLogger<Aura.Providers.Images.LocalStockProvider>(),
                    localDirectory);
                var localAssets = await localProvider.SearchAsync(request.Query, request.Count, ct);
                assets.AddRange(localAssets.Select(a => new { a.Kind, a.PathOrUrl, a.License, a.Attribution }));
                break;

            default:
                return Results.BadRequest(new { success = false, message = $"Unknown provider: {request.Provider}" });
        }

        return Results.Ok(new { success = true, gated = false, assets });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error searching assets");
        return Results.Problem("Error searching assets", statusCode: 500);
    }
})
.WithName("SearchAssets")
.WithOpenApi();

// Assets generate endpoint - generate with Stable Diffusion
apiGroup.MapPost("/assets/generate", async ([FromBody] AssetGenerateRequest request, HardwareDetector detector, IHttpClientFactory httpClientFactory, CancellationToken ct) =>
{
    try
    {
        var profile = await detector.DetectSystemAsync();
        
        // NVIDIA GPU gate - can be bypassed
        if (!request.BypassHardwareChecks && (profile.Gpu == null || profile.Gpu.Vendor.ToLowerInvariant() != "nvidia"))
        {
            return Results.Ok(new 
            { 
                success = false, 
                gated = true,
                reason = "Stable Diffusion typically requires an NVIDIA GPU. Use stock visuals, Pro cloud, or set BypassHardwareChecks=true to override.",
                assets = Array.Empty<object>()
            });
        }

        // VRAM gate - can be bypassed
        if (!request.BypassHardwareChecks && profile.Gpu != null && profile.Gpu.VramGB < 6)
        {
            return Results.Ok(new 
            { 
                success = false, 
                gated = true,
                reason = $"Insufficient VRAM ({profile.Gpu.VramGB}GB). Stable Diffusion typically requires minimum 6GB VRAM. Set BypassHardwareChecks=true to override.",
                assets = Array.Empty<object>()
            });
        }

        // Offline mode gate
        if (profile.OfflineOnly)
        {
            return Results.Ok(new 
            { 
                success = false, 
                gated = true,
                reason = "Offline mode enabled - Stable Diffusion WebUI requires network access",
                assets = Array.Empty<object>()
            });
        }

        var httpClient = httpClientFactory.CreateClient();
        var sdUrl = request.StableDiffusionUrl ?? "http://127.0.0.1:7860";
        
        var sdParams = new Aura.Providers.Images.SDGenerationParams
        {
            Model = request.Model,
            Steps = request.Steps,
            CfgScale = request.CfgScale ?? 7.0,
            Seed = request.Seed ?? -1,
            Width = request.Width,
            Height = request.Height,
            Style = request.Style ?? "high quality, detailed, professional",
            SamplerName = request.SamplerName ?? "DPM++ 2M Karras"
        };

        // Determine if we're using NVIDIA GPU or bypassing checks
        bool isNvidiaGpu = profile.Gpu != null && profile.Gpu.Vendor.ToLowerInvariant() == "nvidia";
        int vramGB = profile.Gpu?.VramGB ?? 0;

        var sdProvider = new Aura.Providers.Images.StableDiffusionWebUiProvider(
            LoggerFactory.Create(b => b.AddConsole()).CreateLogger<Aura.Providers.Images.StableDiffusionWebUiProvider>(),
            httpClient,
            sdUrl,
            isNvidiaGpu,
            vramGB,
            sdParams,
            request.BypassHardwareChecks);

        // Create a dummy scene for generation
        var scene = new Scene(
            Index: request.SceneIndex ?? 0,
            Heading: request.Prompt,
            Script: "",
            Start: TimeSpan.Zero,
            Duration: TimeSpan.FromSeconds(5));

        var spec = new VisualSpec(
            Style: request.Style ?? "",
            Aspect: request.Aspect ?? Aspect.Widescreen16x9,
            Keywords: request.Keywords ?? Array.Empty<string>());

        var assets = await sdProvider.FetchOrGenerateAsync(scene, spec, sdParams, ct);

        return Results.Ok(new 
        { 
            success = true, 
            gated = false,
            model = profile.Gpu.VramGB >= 12 ? "SDXL" : "SD 1.5",
            vramGB = profile.Gpu.VramGB,
            assets = assets.Select(a => new { a.Kind, a.PathOrUrl, a.License, a.Attribution })
        });
    }
    catch (HttpRequestException ex)
    {
        Log.Warning(ex, "Failed to connect to Stable Diffusion WebUI");
        return Results.Ok(new 
        { 
            success = false, 
            gated = true,
            reason = "Failed to connect to Stable Diffusion WebUI. Is it running?",
            assets = Array.Empty<object>()
        });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error generating assets with Stable Diffusion");
        return Results.Problem("Error generating assets", statusCode: 500);
    }
})
.WithName("GenerateAssets")
.WithOpenApi();

// Test provider connections
apiGroup.MapPost("/providers/test/{provider}", async (string provider, [FromBody] ProviderTestRequest request, IHttpClientFactory httpClientFactory) =>
{
    try
    {
        var httpClient = httpClientFactory.CreateClient();
        
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        
        switch (provider.ToLower())
        {
            case "stablediffusion":
                try
                {
                    var sdUrl = request.Url ?? "http://127.0.0.1:7860";
                    var response = await httpClient.GetAsync($"{sdUrl}/sdapi/v1/sd-models", cts.Token);
                    if (response.IsSuccessStatusCode)
                    {
                        return Results.Ok(new { success = true, message = "Successfully connected to Stable Diffusion WebUI" });
                    }
                    return Results.Ok(new { success = false, message = $"Stable Diffusion WebUI returned status: {response.StatusCode}" });
                }
                catch (Exception ex)
                {
                    return Results.Ok(new { success = false, message = $"Failed to connect: {ex.Message}" });
                }
                
            case "ollama":
                try
                {
                    var ollamaUrl = request.Url ?? "http://127.0.0.1:11434";
                    var response = await httpClient.GetAsync($"{ollamaUrl}/api/tags", cts.Token);
                    if (response.IsSuccessStatusCode)
                    {
                        return Results.Ok(new { success = true, message = "Successfully connected to Ollama" });
                    }
                    return Results.Ok(new { success = false, message = $"Ollama returned status: {response.StatusCode}" });
                }
                catch (Exception ex)
                {
                    return Results.Ok(new { success = false, message = $"Failed to connect: {ex.Message}" });
                }
                
            case "ffmpeg":
                try
                {
                    var ffmpegPath = request.Path ?? "ffmpeg";
                    var processInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        Arguments = "-version",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    
                    using var process = System.Diagnostics.Process.Start(processInfo);
                    if (process != null)
                    {
                        await process.WaitForExitAsync();
                        if (process.ExitCode == 0)
                        {
                            var output = await process.StandardOutput.ReadToEndAsync();
                            var versionLine = output.Split('\n').FirstOrDefault(l => l.Contains("ffmpeg version"));
                            return Results.Ok(new { success = true, message = versionLine ?? "FFmpeg found and working" });
                        }
                    }
                    return Results.Ok(new { success = false, message = "FFmpeg returned non-zero exit code" });
                }
                catch (Exception ex)
                {
                    return Results.Ok(new { success = false, message = $"Failed to execute FFmpeg: {ex.Message}" });
                }
                
            default:
                return Results.BadRequest(new { success = false, message = $"Unknown provider: {provider}" });
        }
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error testing provider connection");
        return Results.Problem($"Error testing {provider}", statusCode: 500);
    }
})
.WithName("TestProviderConnection")
.WithOpenApi();

// Validate providers
apiGroup.MapPost("/providers/validate", async (
    [FromBody] ValidateProvidersRequest? request,
    ProviderValidationService validationService,
    CancellationToken ct) =>
{
    try
    {
        var providers = request?.Providers?.Length > 0 ? request.Providers : null;
        
        Log.Information("Validating providers: {Providers}", 
            providers != null ? string.Join(", ", providers) : "all");

        var result = await validationService.ValidateProvidersAsync(providers, ct);

        return Results.Ok(new
        {
            results = result.Results,
            ok = result.Ok
        });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error validating providers");
        return Results.Problem("Error validating providers", statusCode: 500);
    }
})
.WithName("ValidateProviders")
.WithOpenApi();

// Log viewer endpoint - read recent log entries
apiGroup.MapGet("/logs", (HttpContext context, int? limit, string? level, string? search) =>
{
    try
    {
        var logsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "logs");
        if (!Directory.Exists(logsDirectory))
        {
            return Results.Ok(new { logs = Array.Empty<object>() });
        }

        var logFiles = Directory.GetFiles(logsDirectory, "aura-api-*.log")
            .OrderByDescending(f => new FileInfo(f).LastWriteTime)
            .Take(3) // Only read the 3 most recent files
            .ToList();

        var logs = new List<object>();
        var maxLines = limit ?? 500;

        foreach (var logFile in logFiles)
        {
            try
            {
                var lines = File.ReadLines(logFile).Reverse().Take(maxLines).Reverse();
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    // Parse log line: {Timestamp} [{Level}] [{CorrelationId}] {Message} ...
                    var parts = line.Split(new[] { '[', ']' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2)
                        continue;

                    var timestamp = parts[0].Trim();
                    var logLevel = parts.Length > 1 ? parts[1].Trim() : "INFO";
                    var correlationId = parts.Length > 2 ? parts[2].Trim() : "";
                    var message = parts.Length > 3 ? string.Join(" ", parts.Skip(3)) : line;

                    // Filter by level if specified
                    if (!string.IsNullOrEmpty(level) && !logLevel.Equals(level, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Filter by search term if specified
                    if (!string.IsNullOrEmpty(search) && !message.Contains(search, StringComparison.OrdinalIgnoreCase))
                        continue;

                    logs.Add(new
                    {
                        timestamp,
                        level = logLevel,
                        correlationId,
                        message,
                        file = Path.GetFileName(logFile)
                    });

                    if (logs.Count >= maxLines)
                        break;
                }
            }
            catch (IOException ex)
            {
                Log.Warning(ex, "Could not read log file: {File}", logFile);
            }

            if (logs.Count >= maxLines)
                break;
        }

        return Results.Ok(new { logs = logs.OrderByDescending(l => l.GetType().GetProperty("timestamp")?.GetValue(l)).Take(maxLines) });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error reading logs");
        return Results.Problem("Error reading logs", statusCode: 500);
    }
})
.WithName("GetLogs")
.WithOpenApi();

// Fallback to index.html for client-side routing (must be after all API routes)
if (Directory.Exists(wwwrootPath))
{
    app.MapFallbackToFile("index.html");
}

app.Run();

// DTOs
record PlanRequest(double TargetDurationMinutes, Pacing Pacing, Density Density, string Style);
record ScriptRequest(string Topic, string Audience, string Goal, string Tone, string Language, Aspect Aspect, double TargetDurationMinutes, Pacing Pacing, Density Density, string Style, string? ProviderTier);
record TtsRequest(List<LineDto> Lines, string VoiceName, double Rate, double Pitch, PauseStyle PauseStyle);
record LineDto(int SceneIndex, string Text, double StartSeconds, double DurationSeconds);
record ComposeRequest(string TimelineJson);
record RenderRequest(string TimelineJson, string PresetName);
record RenderJobDto(string Id, string Status, float Progress, string? OutputPath, DateTime CreatedAt);
record ApplyProfileRequest(string ProfileName);
record ApiKeysRequest(string? OpenAiKey, string? ElevenLabsKey, string? PexelsKey, string? StabilityAiKey);
record ProviderPathsRequest(string? StableDiffusionUrl, string? OllamaUrl, string? FfmpegPath, string? FfprobePath, string? OutputDirectory);
record ProviderTestRequest(string? Url, string? Path);
record RecommendationsRequestDto(
    string Topic,
    string? Audience,
    string? Goal,
    string? Tone,
    string? Language,
    Aspect? Aspect,
    double TargetDurationMinutes,
    Pacing? Pacing,
    Density? Density,
    string? Style,
    string? AudiencePersona,
    ConstraintsDto? Constraints);
record ConstraintsDto(
    int? MaxSceneCount,
    int? MinSceneCount,
    double? MaxBRollPercentage,
    int? MaxReadingLevel);

// Make Program accessible for integration tests
public partial class Program { }
record AssetSearchRequest(string Provider, string Query, int Count, string? ApiKey = null, string? LocalDirectory = null);
record AssetGenerateRequest(
    string Prompt, 
    int? SceneIndex = null,
    string? Model = null, 
    int? Steps = null, 
    double? CfgScale = null, 
    int? Seed = null, 
    int? Width = null, 
    int? Height = null, 
    string? Style = null, 
    string? SamplerName = null, 
    Aspect? Aspect = null,
    string[]? Keywords = null,
    string? StableDiffusionUrl = null,
    bool BypassHardwareChecks = false); // Allow users to bypass GPU/VRAM restrictions
record CaptionsRequest(List<LineDto> Lines, string Format = "SRT", string? OutputPath = null);
record ValidateProvidersRequest(string[]? Providers);
