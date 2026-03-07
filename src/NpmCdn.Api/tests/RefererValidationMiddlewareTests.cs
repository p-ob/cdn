using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

using NpmCdn.Api.Configuration;
using NpmCdn.Api.Middleware;

namespace NpmCdn.Api.Tests;

public class RefererValidationMiddlewareTests
{
    private static IOptions<AllowedOriginsOptions> CreateOptions(params string[] origins) =>
        Options.Create(new AllowedOriginsOptions { Origins = origins });

    private static DefaultHttpContext CreateContext(string? origin = null, string? referer = null)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        if (origin is not null)
        {
            context.Request.Headers.Origin = origin;
        }
        if (referer is not null)
        {
            context.Request.Headers.Referer = referer;
        }
        return context;
    }

    [Test]
    public async Task InvokeAsync_NoOriginsConfigured_AllowsAllRequests()
    {
        // Arrange
        var middleware = new RefererValidationMiddleware(_ => Task.CompletedTask, CreateOptions());
        var context = CreateContext(origin: "https://random.com");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        await Assert.That(context.Response.StatusCode).IsEqualTo(200);
    }

    [Test]
    public async Task InvokeAsync_AllowedOriginHeader_AllowsRequest()
    {
        // Arrange
        var middleware = new RefererValidationMiddleware(_ => Task.CompletedTask, CreateOptions("https://mysite.com"));
        var context = CreateContext(origin: "https://mysite.com");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        await Assert.That(context.Response.StatusCode).IsEqualTo(200);
    }

    [Test]
    public async Task InvokeAsync_DisallowedOriginHeader_Returns403()
    {
        // Arrange
        var nextCalled = false;
        var middleware = new RefererValidationMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, CreateOptions("https://mysite.com"));
        var context = CreateContext(origin: "https://evil.com");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        await Assert.That(context.Response.StatusCode).IsEqualTo(403);
        await Assert.That(nextCalled).IsFalse();
    }

    [Test]
    public async Task InvokeAsync_AllowedRefererHeader_AllowsRequest()
    {
        // Arrange
        var middleware = new RefererValidationMiddleware(_ => Task.CompletedTask, CreateOptions("https://mysite.com"));
        var context = CreateContext(referer: "https://mysite.com/some/page");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        await Assert.That(context.Response.StatusCode).IsEqualTo(200);
    }

    [Test]
    public async Task InvokeAsync_DisallowedRefererHeader_Returns403()
    {
        // Arrange
        var nextCalled = false;
        var middleware = new RefererValidationMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, CreateOptions("https://mysite.com"));
        var context = CreateContext(referer: "https://evil.com/attack");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        await Assert.That(context.Response.StatusCode).IsEqualTo(403);
        await Assert.That(nextCalled).IsFalse();
    }

    [Test]
    public async Task InvokeAsync_NoOriginOrRefererHeader_AllowsRequest()
    {
        // Arrange – direct requests (curl, Postman, etc.) with no origin headers are allowed through
        var middleware = new RefererValidationMiddleware(_ => Task.CompletedTask, CreateOptions("https://mysite.com"));
        var context = CreateContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        await Assert.That(context.Response.StatusCode).IsEqualTo(200);
    }

    [Test]
    public async Task InvokeAsync_OriginHeaderTakesPrecedenceOverReferer()
    {
        // Arrange – Origin is allowed, Referer would be blocked if checked alone
        var middleware = new RefererValidationMiddleware(_ => Task.CompletedTask, CreateOptions("https://mysite.com"));
        var context = CreateContext(origin: "https://mysite.com", referer: "https://evil.com/page");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        await Assert.That(context.Response.StatusCode).IsEqualTo(200);
    }

    [Test]
    public async Task InvokeAsync_OriginHeaderCaseInsensitive_AllowsRequest()
    {
        // Arrange
        var middleware = new RefererValidationMiddleware(_ => Task.CompletedTask, CreateOptions("https://MySite.COM"));
        var context = CreateContext(origin: "https://mysite.com");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        await Assert.That(context.Response.StatusCode).IsEqualTo(200);
    }

    [Test]
    public async Task GetRequestOrigin_OriginHeader_ReturnsOrigin()
    {
        var context = CreateContext(origin: "https://mysite.com");
        var result = RefererValidationMiddleware.GetRequestOrigin(context.Request);
        await Assert.That(result).IsEqualTo("https://mysite.com");
    }

    [Test]
    public async Task GetRequestOrigin_OriginHeaderWithTrailingSlash_ReturnsNormalized()
    {
        var context = CreateContext(origin: "https://mysite.com/");
        var result = RefererValidationMiddleware.GetRequestOrigin(context.Request);
        await Assert.That(result).IsEqualTo("https://mysite.com");
    }

    [Test]
    public async Task GetRequestOrigin_RefererHeader_ReturnsOriginPart()
    {
        var context = CreateContext(referer: "https://mysite.com/some/deep/page?q=1");
        var result = RefererValidationMiddleware.GetRequestOrigin(context.Request);
        await Assert.That(result).IsEqualTo("https://mysite.com");
    }

    [Test]
    public async Task GetRequestOrigin_NoHeaders_ReturnsNull()
    {
        var context = CreateContext();
        var result = RefererValidationMiddleware.GetRequestOrigin(context.Request);
        await Assert.That(result).IsNull();
    }
}