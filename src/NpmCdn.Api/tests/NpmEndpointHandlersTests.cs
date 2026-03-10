using System.Text;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Moq;

using NpmCdn.Api.Configuration;
using NpmCdn.NpmRegistry;
using NpmCdn.Storage;

namespace NpmCdn.Api.Tests;

public class NpmEndpointHandlersTests
{
    private Mock<INpmRegistryClient>? _registryMock;
    private Mock<INpmPackageDownloader>? _downloaderMock;
    private Mock<IStorageProvider>? _storageMock;
    private Mock<ILogger<Program>>? _loggerMock;
    private NpmPackageExtractor? _extractor;
    private IOptions<CacheOptions>? _cacheOptions;

    [Before(Test)]
    public void Setup()
    {
        _registryMock = new Mock<INpmRegistryClient>();
        _downloaderMock = new Mock<INpmPackageDownloader>();
        _storageMock = new Mock<IStorageProvider>();
        _extractor = new NpmPackageExtractor(_storageMock.Object);
        _loggerMock = new Mock<ILogger<Program>>();

        var options = new CacheOptions
        {
            ExactVersionMaxAge = TimeSpan.FromDays(365),
            DynamicVersionMaxAge = TimeSpan.FromMinutes(10)
        };
        _cacheOptions = Options.Create(options);
    }

    [Test]
    public async Task HandleNpmRequestAsync_ExactVersion_ReturnsImmutableCacheControl()
    {
        // Arrange
        _registryMock!.Setup(x => x.ResolveVersionAsync("jquery", "3.7.1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("3.7.1");

        _storageMock!.Setup(x => x.ReadFileAsync("jquery", "3.7.1", "dist/jquery.js", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(Encoding.UTF8.GetBytes("console.log('jquery');")));

        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddLogging()
                .AddRouting()
                .BuildServiceProvider()
        };

        // Act
        var result = await NpmEndpointHandlers.HandleNpmRequestAsync(
            "jquery", "3.7.1", "dist/jquery.js",
            _registryMock.Object, _downloaderMock!.Object, _extractor!, _storageMock.Object, _cacheOptions!, _loggerMock!.Object,
            httpContext, CancellationToken.None);

        // Assert
        var cacheControl = httpContext.Response.GetTypedHeaders().CacheControl;
        await Assert.That(cacheControl).IsNotNull();
        await Assert.That(cacheControl!.Public).IsTrue();
        await Assert.That(cacheControl.MaxAge).IsEqualTo(TimeSpan.FromDays(365));
        await Assert.That(cacheControl.Extensions.Any(e => e.Name == "immutable")).IsTrue();
    }

    [Test]
    public async Task HandleNpmRequestAsync_TagVersion_ReturnsTenMinuteCacheControl()
    {
        // Arrange
        _registryMock!.Setup(x => x.ResolveVersionAsync("jquery", "latest", It.IsAny<CancellationToken>()))
            .ReturnsAsync("3.7.1");

        _storageMock!.Setup(x => x.ReadFileAsync("jquery", "3.7.1", "dist/jquery.js", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(Encoding.UTF8.GetBytes("console.log('jquery');")));

        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddLogging()
                .AddRouting()
                .BuildServiceProvider()
        };

        // Act
        var result = await NpmEndpointHandlers.HandleNpmRequestAsync(
            "jquery", "latest", "dist/jquery.js",
            _registryMock.Object, _downloaderMock!.Object, _extractor!, _storageMock.Object, _cacheOptions!, _loggerMock!.Object,
            httpContext, CancellationToken.None);

        // Assert
        // In this test, because it's a dynamic tag and we HAVE a filepath, it ACTUALLY returns a Redirect now!
        await Assert.That(result).IsNotNull();
        var redirectResult = result as Microsoft.AspNetCore.Http.HttpResults.RedirectHttpResult;
        await Assert.That(redirectResult).IsNotNull();
        await Assert.That(redirectResult!.Url).IsEqualTo("/npm/jquery@3.7.1/dist/jquery.js");
        await Assert.That(redirectResult.Permanent).IsFalse();

        var cacheControl = httpContext.Response.GetTypedHeaders().CacheControl;
        await Assert.That(cacheControl).IsNotNull();
        await Assert.That(cacheControl!.MaxAge).IsEqualTo(TimeSpan.FromMinutes(10));
        await Assert.That(cacheControl.Extensions.Any(e => e.Name == "immutable")).IsFalse();
    }

    [Test]
    public async Task HandleNpmRequestAsync_TagVersion_ReturnsRedirect()
    {
        // Arrange
        _registryMock!.Setup(x => x.ResolveVersionAsync("jquery", "latest", It.IsAny<CancellationToken>()))
            .ReturnsAsync("3.7.1");

        var httpContext = new DefaultHttpContext();

        // Act
        var result = await NpmEndpointHandlers.HandleNpmRequestAsync(
            "jquery", "latest", "dist/jquery.js",
            _registryMock.Object, _downloaderMock!.Object, _extractor!, _storageMock!.Object, _cacheOptions!, _loggerMock!.Object,
            httpContext, CancellationToken.None);

        // Assert
        // We expect it to yield an IResult that is a RedirectHttpResult
        await Assert.That(result).IsNotNull();
        var redirectResult = result as Microsoft.AspNetCore.Http.HttpResults.RedirectHttpResult;
        await Assert.That(redirectResult).IsNotNull();
        await Assert.That(redirectResult!.Url).IsEqualTo("/npm/jquery@3.7.1/dist/jquery.js");
        await Assert.That(redirectResult.Permanent).IsFalse();

        var cacheControl = httpContext.Response.GetTypedHeaders().CacheControl;
        await Assert.That(cacheControl).IsNotNull();
        await Assert.That(cacheControl!.MaxAge).IsEqualTo(TimeSpan.FromMinutes(10));
    }

    [Test]
    public async Task HandleNpmRequestAsync_ExactVersion_NoPath_ReturnsRedirect()
    {
        // Arrange
        _registryMock!.Setup(x => x.ResolveVersionAsync("jquery", "3.7.1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("3.7.1");

        _registryMock!.Setup(x => x.ResolveEntrypointAsync("jquery", "3.7.1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("dist/jquery.js");

        var httpContext = new DefaultHttpContext();

        // Act
        var result = await NpmEndpointHandlers.HandleNpmRequestAsync(
            "jquery", "3.7.1", null, // null path triggers entrypoint resolution and redirect
            _registryMock.Object, _downloaderMock!.Object, _extractor!, _storageMock!.Object, _cacheOptions!, _loggerMock!.Object,
            httpContext, CancellationToken.None);

        // Assert
        // We expect it to yield an IResult that is a RedirectHttpResult
        await Assert.That(result).IsNotNull();
        var redirectResult = result as Microsoft.AspNetCore.Http.HttpResults.RedirectHttpResult;
        await Assert.That(redirectResult).IsNotNull();
        await Assert.That(redirectResult!.Url).IsEqualTo("/npm/jquery@3.7.1/dist/jquery.js");
        await Assert.That(redirectResult.Permanent).IsFalse();

        var cacheControl = httpContext.Response.GetTypedHeaders().CacheControl;
        await Assert.That(cacheControl).IsNotNull();
        await Assert.That(cacheControl!.MaxAge).IsEqualTo(TimeSpan.FromDays(365));
        await Assert.That(cacheControl.Extensions.Any(e => e.Name == "immutable")).IsTrue();
    }


    [Test]
    public async Task HandleNpmRequestAsync_ResolvesCorrectContentType()
    {
        var testCases = new Dictionary<string, string>
        {
            { "index.js", "text/javascript" },
            { "index.cjs", "text/javascript" },
            { "index.mjs", "text/javascript" },
            { "style.css", "text/css" },
            { "data.json", "application/json" },
            { "font.woff2", "font/woff2" },
            { "chunk.js.map", "application/json" },
            { "unknown.xyz", "application/octet-stream" }
        };

        foreach (var (fileName, expectedMime) in testCases)
        {
            // Arrange
            _registryMock!.Setup(x => x.ResolveVersionAsync("pkg", "1.0.0", It.IsAny<CancellationToken>()))
                .ReturnsAsync("1.0.0");

            _storageMock!.Setup(x => x.ReadFileAsync("pkg", "1.0.0", fileName, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MemoryStream("data"u8.ToArray()));

            var httpContext = new DefaultHttpContext
            {
                RequestServices = new ServiceCollection()
                    .AddLogging()
                    .AddRouting()
                    .BuildServiceProvider()
            };

            // Act
            var result = await NpmEndpointHandlers.HandleNpmRequestAsync(
                "pkg", "1.0.0", fileName,
                _registryMock.Object, _downloaderMock!.Object, _extractor!, _storageMock.Object, _cacheOptions!, _loggerMock!.Object,
                httpContext, CancellationToken.None);

            // Assert
            await Assert.That(result).IsNotNull();
            var streamResult = result as Microsoft.AspNetCore.Http.HttpResults.FileStreamHttpResult;
            await Assert.That(streamResult).IsNotNull();
            await Assert.That(streamResult!.ContentType).IsEqualTo(expectedMime);
        }
    }
}