using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace Aura.Api.ErrorHandling;

/// <summary>
/// Standard error codes for Aura API.
/// E3xx range is for client errors (400 level).
/// E5xx range is for server errors (500 level).
/// </summary>
public static class ErrorCodes
{
    public const string InvalidEnumValue = "E303";
    public const string ValidationError = "E304";
    public const string NotFound = "E305";
    public const string InternalError = "E500";
    public const string ServiceUnavailable = "E503";
}

/// <summary>
/// Helper methods for creating standardized ProblemDetails responses with correlation IDs.
/// </summary>
public static class ProblemDetailsHelper
{
    /// <summary>
    /// Creates a ProblemDetails response with correlation ID and logs the error.
    /// </summary>
    public static IResult CreateProblemWithCorrelation(
        HttpContext context,
        Exception ex,
        string errorCode,
        string title,
        string detail,
        int statusCode,
        string? logMessage = null)
    {
        var correlationId = context.Response.Headers["X-Correlation-ID"].FirstOrDefault() ?? "unknown";
        
        // Log with correlation ID context
        Log.Error(ex, logMessage ?? title + " - CorrelationId: {CorrelationId}", correlationId);
        
        var problemDetails = new ProblemDetails
        {
            Type = $"https://docs.aura.studio/errors/{errorCode}",
            Title = title,
            Status = statusCode,
            Detail = detail,
            Extensions =
            {
                ["correlationId"] = correlationId,
                ["errorCode"] = errorCode
            }
        };
        
        return Results.Problem(
            detail: problemDetails.Detail,
            statusCode: problemDetails.Status,
            title: problemDetails.Title,
            type: problemDetails.Type,
            extensions: problemDetails.Extensions
        );
    }
    
    /// <summary>
    /// Creates a simple error response without exception.
    /// </summary>
    public static IResult CreateProblem(
        HttpContext context,
        string errorCode,
        string title,
        string detail,
        int statusCode)
    {
        var correlationId = context.Response.Headers["X-Correlation-ID"].FirstOrDefault() ?? "unknown";
        
        Log.Warning("{Title} - CorrelationId: {CorrelationId} - Detail: {Detail}", title, correlationId, detail);
        
        var problemDetails = new ProblemDetails
        {
            Type = $"https://docs.aura.studio/errors/{errorCode}",
            Title = title,
            Status = statusCode,
            Detail = detail,
            Extensions =
            {
                ["correlationId"] = correlationId,
                ["errorCode"] = errorCode
            }
        };
        
        return Results.Problem(
            detail: problemDetails.Detail,
            statusCode: problemDetails.Status,
            title: problemDetails.Title,
            type: problemDetails.Type,
            extensions: problemDetails.Extensions
        );
    }
}
