using System.Text;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

using Moq;

using NpmCdn.Api.Configuration;
using NpmCdn.NpmRegistry;
using NpmCdn.Storage;

using TUnit.Assertions;
using TUnit.Core;

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
    public async Task TryParsePackageSpec_MatchesCorrectPatterns()
    {
        // Assert unscoped missing version
        await Assert.That(NpmEndpointHandlers.TryParsePackageSpec("jquery", out var pkg1, out var ver1, out var path1)).IsTrue();
        await Assert.That(pkg1).IsEqualTo("jquery");
        await Assert.That(ver1).IsEqualTo("latest");
        await Assert.That(path1).IsNull();

        // Assert scoped missing version missing path
        await Assert.That(NpmEndpointHandlers.TryParsePackageSpec("@zywave/zui-bundle", out var pkg2, out var ver2, out var path2)).IsTrue();
        await Assert.That(pkg2).IsEqualTo("@zywave/zui-bundle");
        await Assert.That(ver2).IsEqualTo("latest");
        await Assert.That(path2).IsNull();

        // Assert scoped version missing path
        await Assert.That(NpmEndpointHandlers.TryParsePackageSpec("@zywave/zui-bundle@1.2.3", out var pkg3, out var ver3, out var path3)).IsTrue();
        await Assert.That(pkg3).IsEqualTo("@zywave/zui-bundle");
        await Assert.That(ver3).IsEqualTo("1.2.3");
        await Assert.That(path3).IsNull();

        // Assert scoped version with path
        await Assert.That(NpmEndpointHandlers.TryParsePackageSpec("@zywave/zui-bundle@latest/dist/index.js", out var pkg4, out var ver4, out var path4)).IsTrue();
        await Assert.That(pkg4).IsEqualTo("@zywave/zui-bundle");
        await Assert.That(ver4).IsEqualTo("latest");
        await Assert.That(path4).IsEqualTo("dist/index.js");

        // Assert unscoped version with path
        await Assert.That(NpmEndpointHandlers.TryParsePackageSpec("react@18.2.0/index.js", out var pkg5, out var ver5, out var path5)).IsTrue();
        await Assert.That(pkg5).IsEqualTo("react");
        await Assert.That(ver5).IsEqualTo("18.2.0");
        await Assert.That(path5).IsEqualTo("index.js");

        // Assert unscoped missing version with path
        await Assert.That(NpmEndpointHandlers.TryParsePackageSpec("react/index.js", out var pkg6, out var ver6, out var path6)).IsTrue();
        await Assert.That(pkg6).IsEqualTo("react");
        await Assert.That(ver6).IsEqualTo("latest");
        await Assert.That(path6).IsEqualTo("index.js");

        // Assert scoped missing version with path
        await Assert.That(NpmEndpointHandlers.TryParsePackageSpec("@zywave/zui-bundle/dist/index.js", out var pkg7, out var ver7, out var path7)).IsTrue();
        await Assert.That(pkg7).IsEqualTo("@zywave/zui-bundle");
        await Assert.That(ver7).IsEqualTo("latest");
        await Assert.That(path7).IsEqualTo("dist/index.js");
    }
}