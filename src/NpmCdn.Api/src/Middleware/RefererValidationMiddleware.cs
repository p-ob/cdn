using Microsoft.Extensions.Options;

using NpmCdn.Api.Configuration;

namespace NpmCdn.Api.Middleware;

public class RefererValidationMiddleware(
    RequestDelegate next,
    IOptions<AllowedOriginsOptions> options)
{
    private readonly RequestDelegate _next = next;
    private readonly AllowedOriginsOptions _options = options.Value;

    public async Task InvokeAsync(HttpContext context)
    {
        if (_options.Origins.Length > 0)
        {
            var requestOrigin = GetRequestOrigin(context.Request);

            if (requestOrigin is not null &&
                !_options.Origins.Contains(requestOrigin, StringComparer.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Access denied: origin not allowed.");
                return;
            }
        }

        await _next(context);
    }

    /// <summary>
    /// Extracts the origin from the request's <c>Origin</c> or <c>Referer</c> header.
    /// Returns <see langword="null"/> if neither header is present.
    /// </summary>
    public static string? GetRequestOrigin(HttpRequest request)
    {
        var origin = request.Headers.Origin.FirstOrDefault();
        if (!string.IsNullOrEmpty(origin))
        {
            return origin.TrimEnd('/');
        }

        var referer = request.Headers.Referer.FirstOrDefault();
        if (!string.IsNullOrEmpty(referer) && Uri.TryCreate(referer, UriKind.Absolute, out var refererUri))
        {
            return refererUri.GetLeftPart(UriPartial.Authority);
        }

        return null;
    }
}
