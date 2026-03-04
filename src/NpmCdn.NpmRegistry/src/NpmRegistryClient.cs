using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Hybrid;

namespace NpmCdn.NpmRegistry;

public class NpmRegistryClient(HttpClient httpClient, HybridCache cache) : INpmRegistryClient
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly HybridCache _cache = cache;

    public NpmRegistryClient(HttpClient httpClient)
        : this(httpClient, null!) // Handled by older DI container rules potentially, but primary constructor handles proper injection ideally. For safety we just use primary.
    {
    }

    public async Task<string?> ResolveVersionAsync(string packageName, string versionOrTag, CancellationToken cancellationToken = default)
    {
        if (_httpClient.BaseAddress == null)
        {
            _httpClient.BaseAddress = new Uri("https://registry.npmjs.org");
        }

        var cacheKey = $"resolution:{packageName}@{versionOrTag}";
        return await _cache.GetOrCreateAsync(
            cacheKey,
            async cancel => await ResolveVersionInternalAsync(packageName, versionOrTag, cancel),
            cancellationToken: cancellationToken);
    }

    private async ValueTask<string?> ResolveVersionInternalAsync(string packageName, string versionOrTag, CancellationToken cancellationToken)
    {
        var url = $"/{Uri.EscapeDataString(packageName)}";
        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null; // Package not found or registry error
        }

        var packageData = await response.Content.ReadFromJsonAsync<NpmPackageMetadata>(cancellationToken: cancellationToken);
        if (packageData == null)
        {
            return null;
        }

        // 1. Check Dist Tags (e.g. "latest", "beta")
        if (packageData.DistTags != null && packageData.DistTags.TryGetValue(versionOrTag, out var literalTagVersion))
        {
            return literalTagVersion;
        }

        // 2. Check Exact Version
        if (packageData.Versions != null && packageData.Versions.ContainsKey(versionOrTag))
        {
            return versionOrTag;
        }

        // 3. Check Major/Minor Prefix (e.g. user asks for "4" -> resolve highest "4.x.x")
        if (packageData.Versions != null)
        {
            var partialMatch = packageData.Versions.Keys
                .Where(v => v.StartsWith($"{versionOrTag}."))
                .OrderByDescending(v =>
                {
                    // Basic semver sort by trying to parse it as System.Version 
                    // (Handles cases like "3.7.1", drops prerelease tags for simple sorting ideally but good enough for exact prefixed matches usually)
                    var cleanVersion = v;
                    var dashIndex = cleanVersion.IndexOf('-');
                    if (dashIndex > 0)
                    {
                        cleanVersion = cleanVersion.Substring(0, dashIndex);
                    }

                    return Version.TryParse(cleanVersion, out var parsed) ? parsed : new Version(0, 0);
                })
                .FirstOrDefault();

            if (partialMatch != null)
            {
                return partialMatch;
            }
        }

        return null;
    }

    public async Task<string> ResolveEntrypointAsync(string packageName, string exactVersion, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"entrypoint:{packageName}@{exactVersion}";
        return await _cache.GetOrCreateAsync(
            cacheKey,
            async cancel => await ResolveEntrypointInternalAsync(packageName, exactVersion, cancel),
            cancellationToken: cancellationToken);
    }

    private async ValueTask<string> ResolveEntrypointInternalAsync(string packageName, string exactVersion, CancellationToken cancellationToken)
    {
        var url = $"/{Uri.EscapeDataString(packageName)}/{exactVersion}";
        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return "index.js"; // Fallback if we can't fetch metadata
        }

        var packageData = await response.Content.ReadFromJsonAsync<NpmVersionMetadata>(cancellationToken: cancellationToken);
        if (packageData == null)
        {
            return "index.js";
        }

        // Priority 1: browser
        if (!string.IsNullOrWhiteSpace(packageData.Browser))
        {
            return packageData.Browser;
        }

        // Priority 2: main
        if (!string.IsNullOrWhiteSpace(packageData.Main))
        {
            return packageData.Main;
        }

        // Fallback
        return "index.js";
    }
}

public class NpmPackageMetadata
{
    [JsonPropertyName("dist-tags")]
    public Dictionary<string, string>? DistTags { get; set; }

    [JsonPropertyName("versions")]
    public Dictionary<string, object>? Versions { get; set; }
}

public class NpmVersionMetadata
{
    [JsonPropertyName("main")]
    public string? Main { get; set; }

    [JsonPropertyName("browser")]
    public string? Browser { get; set; }

    // Complex exports objects can be parsed here if needed in the future
}