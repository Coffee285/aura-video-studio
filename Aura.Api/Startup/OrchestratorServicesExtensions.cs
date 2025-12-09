using Aura.Core.Orchestrator;
using Aura.Core.Services.Generation;
using Aura.Core.Timeline;

namespace Aura.Api.Startup;

/// <summary>
/// Extension methods for registering orchestration services.
/// </summary>
public static class OrchestratorServicesExtensions
{
    /// <summary>
    /// Registers orchestration services for script generation, video composition, and resource management.
    /// </summary>
    public static IServiceCollection AddOrchestratorServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Script orchestrator with lazy provider creation
        services.AddSingleton<ScriptOrchestrator>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<ScriptOrchestrator>>();
            var mixer = sp.GetRequiredService<ProviderMixer>();
            var factory = sp.GetRequiredService<LlmProviderFactory>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var ollamaDetectionService = sp.GetRequiredService<Aura.Core.Services.Providers.OllamaDetectionService>();

            var providers = factory.CreateAvailableProviders(loggerFactory);

            return new ScriptOrchestrator(logger, loggerFactory, mixer, providers, ollamaDetectionService);
        });

        // Process management for video rendering
        services.AddSingleton<Aura.Core.Runtime.ProcessRegistry>();
        services.AddSingleton<Aura.Core.Runtime.ManagedProcessRunner>();

        // Smart orchestration services
        services.AddSingleton<ResourceMonitor>();
        services.AddSingleton<StrategySelector>();
        services.AddSingleton<VideoGenerationOrchestrator>();

        // Timeline builder
        services.AddSingleton<TimelineBuilder>();

        // Video orchestrator
        services.AddSingleton<VideoOrchestrator>();

        return services;
    }
}
