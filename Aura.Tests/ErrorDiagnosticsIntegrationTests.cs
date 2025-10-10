using Xunit;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Threading.Tasks;

namespace Aura.Tests;

/// <summary>
/// Integration tests for error logging with correlation IDs.
/// </summary>
public class ErrorDiagnosticsIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ErrorDiagnosticsIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HealthCheckEndpoint_Should_IncludeCorrelationId()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/healthz");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Correlation-ID"));
        var correlationId = response.Headers.GetValues("X-Correlation-ID").FirstOrDefault();
        Assert.False(string.IsNullOrEmpty(correlationId));
    }

    [Fact]
    public async Task HealthCheckEndpoint_Should_UseProvidedCorrelationId()
    {
        // Arrange
        var client = _factory.CreateClient();
        var expectedCorrelationId = "test-correlation-456";
        client.DefaultRequestHeaders.Add("X-Correlation-ID", expectedCorrelationId);

        // Act
        var response = await client.GetAsync("/api/healthz");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var actualCorrelationId = response.Headers.GetValues("X-Correlation-ID").FirstOrDefault();
        Assert.Equal(expectedCorrelationId, actualCorrelationId);
    }

    [Fact]
    public async Task LogsEndpoint_Should_ReturnLogData()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/logs?limit=10");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Correlation-ID"));
    }

    [Fact]
    public async Task LogsEndpoint_Should_SupportLevelFilter()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/logs?level=ERR&limit=5");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task LogsEndpoint_Should_SupportSearchFilter()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/logs?search=Error&limit=20");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
