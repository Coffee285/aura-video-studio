using Serilog;
using Serilog.Events;

namespace Aura.Api.Logging;

/// <summary>
/// Centralized Serilog configuration with correlation ID and structured logging support.
/// </summary>
public static class SerilogConfig
{
    /// <summary>
    /// Configures Serilog with console and file sinks, including correlation ID enrichment.
    /// </summary>
    /// <param name="configuration">Application configuration</param>
    /// <returns>Configured Serilog logger</returns>
    public static Serilog.ILogger ConfigureLogger(Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        return new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext() // Required for correlation ID enrichment
            .Enrich.WithProperty("Application", "Aura.Api")
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"
            )
            .WriteTo.File(
                path: "logs/aura-api-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{CorrelationId}] {Message:lj} {Properties:j}{NewLine}{Exception}"
            )
            .CreateLogger();
    }
}
