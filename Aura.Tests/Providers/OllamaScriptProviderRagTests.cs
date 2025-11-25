using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aura.Core.Interfaces;
using Aura.Core.Models;
using Aura.Core.Models.Generation;
using Aura.Core.Models.RAG;
using Aura.Core.Services.RAG;
using Aura.Providers.Llm;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace Aura.Tests.Providers;

/// <summary>
/// Tests for RAG integration in OllamaScriptProvider
/// </summary>
public class OllamaScriptProviderRagTests : IDisposable
{
    private readonly Mock<ILogger<OllamaScriptProvider>> _loggerMock;
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly Mock<RagContextBuilder> _ragContextBuilderMock;
    private const string BaseUrl = "http://127.0.0.1:11434";
    private const string Model = "llama3.1:8b-q4_k_m";

    public OllamaScriptProviderRagTests()
    {
        _loggerMock = new Mock<ILogger<OllamaScriptProvider>>();
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpMessageHandlerMock.Object);
        
        // RagContextBuilder has dependencies, so we create a proper mock
        _ragContextBuilderMock = new Mock<RagContextBuilder>(
            Mock.Of<ILogger<RagContextBuilder>>(),
            Mock.Of<VectorIndex>(),
            Mock.Of<EmbeddingService>());
    }

    [Fact]
    public async Task GenerateScriptAsync_WithRagEnabled_RetrievesContext()
    {
        // Arrange
        var request = CreateTestRequestWithRag(enabled: true);

        // Setup RAG context builder mock
        var ragContext = new RagContext
        {
            Query = "Test Topic",
            Chunks = new List<ContextChunk>
            {
                new ContextChunk
                {
                    Content = "Test context content from document",
                    Source = "test-document.pdf",
                    RelevanceScore = 0.9f,
                    CitationNumber = 1
                }
            },
            FormattedContext = "# Reference Material\n\nTest context content from document [Citation 1]",
            Citations = new List<Citation>
            {
                new Citation { Number = 1, Source = "test-document.pdf" }
            },
            TotalTokens = 100
        };

        _ragContextBuilderMock
            .Setup(r => r.BuildContextAsync(
                It.IsAny<string>(),
                It.IsAny<RagConfig>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ragContext);

        var provider = new OllamaScriptProvider(
            _loggerMock.Object,
            _httpClient,
            _ragContextBuilderMock.Object,
            BaseUrl,
            Model,
            maxRetries: 1,
            timeoutSeconds: 30);

        SetupSuccessfulOllamaResponses();

        // Act
        var result = await provider.GenerateScriptAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        _ragContextBuilderMock.Verify(
            r => r.BuildContextAsync(
                It.Is<string>(q => q == "Test Topic"),
                It.Is<RagConfig>(c => c.Enabled),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "RAG context builder should be called once when RAG is enabled");
    }

    [Fact]
    public async Task GenerateScriptAsync_WithRagDisabled_SkipsContextRetrieval()
    {
        // Arrange
        var request = CreateTestRequestWithRag(enabled: false);

        var provider = new OllamaScriptProvider(
            _loggerMock.Object,
            _httpClient,
            _ragContextBuilderMock.Object,
            BaseUrl,
            Model,
            maxRetries: 1,
            timeoutSeconds: 30);

        SetupSuccessfulOllamaResponses();

        // Act
        var result = await provider.GenerateScriptAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        _ragContextBuilderMock.Verify(
            r => r.BuildContextAsync(
                It.IsAny<string>(),
                It.IsAny<RagConfig>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "RAG context builder should not be called when RAG is disabled");
    }

    [Fact]
    public async Task GenerateScriptAsync_WithNoRagConfiguration_SkipsContextRetrieval()
    {
        // Arrange
        var request = CreateTestRequestWithoutRag();

        var provider = new OllamaScriptProvider(
            _loggerMock.Object,
            _httpClient,
            _ragContextBuilderMock.Object,
            BaseUrl,
            Model,
            maxRetries: 1,
            timeoutSeconds: 30);

        SetupSuccessfulOllamaResponses();

        // Act
        var result = await provider.GenerateScriptAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        _ragContextBuilderMock.Verify(
            r => r.BuildContextAsync(
                It.IsAny<string>(),
                It.IsAny<RagConfig>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "RAG context builder should not be called when no RagConfiguration is provided");
    }

    [Fact]
    public async Task GenerateScriptAsync_WithNullRagContextBuilder_SkipsContextRetrieval()
    {
        // Arrange
        var request = CreateTestRequestWithRag(enabled: true);

        // Create provider WITHOUT RagContextBuilder (null)
        var provider = new OllamaScriptProvider(
            _loggerMock.Object,
            _httpClient,
            ragContextBuilder: null, // No RAG context builder
            BaseUrl,
            Model,
            maxRetries: 1,
            timeoutSeconds: 30);

        SetupSuccessfulOllamaResponses();

        // Act
        var result = await provider.GenerateScriptAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        // No exception should be thrown - graceful handling when RAG is not configured
    }

    [Fact]
    public async Task GenerateScriptAsync_WhenRagFails_ContinuesWithoutContext()
    {
        // Arrange
        var request = CreateTestRequestWithRag(enabled: true);

        // Setup RAG to throw an exception
        _ragContextBuilderMock
            .Setup(r => r.BuildContextAsync(
                It.IsAny<string>(),
                It.IsAny<RagConfig>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("RAG service unavailable"));

        var provider = new OllamaScriptProvider(
            _loggerMock.Object,
            _httpClient,
            _ragContextBuilderMock.Object,
            BaseUrl,
            Model,
            maxRetries: 1,
            timeoutSeconds: 30);

        SetupSuccessfulOllamaResponses();

        // Act
        var result = await provider.GenerateScriptAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        // Should succeed even when RAG fails - graceful degradation
    }

    [Fact]
    public async Task GenerateScriptAsync_WithEmptyRagResults_ContinuesNormally()
    {
        // Arrange
        var request = CreateTestRequestWithRag(enabled: true);

        // Setup RAG to return empty context
        var emptyContext = new RagContext
        {
            Query = "Test Topic",
            Chunks = new List<ContextChunk>(),
            FormattedContext = string.Empty,
            Citations = new List<Citation>(),
            TotalTokens = 0
        };

        _ragContextBuilderMock
            .Setup(r => r.BuildContextAsync(
                It.IsAny<string>(),
                It.IsAny<RagConfig>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyContext);

        var provider = new OllamaScriptProvider(
            _loggerMock.Object,
            _httpClient,
            _ragContextBuilderMock.Object,
            BaseUrl,
            Model,
            maxRetries: 1,
            timeoutSeconds: 30);

        SetupSuccessfulOllamaResponses();

        // Act
        var result = await provider.GenerateScriptAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Scenes);
    }

    [Fact]
    public async Task GenerateScriptAsync_WithRagEnabled_PassesCorrectConfig()
    {
        // Arrange
        var ragConfiguration = new RagConfiguration(
            Enabled: true,
            TopK: 10,
            MinimumScore: 0.8f,
            MaxContextTokens: 3000,
            IncludeCitations: false,
            TightenClaims: true);

        var request = new ScriptGenerationRequest
        {
            Brief = new Brief
            {
                Topic = "Test Topic",
                Audience = "General",
                Goal = "Educate",
                Tone = "Professional",
                RagConfiguration = ragConfiguration
            },
            PlanSpec = new PlanSpec
            {
                TargetDuration = TimeSpan.FromSeconds(30),
                Style = "Educational",
                Pacing = "Medium"
            },
            CorrelationId = Guid.NewGuid().ToString()
        };

        RagConfig? capturedConfig = null;
        _ragContextBuilderMock
            .Setup(r => r.BuildContextAsync(
                It.IsAny<string>(),
                It.IsAny<RagConfig>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, RagConfig, CancellationToken>((q, c, t) => capturedConfig = c)
            .ReturnsAsync(new RagContext { Query = "Test", Chunks = new List<ContextChunk>() });

        var provider = new OllamaScriptProvider(
            _loggerMock.Object,
            _httpClient,
            _ragContextBuilderMock.Object,
            BaseUrl,
            Model,
            maxRetries: 1,
            timeoutSeconds: 30);

        SetupSuccessfulOllamaResponses();

        // Act
        await provider.GenerateScriptAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(capturedConfig);
        Assert.True(capturedConfig.Enabled);
        Assert.Equal(10, capturedConfig.TopK);
        Assert.Equal(0.8f, capturedConfig.MinimumScore);
        Assert.Equal(3000, capturedConfig.MaxContextTokens);
        Assert.False(capturedConfig.IncludeCitations);
    }

    private ScriptGenerationRequest CreateTestRequestWithRag(bool enabled)
    {
        return new ScriptGenerationRequest
        {
            Brief = new Brief
            {
                Topic = "Test Topic",
                Audience = "General",
                Goal = "Educate",
                Tone = "Professional",
                RagConfiguration = new RagConfiguration(
                    Enabled: enabled,
                    TopK: 5,
                    MinimumScore: 0.6f,
                    MaxContextTokens: 2000,
                    IncludeCitations: true,
                    TightenClaims: false)
            },
            PlanSpec = new PlanSpec
            {
                TargetDuration = TimeSpan.FromSeconds(30),
                Style = "Educational",
                Pacing = "Medium"
            },
            CorrelationId = Guid.NewGuid().ToString()
        };
    }

    private ScriptGenerationRequest CreateTestRequestWithoutRag()
    {
        return new ScriptGenerationRequest
        {
            Brief = new Brief
            {
                Topic = "Test Topic",
                Audience = "General",
                Goal = "Educate",
                Tone = "Professional"
            },
            PlanSpec = new PlanSpec
            {
                TargetDuration = TimeSpan.FromSeconds(30),
                Style = "Educational",
                Pacing = "Medium"
            },
            CorrelationId = Guid.NewGuid().ToString()
        };
    }

    private void SetupSuccessfulOllamaResponses()
    {
        var versionResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"version\":\"0.1.0\"}", Encoding.UTF8, "application/json")
        };

        var generateResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    response = "Scene 1: Introduction.\nScene 2: Main content.\nScene 3: Conclusion.",
                    done = true
                }),
                Encoding.UTF8,
                "application/json")
        };

        _httpMessageHandlerMock
            .Protected()
            .SetupSequence<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(versionResponse)
            .ReturnsAsync(generateResponse);
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
