namespace NpmCdn.Storage;

public interface IStorageProvider
{
    /// <summary>
    /// Checks if a file exists in the storage provider for a given package and version.
    /// </summary>
    Task<bool> FileExistsAsync(string packageName, string version, string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a read stream for the requested file. Returns null if not found.
    /// Implicitly updates the "last accessed" timestamp for the package version.
    /// </summary>
    Task<Stream?> ReadFileAsync(string packageName, string version, string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a file stream to the storage provider for a given package and version.
    /// </summary>
    Task WriteFileAsync(string packageName, string version, string filePath, Stream content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the last accessed timestamp for a package version.
    /// </summary>
    Task TouchPackageAccessTimeAsync(string packageName, string version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes all files and metadata associated with a specific package version.
    /// </summary>
    Task DeletePackageVersionAsync(string packageName, string version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a list of package versions that haven't been accessed since the given cutoff date.
    /// </summary>
    Task<IEnumerable<(string PackageName, string Version)>> GetStalePackagesAsync(DateTime cutoffDate, CancellationToken cancellationToken = default);
}