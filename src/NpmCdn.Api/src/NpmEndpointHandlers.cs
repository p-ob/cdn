using System.Diagnostics;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

using NpmCdn.NpmRegistry;
using NpmCdn.Storage;

namespace NpmCdn.Api;

public partial class NpmEndpointHandlers
{
    [LoggerMessage(LogLevel.Warning, "Could not resolve version {Version} for package {PackageName}")]
    public static partial void LogVersionResolutionFailed(ILogger logger, string version, string packageName);

    [LoggerMessage(LogLevel.Information, "Cache miss for {PackageName}@{ExactVersion}. Downloading tarball...")]
    public static partial void LogPackageCacheMiss(ILogger logger, string packageName, string exactVersion);

    [LoggerMessage(LogLevel.Error, "Failed to download tarball for {PackageName}@{ExactVersion}")]
    public static partial void LogTarballDownloadFailed(ILogger logger, string packageName, string exactVersion);

    [LoggerMessage(LogLevel.Warning, "File {FilePath} not found in {PackageName}@{ExactVersion} after extraction.")]
    public static partial void LogFileNotFoundAfterExtraction(ILogger logger, string filePath, string packageName, string exactVersion);

    [LoggerMessage(LogLevel.Error, "Error servicing request for {PackageName}@{Version}/{FilePath}")]
    public static partial void LogRequestServicingError(ILogger logger, Exception ex, string packageName, string version, string? filePath);

    private static readonly ActivitySource ActivitySource = new("NpmCdn.Api.Requests");

    public static async Task<IResult> HandlePackageSpecAsync(
        string packageSpec,
        [FromServices] INpmRegistryClient registryClient,
        [FromServices] INpmPackageDownloader packageDownloader,
        [FromServices] NpmPackageExtractor packageExtractor,
        [FromServices] IStorageProvider storageProvider,
        [FromServices] Microsoft.Extensions.Options.IOptions<NpmCdn.Api.Configuration.CacheOptions> cacheOptions,
        [FromServices] ILogger<Program> logger,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!TryParsePackageSpec(packageSpec, out var packageName, out var version, out var filePath))
        {
            return Results.BadRequest("Invalid package specification format.");
        }

        return await HandleNpmRequestAsync(
            packageName, version, filePath,
            registryClient, packageDownloader, packageExtractor, storageProvider, cacheOptions, logger,
            context, cancellationToken);
    }

    public static bool TryParsePackageSpec(string packageSpec, out string packageName, out string version, out string? filePath)
    {
        packageName = string.Empty;
        version = "latest";
        filePath = null;

        if (string.IsNullOrWhiteSpace(packageSpec))
        {
            return false;
        }

        int nameEndIndex = 0;
        if (packageSpec.StartsWith('@'))
        {
            var slashIndex = packageSpec.IndexOf('/');
            if (slashIndex == -1 || slashIndex == packageSpec.Length - 1)
            {
                return false;
            }

            var nextAt = packageSpec.IndexOf('@', slashIndex + 1);
            var nextSlash = packageSpec.IndexOf('/', slashIndex + 1);

            if (nextAt != -1 && (nextSlash == -1 || nextAt < nextSlash))
            {
                nameEndIndex = nextAt;
            }
            else if (nextSlash != -1)
            {
                nameEndIndex = nextSlash;
            }
            else
            {
                nameEndIndex = packageSpec.Length;
            }
        }
        else
        {
            var nextAt = packageSpec.IndexOf('@');
            var nextSlash = packageSpec.IndexOf('/');

            if (nextAt != -1 && (nextSlash == -1 || nextAt < nextSlash))
            {
                nameEndIndex = nextAt;
            }
            else if (nextSlash != -1)
            {
                nameEndIndex = nextSlash;
            }
            else
            {
                nameEndIndex = packageSpec.Length;
            }
        }

        packageName = packageSpec.Substring(0, nameEndIndex);
        var remainder = packageSpec.Substring(nameEndIndex);

        if (remainder.StartsWith('@'))
        {
            var slashIndex = remainder.IndexOf('/');
            if (slashIndex != -1)
            {
                version = remainder.Substring(1, slashIndex - 1);
                if (slashIndex < remainder.Length - 1)
                {
                    filePath = remainder.Substring(slashIndex + 1);
                }
            }
            else
            {
                version = remainder.Substring(1);
            }
        }
        else if (remainder.StartsWith('/'))
        {
            if (remainder.Length > 1)
            {
                filePath = remainder.Substring(1);
            }
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            version = "latest";
        }

        return !string.IsNullOrWhiteSpace(packageName);
    }

    public static async Task<IResult> HandleNpmRequestAsync(
        string packageName,
        string version,
        string? filePath,
        [FromServices] INpmRegistryClient registryClient,
        [FromServices] INpmPackageDownloader packageDownloader,
        [FromServices] NpmPackageExtractor packageExtractor,
        [FromServices] IStorageProvider storageProvider,
        [FromServices] Microsoft.Extensions.Options.IOptions<NpmCdn.Api.Configuration.CacheOptions> cacheOptions,
        [FromServices] ILogger<Program> logger,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("HandleNpmRequest");
        activity?.SetTag("npm.package", packageName);
        activity?.SetTag("npm.requested_version", version);
        activity?.SetTag("npm.requested_filepath", filePath ?? "[root]");

        try
        {
            // 1. Version Resolution
            var exactVersion = await registryClient.ResolveVersionAsync(packageName, version, cancellationToken);
            if (string.IsNullOrEmpty(exactVersion))
            {
                LogVersionResolutionFailed(logger, version, packageName);
                return Results.NotFound($"Version {version} not found for package {packageName}.");
            }

            activity?.SetTag("npm.exact_version", exactVersion);

            // Calculate Cache-Control headers
            // If requested version matches exact version precisely, it's immutable (1 year by default). 
            // If it's a tag (like "latest" or "^4.0"), it's volatile (10 minutes by default).
            var isExactVersionRequested = version.Equals(exactVersion, StringComparison.OrdinalIgnoreCase);
            var maxAge = isExactVersionRequested ? cacheOptions.Value.ExactVersionMaxAge : cacheOptions.Value.DynamicVersionMaxAge;

            // 2. Entrypoint Resolution & Redirect logic
            var resolvedFilePath = filePath;
            var isEntrypointResolution = false;
            if (string.IsNullOrEmpty(resolvedFilePath))
            {
                resolvedFilePath = await registryClient.ResolveEntrypointAsync(packageName, exactVersion, cancellationToken);
                activity?.SetTag("npm.resolved_filepath", resolvedFilePath);
                isEntrypointResolution = true;
            }

            // HTTP 302 Redirect Optimization:
            // If the user requested a dynamic version (e.g. '@latest') OR they didn't specify a file path (needing entrypoint resolution),
            // we immediately redirect them to the canonical fully-qualified URL to leverage their browser cache and edge CDNs.
            if (!isExactVersionRequested || isEntrypointResolution)
            {
                var redirectCacheControl = new CacheControlHeaderValue
                {
                    Public = true,
                    MaxAge = maxAge
                };
                if (isExactVersionRequested && isEntrypointResolution)
                {
                    // If exactly versioned but missing path, the redirection target is deterministic forever.
                    redirectCacheControl.Extensions.Add(new NameValueHeaderValue("immutable"));
                }

                context.Response.GetTypedHeaders().CacheControl = redirectCacheControl;

                var targetUrl = $"/npm/{packageName}@{exactVersion}/{resolvedFilePath}";
                return Results.Redirect(targetUrl, permanent: false, preserveMethod: true);
            }

            // At this point, the request is fully pinned: /npm/package@exact/path.
            // Assign the immutable cache header for the actual file payload.
            var cacheControl = new CacheControlHeaderValue
            {
                Public = true,
                MaxAge = cacheOptions.Value.ExactVersionMaxAge
            };
            cacheControl.Extensions.Add(new NameValueHeaderValue("immutable"));
            context.Response.GetTypedHeaders().CacheControl = cacheControl;

            // 3. Storage Check
            var fileStream = await storageProvider.ReadFileAsync(packageName, exactVersion, resolvedFilePath, cancellationToken);

            if (fileStream != null)
            {
                // Cache Hit
                activity?.SetTag("cache.hit", true);
                return Results.Stream(fileStream, GetContentType(resolvedFilePath));
            }

            // 4. Cache Miss - Download & Extract
            activity?.SetTag("cache.hit", false);
            LogPackageCacheMiss(logger, packageName, exactVersion);

            var tarballStream = await packageDownloader.DownloadPackageTarballAsync(packageName, exactVersion, cancellationToken);
            if (tarballStream == null)
            {
                LogTarballDownloadFailed(logger, packageName, exactVersion);
                return Results.StatusCode(StatusCodes.Status502BadGateway);
            }

            await packageExtractor.ExtractTarballAsync(packageName, exactVersion, tarballStream, cancellationToken);

            // 5. Read again after extraction
            fileStream = await storageProvider.ReadFileAsync(packageName, exactVersion, resolvedFilePath, cancellationToken);
            if (fileStream != null)
            {
                return Results.Stream(fileStream, GetContentType(resolvedFilePath));
            }

            LogFileNotFoundAfterExtraction(logger, resolvedFilePath, packageName, exactVersion);
            return Results.NotFound($"File {resolvedFilePath} not found in package.");
        }
        catch (Exception ex)
        {
            LogRequestServicingError(logger, ex, packageName, version, filePath);
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    private static string GetContentType(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".js" => "application/javascript",
            ".mjs" => "application/javascript",
            ".css" => "text/css",
            ".html" => "text/html",
            ".json" => "application/json",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".eot" => "application/vnd.ms-fontobject",
            _ => "application/octet-stream"
        };
    }
}