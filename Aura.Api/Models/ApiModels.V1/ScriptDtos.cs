using System;
using System.Collections.Generic;

namespace Aura.Api.Models.ApiModels.V1;

/// <summary>
/// Request to generate a new script
/// </summary>
public record GenerateScriptRequest
{
    /// <summary>
    /// Video topic
    /// </summary>
    public string Topic { get; init; } = string.Empty;

    /// <summary>
    /// Target audience (optional)
    /// </summary>
    public string? Audience { get; init; }

    /// <summary>
    /// Video goal (optional)
    /// </summary>
    public string? Goal { get; init; }

    /// <summary>
    /// Content tone
    /// </summary>
    public string Tone { get; init; } = "Conversational";

    /// <summary>
    /// Language code (e.g., "en", "es", "fr")
    /// </summary>
    public string Language { get; init; } = "en";

    /// <summary>
    /// Aspect ratio (e.g., "16:9", "9:16")
    /// </summary>
    public string Aspect { get; init; } = "16:9";

    /// <summary>
    /// Target duration in seconds
    /// </summary>
    public int TargetDurationSeconds { get; init; } = 60;

    /// <summary>
    /// Pacing style
    /// </summary>
    public string Pacing { get; init; } = "Conversational";

    /// <summary>
    /// Content density
    /// </summary>
    public string Density { get; init; } = "Balanced";

    /// <summary>
    /// Video style
    /// </summary>
    public string Style { get; init; } = "Modern";

    /// <summary>
    /// Preferred provider (optional, uses tier logic if not specified)
    /// </summary>
    public string? PreferredProvider { get; init; }

    /// <summary>
    /// Optional model override
    /// </summary>
    public string? ModelOverride { get; init; }

    /// <summary>
    /// Model name for Ollama streaming (alias for ModelOverride)
    /// </summary>
    public string? Model => ModelOverride;

    /// <summary>
    /// Advanced LLM parameters - Temperature (0.0-2.0, controls randomness)
    /// </summary>
    public double? Temperature { get; init; }

    /// <summary>
    /// Advanced LLM parameters - Top P (0.0-1.0, nucleus sampling)
    /// </summary>
    public double? TopP { get; init; }

    /// <summary>
    /// Advanced LLM parameters - Top K (0-100, limits sampling to top K tokens)
    /// </summary>
    public int? TopK { get; init; }

    /// <summary>
    /// Advanced LLM parameters - Max tokens to generate
    /// </summary>
    public int? MaxTokens { get; init; }

    /// <summary>
    /// Advanced LLM parameters - Frequency penalty (-2.0 to 2.0, reduces repetition, OpenAI/Azure only)
    /// </summary>
    public double? FrequencyPenalty { get; init; }

    /// <summary>
    /// Advanced LLM parameters - Presence penalty (-2.0 to 2.0, encourages new topics, OpenAI/Azure only)
    /// </summary>
    public double? PresencePenalty { get; init; }

    /// <summary>
    /// Advanced LLM parameters - Stop sequences (provider-specific)
    /// </summary>
    public List<string>? StopSequences { get; init; }

    /// <summary>
    /// RAG (Retrieval-Augmented Generation) configuration for grounding script generation
    /// with ingested documents. When enabled, relevant content from uploaded documents
    /// will be used to enhance the generated script.
    /// </summary>
    public RagConfigurationDto? RagConfiguration { get; init; }
}

/// <summary>
/// Response containing generated script
/// </summary>
public record GenerateScriptResponse
{
    /// <summary>
    /// Script ID for future reference
    /// </summary>
    public string ScriptId { get; init; } = string.Empty;

    /// <summary>
    /// Script title
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// List of scenes
    /// </summary>
    public List<ScriptSceneDto> Scenes { get; init; } = new();

    /// <summary>
    /// Total duration in seconds
    /// </summary>
    public double TotalDurationSeconds { get; init; }

    /// <summary>
    /// Metadata about generation
    /// </summary>
    public ScriptMetadataDto Metadata { get; init; } = new();

    /// <summary>
    /// Correlation ID
    /// </summary>
    public string CorrelationId { get; init; } = string.Empty;
}

/// <summary>
/// Scene data transfer object for script generation
/// </summary>
public record ScriptSceneDto
{
    /// <summary>
    /// Scene number (1-based)
    /// </summary>
    public int Number { get; init; }

    /// <summary>
    /// Narration text
    /// </summary>
    public string Narration { get; init; } = string.Empty;

    /// <summary>
    /// Visual prompt
    /// </summary>
    public string VisualPrompt { get; init; } = string.Empty;

    /// <summary>
    /// Duration in seconds
    /// </summary>
    public double DurationSeconds { get; init; }

    /// <summary>
    /// Transition type
    /// </summary>
    public string Transition { get; init; } = "Cut";
}

/// <summary>
/// Script metadata DTO
/// </summary>
public record ScriptMetadataDto
{
    /// <summary>
    /// When the script was generated
    /// </summary>
    public DateTime GeneratedAt { get; init; }

    /// <summary>
    /// Provider name
    /// </summary>
    public string ProviderName { get; init; } = string.Empty;

    /// <summary>
    /// Model used
    /// </summary>
    public string ModelUsed { get; init; } = string.Empty;

    /// <summary>
    /// Tokens consumed
    /// </summary>
    public int TokensUsed { get; init; }

    /// <summary>
    /// Estimated cost in USD
    /// </summary>
    public decimal EstimatedCost { get; init; }

    /// <summary>
    /// Provider tier
    /// </summary>
    public string Tier { get; init; } = string.Empty;

    /// <summary>
    /// Generation time in seconds
    /// </summary>
    public double GenerationTimeSeconds { get; init; }
}

/// <summary>
/// Request to update a scene
/// </summary>
public record UpdateSceneRequest
{
    /// <summary>
    /// Updated narration text
    /// </summary>
    public string? Narration { get; init; }

    /// <summary>
    /// Updated visual prompt
    /// </summary>
    public string? VisualPrompt { get; init; }

    /// <summary>
    /// Updated duration in seconds
    /// </summary>
    public double? DurationSeconds { get; init; }
}

/// <summary>
/// Request to regenerate script
/// </summary>
public record RegenerateScriptRequest
{
    /// <summary>
    /// Optional different provider to use
    /// </summary>
    public string? PreferredProvider { get; init; }

    /// <summary>
    /// Optional model override
    /// </summary>
    public string? ModelOverride { get; init; }
}

/// <summary>
/// Available provider information
/// </summary>
public record ProviderInfoDto
{
    /// <summary>
    /// Provider name
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Provider tier
    /// </summary>
    public string Tier { get; init; } = string.Empty;

    /// <summary>
    /// Whether provider is currently available
    /// </summary>
    public bool IsAvailable { get; init; }

    /// <summary>
    /// Whether provider requires internet
    /// </summary>
    public bool RequiresInternet { get; init; }

    /// <summary>
    /// Whether provider requires API key
    /// </summary>
    public bool RequiresApiKey { get; init; }

    /// <summary>
    /// List of capabilities
    /// </summary>
    public List<string> Capabilities { get; init; } = new();

    /// <summary>
    /// Default model
    /// </summary>
    public string DefaultModel { get; init; } = string.Empty;

    /// <summary>
    /// Estimated cost per 1K tokens
    /// </summary>
    public decimal EstimatedCostPer1KTokens { get; init; }

    /// <summary>
    /// Available models for this provider
    /// </summary>
    public List<string> AvailableModels { get; init; } = new();
}

/// <summary>
/// Request to enhance/improve a script with adjustments
/// </summary>
public record ScriptEnhancementRequest
{
    /// <summary>
    /// Enhancement goal (e.g., "Make more engaging", "Improve pacing")
    /// </summary>
    public string Goal { get; init; } = string.Empty;

    /// <summary>
    /// Tone adjustment (-1.0 to 1.0, 0 is neutral)
    /// </summary>
    public double? ToneAdjustment { get; init; }

    /// <summary>
    /// Pacing adjustment (-1.0 to 1.0, 0 is neutral)
    /// </summary>
    public double? PacingAdjustment { get; init; }

    /// <summary>
    /// Style preset to apply
    /// </summary>
    public string? StylePreset { get; init; }
}

/// <summary>
/// Request to reorder scenes
/// </summary>
public record ReorderScenesRequest
{
    /// <summary>
    /// New scene order (scene numbers)
    /// </summary>
    public List<int> SceneOrder { get; init; } = new();
}

/// <summary>
/// Request to merge scenes
/// </summary>
public record MergeScenesRequest
{
    /// <summary>
    /// Scene numbers to merge
    /// </summary>
    public List<int> SceneNumbers { get; init; } = new();

    /// <summary>
    /// Separator for merged narration
    /// </summary>
    public string Separator { get; init; } = " ";
}

/// <summary>
/// Request to split a scene
/// </summary>
public record SplitSceneRequest
{
    /// <summary>
    /// Position to split at (character index in narration)
    /// </summary>
    public int SplitPosition { get; init; }
}

/// <summary>
/// Script version DTO
/// </summary>
public record ScriptVersionDto
{
    /// <summary>
    /// Version ID
    /// </summary>
    public string VersionId { get; init; } = string.Empty;

    /// <summary>
    /// Version number
    /// </summary>
    public int VersionNumber { get; init; }

    /// <summary>
    /// When this version was created
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// Version notes/comment
    /// </summary>
    public string? Notes { get; init; }

    /// <summary>
    /// Script snapshot
    /// </summary>
    public GenerateScriptResponse Script { get; init; } = new();
}

/// <summary>
/// Response with version history
/// </summary>
public record ScriptVersionHistoryResponse
{
    /// <summary>
    /// List of versions
    /// </summary>
    public List<ScriptVersionDto> Versions { get; init; } = new();

    /// <summary>
    /// Current version ID
    /// </summary>
    public string CurrentVersionId { get; init; } = string.Empty;

    /// <summary>
    /// Correlation ID
    /// </summary>
    public string CorrelationId { get; init; } = string.Empty;
}

/// <summary>
/// Request to revert to a version
/// </summary>
public record RevertToVersionRequest
{
    /// <summary>
    /// Version ID to revert to
    /// </summary>
    public string VersionId { get; init; } = string.Empty;
}

/// <summary>
/// Request to regenerate a scene with context
/// </summary>
public record RegenerateSceneRequest
{
    /// <summary>
    /// Optional improvement goal
    /// </summary>
    public string? ImprovementGoal { get; init; }

    /// <summary>
    /// Whether to include context from surrounding scenes
    /// </summary>
    public bool IncludeContext { get; init; } = true;
}
