using System.Diagnostics;

namespace NpmCdn.NpmRegistry;

public class NpmPackageDownloader : INpmPackageDownloader
{
    private static readonly ActivitySource ActivitySource = new("NpmCdn.NpmRegistry.Downloader");
    private readonly HttpClient _httpClient;

    public NpmPackageDownloader(HttpClient httpClient)
    {
        _httpClient = httpClient;
        if (_httpClient.BaseAddress == null)
        {
            _httpClient.BaseAddress = new Uri("https://registry.npmjs.org");
        }
    }

    public async Task<Stream?> DownloadPackageTarballAsync(string packageName, string exactVersion, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("DownloadPackageTarball");
        activity?.SetTag("npm.package", packageName);
        activity?.SetTag("npm.version", exactVersion);
        // Public NPM registry tarball URL format:
        // non-scoped: https://registry.npmjs.org/jquery/-/jquery-3.7.1.tgz
        // scoped:     https://registry.npmjs.org/@types/jquery/-/jquery-3.5.30.tgz

        string tarballName;

        if (packageName.StartsWith("@"))
        {
            // @types/is-window -> URL path: /@types/is-window/-/is-window-1.0.2.tgz
            var parts = packageName.Split('/');
            if (parts.Length == 2)
            {
                var scope = parts[0];       // "@types"
                var unscopedName = parts[1]; // "is-window"
                tarballName = $"{unscopedName}-{exactVersion}.tgz";
            }
            else
            {
                tarballName = $"{packageName.Replace("@", "").Replace("/", "-")}-{exactVersion}.tgz";
            }
        }
        else
        {
            tarballName = $"{packageName}-{exactVersion}.tgz";
        }

        // The registry path is e.g. /@types/is-window/-/is-window-1.0.2.tgz
        // The first segment of the path needs the slash to be passed literally to the proxy/registry, so we don't escape it.
        var url = $"/{packageName}/-/{tarballName}";

        activity?.SetTag("npm.url", url);

        var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        activity?.SetTag("http.status_code", (int)response.StatusCode);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Failed to download {url}. Status: {response.StatusCode}");
        }

        return await response.Content.ReadAsStreamAsync(cancellationToken);
    }
}