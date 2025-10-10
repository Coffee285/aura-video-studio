using Serilog.Context;

namespace Aura.Api.Middleware;

/// <summary>
/// Middleware that ensures each request has a correlation ID for tracking across logs.
/// If X-Correlation-ID header is not present, generates a new GUID.
/// Adds the correlation ID to the response headers and enriches all logs during the request.
/// </summary>
public class CorrelationIdMiddleware
{
    private const string CorrelationIdHeader = "X-Correlation-ID";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Get correlation ID from request header or generate a new one
        var correlationId = context.Request.Headers[CorrelationIdHeader].FirstOrDefault()
                           ?? Guid.NewGuid().ToString();

        // Add correlation ID to response headers
        context.Response.Headers.Append(CorrelationIdHeader, correlationId);

        // Push correlation ID to Serilog context for all logs in this request
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}
