using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aura.Api.Models.ApiModels.V1;
using Aura.Api.Services;
using Aura.Core.Errors;
using Aura.Core.Models;
using Aura.Core.Models.Audio;
using Aura.Core.Models.Localization;
using Aura.Core.Models.Voice;
using Aura.Core.Orchestration;
using Aura.Core.Providers;
using Aura.Core.Services.Audio;
using Aura.Core.Services.Localization;
using Aura.Core.Captions;
using Aura.Providers.Tts.validators;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using ApiTranslateAndPlanSSMLRequest = Aura.Api.Models.ApiModels.V1.TranslateAndPlanSSMLRequest;
using CoreTranslateAndPlanSSMLRequest = Aura.Core.Services.Localization.TranslateAndPlanSSMLRequest;

namespace Aura.Api.Controllers;

/// <summary>
/// Advanced multi-language translation with cultural localization
/// </summary>
[ApiController]
[Route("api/localization")]
public class LocalizationController : ControllerBase
{
    private readonly ILogger<LocalizationController> _logger;
    private readonly ILlmProvider _llmProvider;
    private readonly LlmStageAdapter _stageAdapter;
    private readonly TranslationService _translationService;
    private readonly GlossaryManager _glossaryManager;
    private readonly ILoggerFactory _loggerFactory;
    private readonly List<ISSMLMapper> _ssmlMappers;
    private readonly int _requestTimeoutSeconds;
    private readonly int _llmTimeoutSeconds;
    private readonly ILocalizationService _localizationService;

    public LocalizationController(
        ILogger<LocalizationController> logger,
        ILlmProvider llmProvider,
        ILoggerFactory loggerFactory,
        IConfiguration configuration,
        LlmStageAdapter stageAdapter,
        TranslationService translationService,
        ILocalizationService localizationService,
        GlossaryManager glossaryManager,
        IEnumerable<ISSMLMapper> ssmlMappers)
    {
        _logger = logger;
        _llmProvider = llmProvider;
        _stageAdapter = stageAdapter;
        _translationService = translationService;
        _loggerFactory = loggerFactory;
        _localizationService = localizationService;
        _glossaryManager = glossaryManager;
        _ssmlMappers = ssmlMappers.ToList();

        // Load timeout configuration with defaults
        // Translation with local LLMs (Ollama) can take several minutes for longer texts or when models need to load
        // Default timeout increased to 180 seconds (3 minutes) to accommodate model loading and complex translations
        // Increase defaults to better accommodate first-run model loads on local LLMs (GPU warmup)
        _requestTimeoutSeconds = configuration.GetValue("Localization:RequestTimeoutSeconds", 180);
        _llmTimeoutSeconds = configuration.GetValue("Localization:LlmTimeoutSeconds", 420);
    }

    /// <summary>
    /// Translate script with cultural localization
    /// </summary>
    [HttpPost("translate")]
    [ProducesResponseType(typeof(TranslationResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status408RequestTimeout)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<TranslationResultDto>> TranslateScript(
        [FromBody] TranslateScriptRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Translation request: {Source} → {Target}, CorrelationId: {CorrelationId}",
            request.SourceLanguage, request.TargetLanguage, HttpContext.TraceIdentifier);

        // Validate language codes
        var sourceValidation = _localizationService.ValidateLanguageCode(request.SourceLanguage);
        if (!sourceValidation.IsValid && !sourceValidation.IsWarning)
        {
            _logger.LogWarning("Invalid source language code: {Code}, CorrelationId: {CorrelationId}",
                request.SourceLanguage, HttpContext.TraceIdentifier);
            return BadRequest(new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#INVALID_LANGUAGE",
                Title = "Invalid Source Language",
                Status = StatusCodes.Status400BadRequest,
                Detail = sourceValidation.Message,
                Extensions =
                {
                    ["correlationId"] = HttpContext.TraceIdentifier,
                    ["errorCode"] = sourceValidation.ErrorCode ?? "INVALID_LANGUAGE",
                    ["languageCode"] = request.SourceLanguage
                }
            });
        }

        var targetValidation = _localizationService.ValidateLanguageCode(request.TargetLanguage);
        if (!targetValidation.IsValid && !targetValidation.IsWarning)
        {
            _logger.LogWarning("Invalid target language code: {Code}, CorrelationId: {CorrelationId}",
                request.TargetLanguage, HttpContext.TraceIdentifier);
            return BadRequest(new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#INVALID_LANGUAGE",
                Title = "Invalid Target Language",
                Status = StatusCodes.Status400BadRequest,
                Detail = targetValidation.Message,
                Extensions =
                {
                    ["correlationId"] = HttpContext.TraceIdentifier,
                    ["errorCode"] = targetValidation.ErrorCode ?? "INVALID_LANGUAGE",
                    ["languageCode"] = request.TargetLanguage
                }
            });
        }

        // Validate text length
        var textLength = request.SourceText?.Length ?? 0;
        if (request.ScriptLines != null)
        {
            textLength += request.ScriptLines.Sum(l => l.Text?.Length ?? 0);
        }

        if (textLength == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#EMPTY_CONTENT",
                Title = "Empty Content",
                Status = StatusCodes.Status400BadRequest,
                Detail = "No text provided for translation. Please provide either sourceText or scriptLines.",
                Extensions =
                {
                    ["correlationId"] = HttpContext.TraceIdentifier,
                    ["errorCode"] = "EMPTY_CONTENT"
                }
            });
        }

        const int maxTextLength = 50000;
        if (textLength > maxTextLength)
        {
            return BadRequest(new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#TEXT_TOO_LONG",
                Title = "Text Too Long",
                Status = StatusCodes.Status400BadRequest,
                Detail = $"Text length ({textLength} characters) exceeds maximum allowed ({maxTextLength} characters). Please split into smaller batches.",
                Extensions =
                {
                    ["correlationId"] = HttpContext.TraceIdentifier,
                    ["errorCode"] = "TEXT_TOO_LONG",
                    ["textLength"] = textLength,
                    ["maxLength"] = maxTextLength
                }
            });
        }

        // Create a linked cancellation token with timeout
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_llmTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var translationRequest = MapToTranslationRequest(request);
            var result = await _localizationService.TranslateAsync(translationRequest, linkedCts.Token).ConfigureAwait(false);

            var dto = MapToTranslationResultDto(result);

            _logger.LogInformation("Translation completed in {Time:F2}s with quality {Quality:F1}",
                result.TranslationTimeSeconds, result.Quality.OverallScore);

            return Ok(dto);
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogError(ex, "Translation circuit breaker is open, CorrelationId: {CorrelationId}",
                HttpContext.TraceIdentifier);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#SERVICE_UNAVAILABLE",
                Title = "Translation Service Temporarily Unavailable",
                Status = StatusCodes.Status503ServiceUnavailable,
                Detail = "The translation service is experiencing issues and has been temporarily disabled. Please try again in a few minutes.",
                Extensions =
                {
                    ["correlationId"] = HttpContext.TraceIdentifier,
                    ["errorCode"] = "CIRCUIT_BREAKER_OPEN",
                    ["retryAfterSeconds"] = 30
                }
            });
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("does not support translation") ||
            ex.Message.Contains("No AI provider is configured") ||
            ex.Message.Contains("Ollama is not running") ||
            ex.Message.Contains("not available"))
        {
            _logger.LogWarning(ex, "Translation provider not available, CorrelationId: {CorrelationId}",
                HttpContext.TraceIdentifier);

            // Return 503 for provider availability issues with clear instructions
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#PROVIDER_NOT_AVAILABLE",
                Title = "AI Provider Not Available",
                Status = StatusCodes.Status503ServiceUnavailable,
                Detail = ex.Message,
                Extensions =
                {
                    ["correlationId"] = HttpContext.TraceIdentifier,
                    ["errorCode"] = "PROVIDER_NOT_AVAILABLE",
                    ["suggestions"] = new[] {
                        "Start Ollama: Run 'ollama serve' in a terminal",
                        "Install a model: Run 'ollama pull llama3.1'",
                        "Or configure another AI provider in Settings"
                    }
                }
            });
        }
        catch (ProviderException ex) when (ex.Message.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
                                            ex.Message.Contains("unavailable", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(ex, "LLM provider connection failed, CorrelationId: {CorrelationId}",
                HttpContext.TraceIdentifier);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#PROVIDER_UNAVAILABLE",
                Title = "AI Provider Unavailable",
                Status = StatusCodes.Status503ServiceUnavailable,
                Detail = $"Could not connect to AI provider: {ex.Message}",
                Extensions =
                {
                    ["correlationId"] = HttpContext.TraceIdentifier,
                    ["errorCode"] = "PROVIDER_UNAVAILABLE",
                    ["recommendation"] = "Verify Ollama is running: 'ollama list' should show available models"
                }
            });
        }
        catch (ProviderException ex)
        {
            _logger.LogError(ex, "LLM provider error during translation, CorrelationId: {CorrelationId}",
                HttpContext.TraceIdentifier);

            var statusCode = ex.IsTransient ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status500InternalServerError;
            return StatusCode(statusCode, new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#PROVIDER_ERROR",
                Title = "LLM Provider Error",
                Status = statusCode,
                Detail = ex.UserMessage ?? ex.Message,
                Extensions =
                {
                    ["correlationId"] = HttpContext.TraceIdentifier,
                    ["errorCode"] = ex.SpecificErrorCode,
                    ["providerName"] = ex.ProviderName,
                    ["isRetryable"] = ex.IsTransient,
                    ["suggestedActions"] = ex.SuggestedActions
                }
            });
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            _logger.LogWarning("Translation request timed out after {Timeout}s, CorrelationId: {CorrelationId}. " +
                "This may be due to GPU being under heavy load.",
                _llmTimeoutSeconds, HttpContext.TraceIdentifier);
            return StatusCode(StatusCodes.Status408RequestTimeout, new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#TIMEOUT",
                Title = "Request Timeout",
                Status = StatusCodes.Status408RequestTimeout,
                Detail = $"Translation request timed out after {_llmTimeoutSeconds} seconds. " +
                         $"This often happens when GPU is under heavy load (100% usage). " +
                         $"Please wait a few moments and try again with shorter text or check GPU availability.",
                Extensions =
                {
                    ["correlationId"] = HttpContext.TraceIdentifier,
                    ["errorCode"] = "TIMEOUT",
                    ["timeoutSeconds"] = _llmTimeoutSeconds,
                    ["isRetryable"] = true,
                    ["suggestion"] = "If using Ollama with GPU: Check GPU usage (nvidia-smi), wait for GPU to become available, then retry"
                }
            });
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Translation request was cancelled, CorrelationId: {CorrelationId}",
                HttpContext.TraceIdentifier);
            return StatusCode(499, new ProblemDetails
            {
                Title = "Request Cancelled",
                Status = 499,
                Detail = "The request was cancelled by the client.",
                Extensions = { ["correlationId"] = HttpContext.TraceIdentifier }
            });
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid translation request");
            return BadRequest(new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#INVALID_REQUEST",
                Title = "Invalid Translation Request",
                Status = StatusCodes.Status400BadRequest,
                Detail = ex.Message,
                Extensions =
                {
                    ["correlationId"] = HttpContext.TraceIdentifier,
                    ["errorCode"] = "INVALID_REQUEST"
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Translation failed");
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#INTERNAL_ERROR",
                Title = "Translation Failed",
                Status = StatusCodes.Status500InternalServerError,
                Detail = "An unexpected error occurred during translation. Please try again or contact support if the problem persists.",
                Extensions =
                {
                    ["correlationId"] = HttpContext.TraceIdentifier,
                    ["errorCode"] = "INTERNAL_ERROR",
                    ["isRetryable"] = true
                }
            });
        }
    }

    /// <summary>
    /// Simple text translation for frontend LocalizationPage
    /// Returns only the translated text without additional metadata
    /// </summary>
    [HttpPost("translate/simple")]
    [ProducesResponseType(typeof(SimpleTranslationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status408RequestTimeout)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<SimpleTranslationDto>> SimpleTranslate(
        [FromBody] SimpleTranslationRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Simple translation request: {Source} → {Target}, Text length: {Length}, CorrelationId: {CorrelationId}",
            request.SourceLanguage, request.TargetLanguage, request.SourceText?.Length ?? 0, HttpContext.TraceIdentifier);

        // Validate language codes
        var sourceValidation = _localizationService.ValidateLanguageCode(request.SourceLanguage);
        if (!sourceValidation.IsValid && !sourceValidation.IsWarning)
        {
            _logger.LogWarning("Invalid source language code: {Code}, CorrelationId: {CorrelationId}",
                request.SourceLanguage, HttpContext.TraceIdentifier);
            return BadRequest(new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#INVALID_LANGUAGE",
                Title = "Invalid Source Language",
                Status = StatusCodes.Status400BadRequest,
                Detail = sourceValidation.Message,
                Extensions =
                {
                    ["correlationId"] = HttpContext.TraceIdentifier,
                    ["errorCode"] = sourceValidation.ErrorCode ?? "INVALID_LANGUAGE",
                    ["languageCode"] = request.SourceLanguage
                }
            });
        }

        var targetValidation = _localizationService.ValidateLanguageCode(request.TargetLanguage);
        if (!targetValidation.IsValid && !targetValidation.IsWarning)
        {
            _logger.LogWarning("Invalid target language code: {Code}, CorrelationId: {CorrelationId}",
                request.TargetLanguage, HttpContext.TraceIdentifier);
            return BadRequest(new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#INVALID_LANGUAGE",
                Title = "Invalid Target Language",
                Status = StatusCodes.Status400BadRequest,
                Detail = targetValidation.Message,
                Extensions =
                {
                    ["correlationId"] = HttpContext.TraceIdentifier,
                    ["errorCode"] = targetValidation.ErrorCode ?? "INVALID_LANGUAGE",
                    ["languageCode"] = request.TargetLanguage
                }
            });
        }

        // Validate text is not empty
        if (string.IsNullOrWhiteSpace(request.SourceText))
        {
            return BadRequest(new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#EMPTY_CONTENT",
                Title = "Empty Content",
                Status = StatusCodes.Status400BadRequest,
                Detail = "No text provided for translation. Please provide sourceText.",
                Extensions =
                {
                    ["correlationId"] = HttpContext.TraceIdentifier,
                    ["errorCode"] = "EMPTY_CONTENT"
                }
            });
        }

        // Validate text length
        const int maxTextLength = 50000;
        if (request.SourceText.Length > maxTextLength)
        {
            return BadRequest(new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#TEXT_TOO_LONG",
                Title = "Text Too Long",
                Status = StatusCodes.Status400BadRequest,
                Detail = $"Text length ({request.SourceText.Length} characters) exceeds maximum allowed ({maxTextLength} characters). Please split into smaller batches.",
                Extensions =
                {
                    ["correlationId"] = HttpContext.TraceIdentifier,
                    ["errorCode"] = "TEXT_TOO_LONG",
                    ["textLength"] = request.SourceText.Length,
                    ["maxLength"] = maxTextLength
                }
            });
        }

        // Create a linked cancellation token with timeout
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_llmTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var translationRequest = new TranslationRequest
            {
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                SourceText = request.SourceText,
                ScriptLines = new List<ScriptLine>(),
                Options = new TranslationOptions(),
                Provider = request.Provider,
                ModelId = request.ModelId
            };

            _logger.LogInformation("Translation request with model selection: Provider={Provider}, ModelId={ModelId}, CorrelationId: {CorrelationId}",
                request.Provider ?? "(default)", request.ModelId ?? "(default)", HttpContext.TraceIdentifier);

            var result = await _localizationService.TranslateAsync(translationRequest, linkedCts.Token).ConfigureAwait(false);

            _logger.LogInformation("Simple translation completed in {Time:F2}s, CorrelationId: {CorrelationId}",
                result.TranslationTimeSeconds, HttpContext.TraceIdentifier);

            return Ok(new SimpleTranslationDto(result.TranslatedText));
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogError(ex, "Translation circuit breaker is open, CorrelationId: {CorrelationId}",
                HttpContext.TraceIdentifier);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#SERVICE_UNAVAILABLE",
                Title = "Translation Service Temporarily Unavailable",
                Status = StatusCodes.Status503ServiceUnavailable,
                Detail = "The translation service is experiencing issues and has been temporarily disabled. Please try again in a few minutes.",
                Extensions =
                {
                    ["correlationId"] = HttpContext.TraceIdentifier,
                    ["errorCode"] = "CIRCUIT_BREAKER_OPEN",
                    ["retryAfterSeconds"] = 30
                }
            });
        }
        catch (ProviderException ex)
        {
            _logger.LogError(ex, "LLM provider error during simple translation, CorrelationId: {CorrelationId}",
                HttpContext.TraceIdentifier);

            var statusCode = ex.IsTransient ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status500InternalServerError;
            return StatusCode(statusCode, new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#PROVIDER_ERROR",
                Title = "LLM Provider Error",
                Status = statusCode,
                Detail = ex.UserMessage ?? ex.Message,
                Extensions =
                {
                    ["correlationId"] = HttpContext.TraceIdentifier,
                    ["errorCode"] = ex.SpecificErrorCode,
                    ["providerName"] = ex.ProviderName,
                    ["isRetryable"] = ex.IsTransient,
                    ["suggestedActions"] = ex.SuggestedActions
                }
            });
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            _logger.LogWarning("Simple translation request timed out after {Timeout}s, CorrelationId: {CorrelationId}. " +
                "This may be due to GPU being under heavy load.",
                _llmTimeoutSeconds, HttpContext.TraceIdentifier);
            return StatusCode(StatusCodes.Status408RequestTimeout, new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#TIMEOUT",
                Title = "Request Timeout",
                Status = StatusCodes.Status408RequestTimeout,
                Detail = $"Translation request timed out after {_llmTimeoutSeconds} seconds. " +
                         $"This often happens when GPU is under heavy load (100% usage). " +
                         $"Please wait a few moments and try again with shorter text.",
                Extensions =
                {
                    ["correlationId"] = HttpContext.TraceIdentifier,
                    ["errorCode"] = "TIMEOUT",
                    ["timeoutSeconds"] = _llmTimeoutSeconds,
                    ["isRetryable"] = true,
                    ["suggestion"] = "If using Ollama with GPU: Check GPU usage (nvidia-smi), wait for GPU to become available, then retry"
                }
            });
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Simple translation request was cancelled, CorrelationId: {CorrelationId}",
                HttpContext.TraceIdentifier);
            return StatusCode(499, new ProblemDetails
            {
                Title = "Request Cancelled",
                Status = 499,
                Detail = "The request was cancelled by the client.",
                Extensions = { ["correlationId"] = HttpContext.TraceIdentifier }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Simple translation failed, CorrelationId: {CorrelationId}", HttpContext.TraceIdentifier);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#INTERNAL_ERROR",
                Title = "Translation Failed",
                Status = StatusCodes.Status500InternalServerError,
                Detail = "An unexpected error occurred during translation. Please try again or contact support if the problem persists.",
                Extensions =
                {
                    ["correlationId"] = HttpContext.TraceIdentifier,
                    ["errorCode"] = "INTERNAL_ERROR",
                    ["isRetryable"] = true
                }
            });
        }
    }

    /// <summary>
    /// Batch translate to multiple languages
    /// </summary>
    [HttpPost("translate/batch")]
    [ProducesResponseType(typeof(BatchTranslationResultDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<BatchTranslationResultDto>> BatchTranslate(
        [FromBody] BatchTranslateRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Batch translation request: {Source} → [{Targets}]",
            request.SourceLanguage, string.Join(", ", request.TargetLanguages));

        try
        {
            var batchRequest = MapToBatchTranslationRequest(request);
            var result = await _translationService.BatchTranslateAsync(batchRequest, null, cancellationToken).ConfigureAwait(false);

            var dto = MapToBatchTranslationResultDto(result);

            _logger.LogInformation("Batch translation completed: {Success}/{Total}",
                result.SuccessfulLanguages.Count, request.TargetLanguages.Count);

            return Ok(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch translation failed");
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Batch Translation Failed",
                Status = StatusCodes.Status500InternalServerError,
                Detail = ex.Message,
                Extensions = { ["correlationId"] = HttpContext.TraceIdentifier }
            });
        }
    }

    /// <summary>
    /// Analyze cultural appropriateness of content
    /// </summary>
    [HttpPost("analyze-culture")]
    [ProducesResponseType(typeof(CulturalAnalysisResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status408RequestTimeout)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CulturalAnalysisResultDto>> AnalyzeCulturalContent(
        [FromBody] Models.ApiModels.V1.CulturalAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Cultural analysis request: {Language}/{Region}, CorrelationId: {CorrelationId}",
            request.TargetLanguage, request.TargetRegion, HttpContext.TraceIdentifier);

        // Validate language code
        var languageValidation = _localizationService.ValidateLanguageCode(request.TargetLanguage);
        if (!languageValidation.IsValid && !languageValidation.IsWarning)
        {
            _logger.LogWarning("Invalid language code for cultural analysis: {Code}, CorrelationId: {CorrelationId}",
                request.TargetLanguage, HttpContext.TraceIdentifier);
            return BadRequest(new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#INVALID_LANGUAGE",
                Title = "Invalid Language Code",
                Status = StatusCodes.Status400BadRequest,
                Detail = languageValidation.Message,
                Extensions =
                {
                    ["correlationId"] = HttpContext.TraceIdentifier,
                    ["errorCode"] = languageValidation.ErrorCode ?? "INVALID_LANGUAGE",
                    ["languageCode"] = request.TargetLanguage
                }
            });
        }

        // Validate content is not empty
        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return BadRequest(new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#EMPTY_CONTENT",
                Title = "Empty Content",
                Status = StatusCodes.Status400BadRequest,
                Detail = "Content is required for cultural analysis.",
                Extensions =
                {
                    ["correlationId"] = HttpContext.TraceIdentifier,
                    ["errorCode"] = "EMPTY_CONTENT"
                }
            });
        }

        // Create a linked cancellation token with timeout
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_llmTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var analysisRequest = new Core.Models.Localization.CulturalAnalysisRequest
            {
                TargetLanguage = request.TargetLanguage,
                TargetRegion = request.TargetRegion,
                Content = request.Content,
                AudienceProfileId = request.AudienceProfileId,
                Provider = request.Provider,
                ModelId = request.ModelId
            };

            _logger.LogInformation("Cultural analysis request with model selection: Provider={Provider}, ModelId={ModelId}, CorrelationId: {CorrelationId}",
                request.Provider ?? "(default)", request.ModelId ?? "(default)", HttpContext.TraceIdentifier);

            var result = await _localizationService.AnalyzeCulturalContentAsync(analysisRequest, linkedCts.Token).ConfigureAwait(false);

            var dto = MapToCulturalAnalysisResultDto(result);

            _logger.LogInformation("Cultural analysis completed successfully: Score={Score}, Issues={IssueCount}, Recommendations={RecCount}, CorrelationId: {CorrelationId}",
                result.CulturalSensitivityScore, result.Issues?.Count ?? 0, result.Recommendations?.Count ?? 0, HttpContext.TraceIdentifier);

            return Ok(dto);
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogError(ex, "Cultural analysis circuit breaker is open, CorrelationId: {CorrelationId}",
                HttpContext.TraceIdentifier);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#SERVICE_UNAVAILABLE",
                Title = "Analysis Service Temporarily Unavailable",
                Status = StatusCodes.Status503ServiceUnavailable,
                Detail = "The cultural analysis service is experiencing issues. Please try again in a few minutes.",
                Extensions =
                {
                    ["correlationId"] = HttpContext.TraceIdentifier,
                    ["errorCode"] = "CIRCUIT_BREAKER_OPEN",
                    ["retryAfterSeconds"] = 30
                }
            });
        }
        catch (ProviderException ex)
        {
            _logger.LogError(ex, "LLM provider error during cultural analysis, CorrelationId: {CorrelationId}",
                HttpContext.TraceIdentifier);

            var statusCode = ex.IsTransient ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status500InternalServerError;
            return StatusCode(statusCode, new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#PROVIDER_ERROR",
                Title = "LLM Provider Error",
                Status = statusCode,
                Detail = ex.UserMessage ?? ex.Message,
                Extensions =
                {
                    ["correlationId"] = HttpContext.TraceIdentifier,
                    ["errorCode"] = ex.SpecificErrorCode,
                    ["providerName"] = ex.ProviderName,
                    ["isRetryable"] = ex.IsTransient,
                    ["suggestedActions"] = ex.SuggestedActions
                }
            });
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            _logger.LogWarning("Cultural analysis request timed out after {Timeout}s, CorrelationId: {CorrelationId}. " +
                "This may be due to GPU being under heavy load.",
                _llmTimeoutSeconds, HttpContext.TraceIdentifier);
            return StatusCode(StatusCodes.Status408RequestTimeout, new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#TIMEOUT",
                Title = "Request Timeout",
                Status = StatusCodes.Status408RequestTimeout,
                Detail = $"Cultural analysis request timed out after {_llmTimeoutSeconds} seconds. " +
                         $"This often happens when GPU is under heavy load. " +
                         $"Please try with shorter content or wait for GPU to become available.",
                Extensions =
                {
                    ["correlationId"] = HttpContext.TraceIdentifier,
                    ["errorCode"] = "TIMEOUT",
                    ["timeoutSeconds"] = _llmTimeoutSeconds,
                    ["isRetryable"] = true,
                    ["suggestion"] = "If using Ollama with GPU: Check GPU usage and wait for GPU to become available"
                }
            });
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Cultural analysis request was cancelled, CorrelationId: {CorrelationId}",
                HttpContext.TraceIdentifier);
            return StatusCode(499, new ProblemDetails
            {
                Title = "Request Cancelled",
                Status = 499,
                Detail = "The request was cancelled by the client.",
                Extensions = { ["correlationId"] = HttpContext.TraceIdentifier }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cultural analysis failed");
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Type = "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#INTERNAL_ERROR",
                Title = "Cultural Analysis Failed",
                Status = StatusCodes.Status500InternalServerError,
                Detail = "An unexpected error occurred during cultural analysis. Please try again or contact support.",
                Extensions =
                {
                    ["correlationId"] = HttpContext.TraceIdentifier,
                    ["errorCode"] = "INTERNAL_ERROR",
                    ["isRetryable"] = true
                }
            });
        }
    }

    /// <summary>
    /// Get list of supported languages
    /// </summary>
    [HttpGet("languages")]
    [ProducesResponseType(typeof(List<LanguageInfoDto>), StatusCodes.Status200OK)]
    public ActionResult<List<LanguageInfoDto>> GetSupportedLanguages()
    {
        var languages = LanguageRegistry.GetAllLanguages();
        var dtos = languages.Select(MapToLanguageInfoDto).ToList();

        _logger.LogInformation("Returned {Count} supported languages", dtos.Count);
        return Ok(dtos);
    }

    /// <summary>
    /// Check if translation is available and provider is healthy
    /// </summary>
    [HttpGet("health")]
    [ProducesResponseType(typeof(TranslationProviderHealthDto), StatusCodes.Status200OK)]
    public ActionResult<TranslationProviderHealthDto> CheckProviderHealth()
    {
        try
        {
            var capabilities = _llmProvider.GetCapabilities();

            return Ok(new TranslationProviderHealthDto(
                IsAvailable: true,
                ProviderName: capabilities.ProviderName,
                SupportsTranslation: capabilities.SupportsTranslation,
                IsLocalModel: capabilities.IsLocalModel,
                Limitations: capabilities.KnownLimitations,
                ErrorMessage: null
            ));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get provider capabilities, CorrelationId: {CorrelationId}",
                HttpContext.TraceIdentifier);

            return Ok(new TranslationProviderHealthDto(
                IsAvailable: false,
                ProviderName: "",
                SupportsTranslation: false,
                IsLocalModel: false,
                Limitations: null,
                ErrorMessage: ex.Message
            ));
        }
    }

    /// <summary>
    /// Get translation quality analytics (admin endpoint)
    /// Returns aggregated metrics about translation quality and provider usage.
    /// In production, this would integrate with a metrics storage system.
    /// </summary>
    [HttpGet("analytics/quality")]
    [ProducesResponseType(typeof(TranslationAnalyticsDto), StatusCodes.Status200OK)]
    public ActionResult<TranslationAnalyticsDto> GetQualityAnalytics([FromQuery] int lastNTranslations = 100)
    {
        _logger.LogInformation("Fetching translation quality analytics for last {Count} translations, CorrelationId: {CorrelationId}",
            lastNTranslations, HttpContext.TraceIdentifier);

        // Get current provider info for the breakdown
        var providerName = "Unknown";
        try
        {
            var capabilities = _llmProvider.GetCapabilities();
            providerName = capabilities.ProviderName;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not retrieve provider capabilities for analytics, CorrelationId: {CorrelationId}",
                HttpContext.TraceIdentifier);
        }

        // Return analytics structure showing what would be tracked in production
        // A real implementation would query from a metrics database
        return Ok(new TranslationAnalyticsDto(
            TotalTranslations: lastNTranslations,
            AverageQualityGrade: "Good",
            CommonIssues: new Dictionary<string, int>
            {
                ["Structured artifacts"] = 0,
                ["Unwanted prefixes"] = 0,
                ["Length ratio issues"] = 0
            },
            ProviderBreakdown: new Dictionary<string, int>
            {
                [providerName] = lastNTranslations
            },
            RecommendedActions: new List<string>
            {
                "Consider using llama3.1 or mistral models for best translation quality",
                "Enable automatic cleanup for JSON artifacts in Ollama responses"
            }
        ));
    }

    /// <summary>
    /// Get language information
    /// </summary>
    [HttpGet("languages/{languageCode}")]
    [ProducesResponseType(typeof(LanguageInfoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public ActionResult<LanguageInfoDto> GetLanguage(string languageCode)
    {
        var language = LanguageRegistry.GetLanguage(languageCode);

        if (language == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Language Not Found",
                Status = StatusCodes.Status404NotFound,
                Detail = $"Language {languageCode} is not supported",
                Extensions = { ["correlationId"] = HttpContext.TraceIdentifier }
            });
        }

        return Ok(MapToLanguageInfoDto(language));
    }

    /// <summary>
    /// Create new glossary
    /// </summary>
    [HttpPost("glossary")]
    [ProducesResponseType(typeof(ProjectGlossaryDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<ProjectGlossaryDto>> CreateGlossary(
        [FromBody] CreateGlossaryRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating glossary: {Name}", request.Name);

        try
        {
            var glossary = await _glossaryManager.CreateGlossaryAsync(
                request.Name,
                request.Description,
                cancellationToken).ConfigureAwait(false);

            var dto = MapToProjectGlossaryDto(glossary);

            return CreatedAtAction(
                nameof(GetGlossary),
                new { glossaryId = glossary.Id },
                dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create glossary");
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Glossary Creation Failed",
                Status = StatusCodes.Status500InternalServerError,
                Detail = ex.Message,
                Extensions = { ["correlationId"] = HttpContext.TraceIdentifier }
            });
        }
    }

    /// <summary>
    /// Get glossary by ID
    /// </summary>
    [HttpGet("glossary/{glossaryId}")]
    [ProducesResponseType(typeof(ProjectGlossaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectGlossaryDto>> GetGlossary(
        string glossaryId,
        CancellationToken cancellationToken)
    {
        var glossary = await _glossaryManager.GetGlossaryAsync(glossaryId, cancellationToken).ConfigureAwait(false);

        if (glossary == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Glossary Not Found",
                Status = StatusCodes.Status404NotFound,
                Detail = $"Glossary {glossaryId} not found",
                Extensions = { ["correlationId"] = HttpContext.TraceIdentifier }
            });
        }

        return Ok(MapToProjectGlossaryDto(glossary));
    }

    /// <summary>
    /// List all glossaries
    /// </summary>
    [HttpGet("glossary")]
    [ProducesResponseType(typeof(List<ProjectGlossaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ProjectGlossaryDto>>> ListGlossaries(
        CancellationToken cancellationToken)
    {
        var glossaries = await _glossaryManager.ListGlossariesAsync(cancellationToken).ConfigureAwait(false);
        var dtos = glossaries.Select(MapToProjectGlossaryDto).ToList();

        return Ok(dtos);
    }

    /// <summary>
    /// Add entry to glossary
    /// </summary>
    [HttpPost("glossary/{glossaryId}/entries")]
    [ProducesResponseType(typeof(GlossaryEntryDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<GlossaryEntryDto>> AddGlossaryEntry(
        string glossaryId,
        [FromBody] AddGlossaryEntryRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var entry = await _glossaryManager.AddEntryAsync(
                glossaryId,
                request.Term,
                request.Translations,
                request.Context,
                request.Industry,
                cancellationToken).ConfigureAwait(false);

            var dto = MapToGlossaryEntryDto(entry);

            return CreatedAtAction(
                nameof(GetGlossary),
                new { glossaryId },
                dto);
        }
        catch (ArgumentException ex)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Glossary Not Found",
                Status = StatusCodes.Status404NotFound,
                Detail = ex.Message,
                Extensions = { ["correlationId"] = HttpContext.TraceIdentifier }
            });
        }
    }

    /// <summary>
    /// Delete glossary
    /// </summary>
    [HttpDelete("glossary/{glossaryId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> DeleteGlossary(
        string glossaryId,
        CancellationToken cancellationToken)
    {
        await _glossaryManager.DeleteGlossaryAsync(glossaryId, cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    private TranslationRequest MapToTranslationRequest(TranslateScriptRequest request)
    {
        return new TranslationRequest
        {
            SourceLanguage = request.SourceLanguage,
            TargetLanguage = request.TargetLanguage,
            SourceText = request.SourceText ?? string.Empty,
            ScriptLines = request.ScriptLines?.Select(MapToScriptLine).ToList() ?? new(),
            CulturalContext = request.CulturalContext != null ? MapToCulturalContext(request.CulturalContext) : null,
            Options = request.Options != null ? MapToTranslationOptions(request.Options) : new(),
            Glossary = request.Glossary ?? new(),
            AudienceProfileId = request.AudienceProfileId
        };
    }

    private BatchTranslationRequest MapToBatchTranslationRequest(BatchTranslateRequest request)
    {
        return new BatchTranslationRequest
        {
            SourceLanguage = request.SourceLanguage,
            TargetLanguages = request.TargetLanguages,
            SourceText = request.SourceText ?? string.Empty,
            ScriptLines = request.ScriptLines?.Select(MapToScriptLine).ToList() ?? new(),
            CulturalContext = request.CulturalContext != null ? MapToCulturalContext(request.CulturalContext) : null,
            Options = request.Options != null ? MapToTranslationOptions(request.Options) : new(),
            Glossary = request.Glossary ?? new()
        };
    }

    private ScriptLine MapToScriptLine(ScriptLineDto dto)
    {
        return new ScriptLine(
            SceneIndex: 0,
            Text: dto.Text,
            Start: TimeSpan.FromSeconds(dto.StartSeconds),
            Duration: TimeSpan.FromSeconds(dto.DurationSeconds)
        );
    }

    private Core.Models.Localization.CulturalContext MapToCulturalContext(CulturalContextDto dto)
    {
        return new Core.Models.Localization.CulturalContext
        {
            TargetRegion = dto.TargetRegion,
            TargetFormality = Enum.Parse<FormalityLevel>(dto.TargetFormality, true),
            PreferredStyle = Enum.Parse<Aura.Core.Models.Audience.CommunicationStyle>(dto.PreferredStyle, true),
            Sensitivities = dto.Sensitivities,
            TabooTopics = dto.TabooTopics,
            ContentRating = Enum.Parse<AgeRating>(dto.ContentRating, true)
        };
    }

    private TranslationOptions MapToTranslationOptions(TranslationOptionsDto dto)
    {
        return new TranslationOptions
        {
            Mode = Enum.Parse<TranslationMode>(dto.Mode, true),
            EnableBackTranslation = dto.EnableBackTranslation,
            EnableQualityScoring = dto.EnableQualityScoring,
            AdjustTimings = dto.AdjustTimings,
            MaxTimingVariance = dto.MaxTimingVariance,
            PreserveNames = dto.PreserveNames,
            PreserveBrands = dto.PreserveBrands,
            AdaptMeasurements = dto.AdaptMeasurements,
            TranscreationContext = dto.TranscreationContext
        };
    }

    private TranslationResultDto MapToTranslationResultDto(TranslationResult result)
    {
        return new TranslationResultDto(
            result.SourceLanguage,
            result.TargetLanguage,
            result.SourceText,
            result.TranslatedText,
            result.TranslatedLines.Select(MapToTranslatedScriptLineDto).ToList(),
            MapToTranslationQualityDto(result.Quality),
            result.CulturalAdaptations.Select(MapToCulturalAdaptationDto).ToList(),
            MapToTimingAdjustmentDto(result.TimingAdjustment),
            result.VisualRecommendations.Select(MapToVisualLocalizationRecommendationDto).ToList(),
            result.TranslationTimeSeconds,
            result.Metrics != null ? MapToTranslationMetricsDto(result.Metrics) : null
        );
    }

    private TranslationMetricsDto MapToTranslationMetricsDto(TranslationMetrics metrics)
    {
        return new TranslationMetricsDto(
            metrics.LengthRatio,
            metrics.HasStructuredArtifacts,
            metrics.HasUnwantedPrefixes,
            metrics.CharacterCount,
            metrics.WordCount,
            metrics.TranslationTimeSeconds,
            metrics.ProviderUsed,
            metrics.ModelIdentifier,
            metrics.QualityIssues,
            metrics.Grade.ToString()
        );
    }

    private TranslatedScriptLineDto MapToTranslatedScriptLineDto(TranslatedScriptLine line)
    {
        return new TranslatedScriptLineDto(
            line.SceneIndex,
            line.SourceText,
            line.TranslatedText,
            line.OriginalStartSeconds,
            line.OriginalDurationSeconds,
            line.AdjustedStartSeconds,
            line.AdjustedDurationSeconds,
            line.TimingVariance,
            line.AdaptationNotes
        );
    }

    private TranslationQualityDto MapToTranslationQualityDto(TranslationQuality quality)
    {
        return new TranslationQualityDto(
            quality.OverallScore,
            quality.FluencyScore,
            quality.AccuracyScore,
            quality.CulturalAppropriatenessScore,
            quality.TerminologyConsistencyScore,
            quality.BackTranslationScore,
            quality.BackTranslatedText,
            quality.Issues.Select(i => new QualityIssueDto(
                i.Severity.ToString(),
                i.Category,
                i.Description,
                i.Suggestion,
                i.LineNumber
            )).ToList()
        );
    }

    private CulturalAdaptationDto MapToCulturalAdaptationDto(CulturalAdaptation adaptation)
    {
        return new CulturalAdaptationDto(
            adaptation.Category,
            adaptation.SourcePhrase,
            adaptation.AdaptedPhrase,
            adaptation.Reasoning,
            adaptation.LineNumber
        );
    }

    private TimingAdjustmentDto MapToTimingAdjustmentDto(TimingAdjustment adjustment)
    {
        return new TimingAdjustmentDto(
            adjustment.OriginalTotalDuration,
            adjustment.AdjustedTotalDuration,
            adjustment.ExpansionFactor,
            adjustment.RequiresCompression,
            adjustment.CompressionSuggestions,
            adjustment.Warnings.Select(w => new TimingWarningDto(
                w.Severity.ToString(),
                w.Message,
                w.LineNumber
            )).ToList()
        );
    }

    private VisualLocalizationRecommendationDto MapToVisualLocalizationRecommendationDto(
        VisualLocalizationRecommendation rec)
    {
        return new VisualLocalizationRecommendationDto(
            rec.ElementType.ToString(),
            rec.Description,
            rec.Recommendation,
            rec.Priority.ToString(),
            rec.SceneIndex
        );
    }

    private BatchTranslationResultDto MapToBatchTranslationResultDto(BatchTranslationResult result)
    {
        return new BatchTranslationResultDto(
            result.SourceLanguage,
            result.Translations.ToDictionary(
                kvp => kvp.Key,
                kvp => MapToTranslationResultDto(kvp.Value)
            ),
            result.SuccessfulLanguages,
            result.FailedLanguages,
            result.TotalTimeSeconds
        );
    }

    private CulturalAnalysisResultDto MapToCulturalAnalysisResultDto(CulturalAnalysisResult result)
    {
        return new CulturalAnalysisResultDto(
            result.TargetLanguage,
            result.TargetRegion,
            result.CulturalSensitivityScore,
            result.Issues.Select(i => new CulturalIssueDto(
                i.Severity.ToString(),
                i.Category,
                i.Issue,
                i.Context,
                i.Suggestion
            )).ToList(),
            result.Recommendations.Select(r => new CulturalRecommendationDto(
                r.Category,
                r.Recommendation,
                r.Reasoning,
                r.Priority.ToString()
            )).ToList()
        );
    }

    private LanguageInfoDto MapToLanguageInfoDto(LanguageInfo language)
    {
        return new LanguageInfoDto(
            language.Code,
            language.Name,
            language.NativeName,
            language.Region,
            language.IsRightToLeft,
            language.DefaultFormality.ToString(),
            language.TypicalExpansionFactor
        );
    }

    private ProjectGlossaryDto MapToProjectGlossaryDto(ProjectGlossary glossary)
    {
        return new ProjectGlossaryDto(
            glossary.Id,
            glossary.Name,
            glossary.Description,
            glossary.Entries.Select(MapToGlossaryEntryDto).ToList(),
            glossary.CreatedAt,
            glossary.UpdatedAt
        );
    }

    private GlossaryEntryDto MapToGlossaryEntryDto(GlossaryEntry entry)
    {
        return new GlossaryEntryDto(
            entry.Id,
            entry.Term,
            entry.Translations,
            entry.Context,
            entry.Industry
        );
    }

    /// <summary>
    /// Translate script and plan SSML with subtitle generation
    /// </summary>
    [HttpPost("translate-and-plan-ssml")]
    [ProducesResponseType(typeof(TranslatedSSMLResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TranslatedSSMLResultDto>> TranslateAndPlanSSML(
        [FromBody] ApiTranslateAndPlanSSMLRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Translate and plan SSML: {Source} → {Target}, Provider: {Provider}, CorrelationId: {CorrelationId}",
            request.SourceLanguage, request.TargetLanguage, request.TargetProvider, HttpContext.TraceIdentifier);

        try
        {
            var ssmlPlannerService = new SSMLPlannerService(
                _loggerFactory.CreateLogger<SSMLPlannerService>(),
                _ssmlMappers);

            var captionBuilder = new CaptionBuilder(
                _loggerFactory.CreateLogger<CaptionBuilder>());

            var integrationService = new TranslationIntegrationService(
                _loggerFactory.CreateLogger<TranslationIntegrationService>(),
                _translationService,
                ssmlPlannerService,
                captionBuilder);

            if (!TryParseVoiceProvider(request.TargetProvider, out var provider))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid Provider",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = $"Unknown TTS provider: {request.TargetProvider}",
                    Extensions = { ["correlationId"] = HttpContext.TraceIdentifier }
                });
            }

            var scriptLines = request.ScriptLines.Select(line => new ScriptLine(
                line.SceneIndex,
                line.Text,
                TimeSpan.FromSeconds(line.StartSeconds),
                TimeSpan.FromSeconds(line.DurationSeconds)
            )).ToList();

            var voiceSpec = new VoiceSpec(
                request.VoiceSpec.VoiceName,
                request.VoiceSpec.Rate,
                request.VoiceSpec.Pitch,
                Aura.Core.Models.PauseStyle.Natural
            );

            var subtitleFormat = Enum.Parse<SubtitleFormat>(
                request.SubtitleFormat, true);

            var integrationRequest = new CoreTranslateAndPlanSSMLRequest
            {
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                ScriptLines = scriptLines,
                TargetProvider = provider,
                VoiceSpec = voiceSpec,
                CulturalContext = request.CulturalContext != null ? MapToCulturalContext(request.CulturalContext) : null,
                TranslationOptions = request.TranslationOptions != null ? MapToTranslationOptions(request.TranslationOptions) : new(),
                Glossary = request.Glossary ?? new Dictionary<string, string>(),
                AudienceProfileId = request.AudienceProfileId,
                DurationTolerance = request.DurationTolerance,
                MaxFittingIterations = request.MaxFittingIterations,
                EnableAggressiveAdjustments = request.EnableAggressiveAdjustments,
                SubtitleFormat = subtitleFormat
            };

            var result = await integrationService.TranslateAndPlanSSMLAsync(
                integrationRequest, cancellationToken).ConfigureAwait(false);

            var dto = MapToTranslatedSSMLResultDto(result);

            _logger.LogInformation("Translation and SSML planning completed successfully");

            return Ok(dto);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid request parameters");
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Status = StatusCodes.Status400BadRequest,
                Detail = ex.Message,
                Extensions = { ["correlationId"] = HttpContext.TraceIdentifier }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Translation and SSML planning failed");
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Translation and SSML Planning Failed",
                Status = StatusCodes.Status500InternalServerError,
                Detail = ex.Message,
                Extensions = { ["correlationId"] = HttpContext.TraceIdentifier }
            });
        }
    }

    /// <summary>
    /// Get recommended voices for target language and provider
    /// </summary>
    [HttpPost("voice-recommendation")]
    [ProducesResponseType(typeof(VoiceRecommendationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public ActionResult<VoiceRecommendationDto> GetVoiceRecommendation(
        [FromBody] VoiceRecommendationRequest request)
    {
        _logger.LogInformation(
            "Voice recommendation request: {Language}, Provider: {Provider}",
            request.TargetLanguage, request.Provider);

        try
        {
            if (!TryParseVoiceProvider(request.Provider, out var provider))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid Provider",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = $"Unknown TTS provider: {request.Provider}",
                    Extensions = { ["correlationId"] = HttpContext.TraceIdentifier }
                });
            }

            var ssmlPlannerService = new SSMLPlannerService(
                _loggerFactory.CreateLogger<SSMLPlannerService>(),
                _ssmlMappers);

            var captionBuilder = new CaptionBuilder(
                _loggerFactory.CreateLogger<CaptionBuilder>());

            var integrationService = new TranslationIntegrationService(
                _loggerFactory.CreateLogger<TranslationIntegrationService>(),
                _translationService,
                ssmlPlannerService,
                captionBuilder);

            var recommendation = integrationService.GetRecommendedVoice(
                request.TargetLanguage,
                provider,
                request.PreferredGender,
                request.PreferredStyle);

            var dto = new VoiceRecommendationDto(
                recommendation.TargetLanguage,
                recommendation.Provider.ToString(),
                recommendation.IsRTL,
                recommendation.RecommendedVoices.Select(v => new RecommendedVoiceDto(
                    v.VoiceName,
                    v.Gender,
                    v.Style,
                    v.Quality
                )).ToList()
            );

            return Ok(dto);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid language or provider");
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Status = StatusCodes.Status400BadRequest,
                Detail = ex.Message,
                Extensions = { ["correlationId"] = HttpContext.TraceIdentifier }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Voice recommendation failed");
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Voice Recommendation Failed",
                Status = StatusCodes.Status500InternalServerError,
                Detail = ex.Message,
                Extensions = { ["correlationId"] = HttpContext.TraceIdentifier }
            });
        }
    }

    private TranslatedSSMLResultDto MapToTranslatedSSMLResultDto(
        TranslatedSSMLResult result)
    {
        return new TranslatedSSMLResultDto(
            MapToTranslationResultDto(result.Translation),
            MapToSSMLPlanningResultDto(result.SSMLPlanning),
            result.TranslatedScriptLines.Select(line => new LineDto(
                line.SceneIndex,
                line.Text,
                line.Start.TotalSeconds,
                line.Duration.TotalSeconds
            )).ToList(),
            new SubtitleOutputDto(
                result.Subtitles.Format.ToString(),
                result.Subtitles.Content,
                result.Subtitles.LineCount
            )
        );
    }

    private SSMLPlanningResultDto MapToSSMLPlanningResultDto(SSMLPlanningResult result)
    {
        return new SSMLPlanningResultDto(
            result.Segments.Select(s => new SSMLSegmentResultDto(
                s.SceneIndex,
                s.OriginalText,
                s.SsmlMarkup,
                s.EstimatedDurationMs,
                s.TargetDurationMs,
                s.DeviationPercent,
                new ProsodyAdjustmentsDto(
                    s.Adjustments.Rate,
                    s.Adjustments.Pitch,
                    s.Adjustments.Volume,
                    s.Adjustments.Pauses.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                    s.Adjustments.Emphasis.Select(e => new EmphasisSpanDto(
                        e.StartPosition,
                        e.Length,
                        e.Level.ToString()
                    )).ToList(),
                    s.Adjustments.Iterations
                ),
                s.TimingMarkers.Select(m => new TimingMarkerDto(
                    m.OffsetMs,
                    m.Name,
                    m.Metadata
                )).ToList()
            )).ToList(),
            new DurationFittingStatsDto(
                result.Stats.SegmentsAdjusted,
                result.Stats.AverageFitIterations,
                result.Stats.MaxFitIterations,
                result.Stats.WithinTolerancePercent,
                result.Stats.AverageDeviation,
                result.Stats.MaxDeviation,
                result.Stats.TargetDurationSeconds,
                result.Stats.ActualDurationSeconds
            ),
            result.Warnings.ToList(),
            result.PlanningDurationMs
        );
    }

    /// <summary>
    /// Validate voice availability for target language and provider
    /// </summary>
    [HttpPost("validate-voice")]
    [ProducesResponseType(typeof(VoiceValidationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public ActionResult<VoiceValidationDto> ValidateVoice(
        [FromBody] ValidateVoiceRequest request)
    {
        _logger.LogInformation(
            "Voice validation request: {Language}, Provider: {Provider}, Voice: {Voice}",
            request.TargetLanguage, request.Provider, request.VoiceName);

        try
        {
            if (!TryParseVoiceProvider(request.Provider, out var provider))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid Provider",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = $"Unknown TTS provider: {request.Provider}",
                    Extensions = { ["correlationId"] = HttpContext.TraceIdentifier }
                });
            }

            var registry = new Aura.Core.Services.Voice.VoiceProviderRegistry(
                _loggerFactory.CreateLogger<Aura.Core.Services.Voice.VoiceProviderRegistry>());

            var result = registry.ValidateVoice(provider.ToString(), request.TargetLanguage, request.VoiceName);

            var dto = new VoiceValidationDto(
                request.TargetLanguage,
                request.Provider,
                request.VoiceName,
                result.IsValid,
                result.ErrorMessage,
                result.MatchedVoice != null ? new VoiceInfoDto(
                    result.MatchedVoice.Name,
                    result.MatchedVoice.Id,
                    result.MatchedVoice.Gender.ToString(),
                    string.Empty,
                    result.MatchedVoice.VoiceType.ToString()
                ) : null,
                result.FallbackSuggestion != null ? new VoiceInfoDto(
                    result.FallbackSuggestion.Name,
                    result.FallbackSuggestion.Id,
                    result.FallbackSuggestion.Gender.ToString(),
                    string.Empty,
                    result.FallbackSuggestion.VoiceType.ToString()
                ) : null
            );

            return Ok(new { validation = dto });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Voice validation failed");
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Voice Validation Failed",
                Status = StatusCodes.Status500InternalServerError,
                Detail = ex.Message,
                Extensions = { ["correlationId"] = HttpContext.TraceIdentifier }
            });
        }
    }

    private bool TryParseVoiceProvider(string providerName, out VoiceProvider provider)
    {
        return Enum.TryParse<VoiceProvider>(providerName, true, out provider);
    }
}
