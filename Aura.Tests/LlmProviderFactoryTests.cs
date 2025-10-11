using System;
using System.Net.Http;
using Aura.Core.Configuration;
using Aura.Core.Orchestrator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aura.Tests;

/// <summary>
/// Integration tests for LlmProviderFactory to ensure it can create providers correctly
/// </summary>
public class LlmProviderFactoryTests
{
    [Fact]
    public void CreateAvailableProviders_Should_CreateRuleBasedProvider()
    {
        // Arrange
        var loggerFactory = NullLoggerFactory.Instance;
        var logger = NullLogger<LlmProviderFactory>.Instance;
        var httpClientFactory = new TestHttpClientFactory();
        var providerSettingsLogger = NullLogger<ProviderSettings>.Instance;
        var providerSettings = new ProviderSettings(providerSettingsLogger);
        
        var factory = new LlmProviderFactory(logger, httpClientFactory, providerSettings);

        // Act
        var providers = factory.CreateAvailableProviders(loggerFactory);

        // Assert
        Assert.NotNull(providers);
        Assert.True(providers.ContainsKey("RuleBased"), "RuleBased provider should be registered");
        Assert.NotNull(providers["RuleBased"]);
    }

    [Fact]
    public void CreateAvailableProviders_Should_NotThrowException()
    {
        // Arrange
        var loggerFactory = NullLoggerFactory.Instance;
        var logger = NullLogger<LlmProviderFactory>.Instance;
        var httpClientFactory = new TestHttpClientFactory();
        var providerSettingsLogger = NullLogger<ProviderSettings>.Instance;
        var providerSettings = new ProviderSettings(providerSettingsLogger);
        
        var factory = new LlmProviderFactory(logger, httpClientFactory, providerSettings);

        // Act & Assert - should not throw
        var providers = factory.CreateAvailableProviders(loggerFactory);
        Assert.NotNull(providers);
    }

    /// <summary>
    /// Simple test implementation of IHttpClientFactory
    /// </summary>
    private class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient();
        }
    }
}
