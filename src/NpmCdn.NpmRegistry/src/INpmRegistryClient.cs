namespace NpmCdn.NpmRegistry;

public interface INpmRegistryClient
{
    /// <summary>
    /// Resolves a package version from a literal semver string, major/minor string, or dist-tag.
    /// </summary>
    /// <param name="packageName">The name of the npm package (e.g., "jquery", "@angular/core").</param>
    /// <param name="versionOrTag">The semver, partial version, or dist-tag (e.g., "latest", "3", "3.7.1").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The exact resolved literal version (e.g., "3.7.1"), or null if not found.</returns>
    Task<string?> ResolveVersionAsync(string packageName, string versionOrTag, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the default entrypoint file path for a package version based on its package.json metadata (main, exports, browser).
    /// </summary>
    /// <param name="packageName">The name of the npm package (e.g., "jquery").</param>
    /// <param name="exactVersion">The exact resolved version (e.g., "3.7.1").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The relative file path (e.g., "dist/jquery.js"), or "index.js" as a fallback.</returns>
    Task<string> ResolveEntrypointAsync(string packageName, string exactVersion, CancellationToken cancellationToken = default);
}