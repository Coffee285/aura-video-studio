using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Aura.Core.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aura.Api.Middleware;

public class FirstRunMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<FirstRunMiddleware> _logger;

    public FirstRunMiddleware(RequestDelegate next, ILogger<FirstRunMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        // Allow setup API endpoints, health/diagnostic endpoints, onboarding page, and essential resources
        // Critical: These endpoints must be accessible during first-run wizard to prevent UI deadlock
        if (path.StartsWith("/api/setup", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/settings/first-run", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/preflight", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/probes", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/downloads", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/dependencies", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/apikeys", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/keys", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/providers", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/health", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/healthz", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/capabilities", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/jobs", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/system", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/ffmpeg", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/ideation", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/offline-providers", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/visuals", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/tts", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/wizard-projects", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/templates", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/template-management", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/scripts", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/localization", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/models", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/ollama", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/onboarding", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/setup", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/assets", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/_next", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".map", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".woff", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Check if wizard is completed using database
        using var scope = context.RequestServices.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuraDbContext>();

        try
        {
            var userId = "default";
            var userSetup = await dbContext.UserSetups
                .Where(s => s.UserId == userId)
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);

            var setupCompleted = userSetup?.Completed ?? false;

            // Fallback to file-based check for backward compatibility
            if (!setupCompleted)
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var configPath = Path.Combine(localAppData, "Aura", "config.json");
                setupCompleted = File.Exists(configPath);

                // If file exists but database doesn't have it, sync to database
                if (setupCompleted)
                {
                    if (userSetup == null)
                    {
                        userSetup = new UserSetupEntity
                        {
                            UserId = userId,
                            Completed = true,
                            CompletedAt = DateTime.UtcNow,
                            Version = "1.0.0"
                        };
                        dbContext.UserSetups.Add(userSetup);
                    }
                    else
                    {
                        userSetup.Completed = true;
                        userSetup.CompletedAt = DateTime.UtcNow;
                    }

                    await dbContext.SaveChangesAsync().ConfigureAwait(false);
                }
            }

            if (!setupCompleted)
            {
                _logger.LogInformation("Setup not completed, access restricted for path: {Path}", path);

                // If it's an API call, return 428 Precondition Required
                if (path.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = 428;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        error = "Setup not completed",
                        message = "Please complete the first-run wizard before using the application",
                        redirectTo = "/onboarding"
                    }).ConfigureAwait(false);
                    return;
                }

                // For page requests, let the SPA router handle the redirect
                await _next(context).ConfigureAwait(false);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking first-run status, allowing request to proceed");
            // On error, allow the request to proceed to avoid blocking the app
        }

        await _next(context).ConfigureAwait(false);
    }
}

/// <summary>
/// Extension methods for registering the FirstRunMiddleware
/// </summary>
public static class FirstRunMiddlewareExtensions
{
    public static IApplicationBuilder UseFirstRunCheck(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<FirstRunMiddleware>();
    }
}
