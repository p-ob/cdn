namespace NpmCdn.NpmRegistry;

public interface INpmPackageDownloader
{
    /// <summary>
    /// Downloads the complete .tgz tarball for the exact package version.
    /// The caller is responsible for disposing the returned Stream.
    /// </summary>
    /// <param name="packageName">The name of the npm package (e.g., "jquery").</param>
    /// <param name="exactVersion">The exact resolved version (e.g., "3.7.1").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A gzip-compressed tarball stream.</returns>
    Task<Stream?> DownloadPackageTarballAsync(string packageName, string exactVersion, CancellationToken cancellationToken = default);
}