using System.Collections.Concurrent;

namespace NpmCdn.Storage;

public class VolumeStorageProvider : IStorageProvider
{
    private readonly string _basePath;

    // Concurrency lock to prevent identical packages from being written over each other 
    // repeatedly during stampede requests.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public VolumeStorageProvider(string basePath)
    {
        _basePath = basePath ?? throw new ArgumentNullException(nameof(basePath));
        if (!Directory.Exists(_basePath))
        {
            Directory.CreateDirectory(_basePath);
        }
    }

    private string GetPackageDirectory(string packageName, string version)
    {
        // Prevent path traversal
        var safePackage = packageName.Replace("..", "").Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var safeVersion = version.Replace("..", "").Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        return Path.Combine(_basePath, "packages", safePackage, safeVersion);
    }

    private string GetPackageAccessFile(string packageName, string version)
    {
        return Path.Combine(GetPackageDirectory(packageName, version), ".last_accessed");
    }

    public Task<bool> FileExistsAsync(string packageName, string version, string filePath, CancellationToken cancellationToken = default)
    {
        var packageDir = GetPackageDirectory(packageName, version);
        var safeFilePath = filePath.Replace("..", "").Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.Combine(packageDir, safeFilePath);

        return Task.FromResult(File.Exists(fullPath));
    }

    public async Task<Stream?> ReadFileAsync(string packageName, string version, string filePath, CancellationToken cancellationToken = default)
    {
        var packageDir = GetPackageDirectory(packageName, version);
        var safeFilePath = filePath.Replace("..", "").Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.Combine(packageDir, safeFilePath);

        if (!File.Exists(fullPath))
        {
            return null;
        }

        // Update last accessed time asynchronously while opening stream
        await TouchPackageAccessTimeAsync(packageName, version, cancellationToken);

        // Return a read-only stream. FileShare.Read allows concurrent reads.
        return new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    public async Task WriteFileAsync(string packageName, string version, string filePath, Stream content, CancellationToken cancellationToken = default)
    {
        var packageDir = GetPackageDirectory(packageName, version);
        var safeFilePath = filePath.Replace("..", "").Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.Combine(packageDir, safeFilePath);

        var lockKey = $"{packageName}@{version}";
        var semaphore = _locks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(cancellationToken);
        try
        {
            var dir = Path.GetDirectoryName(fullPath);
            if (dir != null && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await content.CopyToAsync(fs, cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task TouchPackageAccessTimeAsync(string packageName, string version, CancellationToken cancellationToken = default)
    {
        var accessFile = GetPackageAccessFile(packageName, version);
        var dir = Path.GetDirectoryName(accessFile);
        if (dir != null && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        await File.WriteAllTextAsync(accessFile, timestamp, cancellationToken);
    }

    public Task DeletePackageVersionAsync(string packageName, string version, CancellationToken cancellationToken = default)
    {
        var packageDir = GetPackageDirectory(packageName, version);
        if (Directory.Exists(packageDir))
        {
            Directory.Delete(packageDir, true);
        }
        return Task.CompletedTask;
    }

    public async Task<IEnumerable<(string PackageName, string Version)>> GetStalePackagesAsync(DateTime cutoffDate, CancellationToken cancellationToken = default)
    {
        var stalePackages = new List<(string, string)>();
        var packagesDir = Path.Combine(_basePath, "packages");

        if (!Directory.Exists(packagesDir))
        {
            return stalePackages;
        }

        var cutoffUnix = ((DateTimeOffset)cutoffDate).ToUnixTimeSeconds();

        // Search format: packages/{org}/{pkg}/{version}/.last_accessed OR packages/{pkg}/{version}/.last_accessed
        // Directory structure could be deeper if it's a scoped package like @types/jquery. 
        // We'll iterate down to find .last_accessed files.
        var accessFiles = Directory.EnumerateFiles(packagesDir, ".last_accessed", SearchOption.AllDirectories);

        foreach (var file in accessFiles)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                var content = await File.ReadAllTextAsync(file, cancellationToken);
                if (long.TryParse(content, out var lastAccessedSeconds))
                {
                    if (lastAccessedSeconds < cutoffUnix)
                    {
                        var dirInfo = new DirectoryInfo(Path.GetDirectoryName(file)!);
                        var version = dirInfo.Name;

                        // the package name could be just "jquery" or "@types/jquery"
                        // we can determine this by looking at the parent and grand-parent directory names relative to "packages"
                        var relPath = Path.GetRelativePath(packagesDir, dirInfo.FullName);
                        var parts = relPath.Split(Path.DirectorySeparatorChar);

                        // parts are something like: ["jquery", "3.7.1"] OR ["@types", "jquery", "3.7.1"]
                        if (parts.Length >= 2)
                        {
                            var packageName = string.Join("/", parts.Take(parts.Length - 1));
                            stalePackages.Add((packageName, version));
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Ignore read errors, might be locked or deleted concurrently
            }
        }

        return stalePackages;
    }
}