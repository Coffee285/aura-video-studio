using Xunit;
using Aura.Api.Middleware;
using Microsoft.AspNetCore.Http;
using System;
using System.Threading.Tasks;

namespace Aura.Tests;

/// <summary>
/// Tests for CorrelationIdMiddleware to ensure it correctly stamps requests with correlation IDs.
/// </summary>
public class CorrelationIdMiddlewareTests
{
    [Fact]
    public async Task Middleware_Should_AddCorrelationIdToResponse()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var wasCalled = false;
        RequestDelegate next = (HttpContext _) =>
        {
            wasCalled = true;
            return Task.CompletedTask;
        };
        var middleware = new CorrelationIdMiddleware(next);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(wasCalled);
        Assert.True(context.Response.Headers.ContainsKey("X-Correlation-ID"));
        Assert.False(string.IsNullOrEmpty(context.Response.Headers["X-Correlation-ID"]));
    }

    [Fact]
    public async Task Middleware_Should_UseExistingCorrelationIdFromRequest()
    {
        // Arrange
        var expectedCorrelationId = "test-correlation-123";
        var context = new DefaultHttpContext();
        context.Request.Headers.Append("X-Correlation-ID", expectedCorrelationId);
        
        RequestDelegate next = (HttpContext _) => Task.CompletedTask;
        var middleware = new CorrelationIdMiddleware(next);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(expectedCorrelationId, context.Response.Headers["X-Correlation-ID"]);
    }

    [Fact]
    public async Task Middleware_Should_GenerateNewCorrelationIdWhenNotProvided()
    {
        // Arrange
        var context = new DefaultHttpContext();
        RequestDelegate next = (HttpContext _) => Task.CompletedTask;
        var middleware = new CorrelationIdMiddleware(next);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        var correlationId = context.Response.Headers["X-Correlation-ID"].ToString();
        Assert.NotEmpty(correlationId);
        // Verify it's a valid GUID format
        Assert.True(Guid.TryParse(correlationId, out _));
    }

    [Fact]
    public async Task Middleware_Should_CallNextMiddleware()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var nextCalled = false;
        RequestDelegate next = (HttpContext _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };
        var middleware = new CorrelationIdMiddleware(next);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled);
    }
}
