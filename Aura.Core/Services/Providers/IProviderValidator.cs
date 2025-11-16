using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Aura.Core.Services.Providers;

/// <summary>
/// Interface for provider validation (new validation system)
/// </summary>
public interface IProviderValidator
{
    /// <summary>
    /// The provider ID this validator handles
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Validate the provider with the given credentials
    /// </summary>
    /// <param name="credentials">Provider-specific credentials</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Validation result</returns>
    Task<ProviderValidationResultV2> ValidateAsync(
        ProviderCredentials credentials,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Provider credentials container
/// </summary>
public class ProviderCredentials
{
    /// <summary>
    /// API key (if applicable)
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Base URL (for providers that support custom endpoints)
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Organization ID (for providers like OpenAI)
    /// </summary>
    public string? OrganizationId { get; set; }

    /// <summary>
    /// Project ID (for providers like OpenAI)
    /// </summary>
    public string? ProjectId { get; set; }

    /// <summary>
    /// Additional provider-specific settings
    /// </summary>
    public Dictionary<string, string> AdditionalSettings { get; set; } = new();
}

/// <summary>
/// Result of provider validation (V2 - new system)
/// </summary>
public class ProviderValidationResultV2
{
    /// <summary>
    /// Whether validation succeeded
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Error code (null if valid)
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// User-friendly error message (null if valid)
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// HTTP status code (if applicable)
    /// </summary>
    public int? HttpStatusCode { get; init; }

    /// <summary>
    /// Response time in milliseconds
    /// </summary>
    public long ResponseTimeMs { get; init; }

    /// <summary>
    /// Diagnostic information for troubleshooting
    /// </summary>
    public string? DiagnosticInfo { get; init; }
}
