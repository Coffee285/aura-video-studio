using System;
using System.Collections.Generic;
using Aura.Core.Errors;

namespace Aura.Core.Models;

/// <summary>
/// Represents the configuration and validation state of a provider
/// </summary>
public class ProviderState
{
    /// <summary>
    /// Unique identifier for the provider (e.g., "OpenAI", "ElevenLabs", "Ollama")
    /// </summary>
    public required string ProviderId { get; init; }

    /// <summary>
    /// Provider type category
    /// </summary>
    public required ProviderType Type { get; init; }

    /// <summary>
    /// Whether this provider is enabled for use
    /// </summary>
    public required bool Enabled { get; set; }

    /// <summary>
    /// Whether credentials (API key or other) are configured
    /// </summary>
    public required bool CredentialsConfigured { get; set; }

    /// <summary>
    /// Current validation status of the provider
    /// </summary>
    public required ProviderValidationStatus ValidationStatus { get; set; }

    /// <summary>
    /// When the provider was last validated (null if never validated)
    /// </summary>
    public DateTimeOffset? LastValidationAt { get; set; }

    /// <summary>
    /// Error code from last validation (null if no error)
    /// </summary>
    public string? LastErrorCode { get; set; }

    /// <summary>
    /// Error message from last validation (null if no error)
    /// </summary>
    public string? LastErrorMessage { get; set; }

    /// <summary>
    /// Additional metadata about the provider
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>
    /// Priority order for fallback chains (lower number = higher priority)
    /// </summary>
    public int Priority { get; set; }
}

/// <summary>
/// Validation status for a provider
/// </summary>
public enum ProviderValidationStatus
{
    /// <summary>
    /// Provider has not been validated yet
    /// </summary>
    Unknown,

    /// <summary>
    /// Provider validation succeeded
    /// </summary>
    Valid,

    /// <summary>
    /// Provider validation failed (credentials invalid)
    /// </summary>
    Invalid,

    /// <summary>
    /// Provider validation encountered an error (network, service down, etc.)
    /// </summary>
    Error,

    /// <summary>
    /// Provider is not configured (no credentials provided)
    /// </summary>
    NotConfigured
}
