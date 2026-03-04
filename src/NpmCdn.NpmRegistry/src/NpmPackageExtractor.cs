using System.Formats.Tar;
using System.IO.Compression;

using NpmCdn.Storage;

namespace NpmCdn.NpmRegistry;

public class NpmPackageExtractor(IStorageProvider storage)
{
    private readonly IStorageProvider _storage = storage;

    /// <summary>
    /// Extracts a gzip-compressed tarball stream to the storage provider.
    /// NPM tarballs bundle everything inside a single root 'package/' directory,
    /// which this extractor strips during extraction to save files directly under the 
    /// {package}@{version} storage root.
    /// </summary>
    public async Task ExtractTarballAsync(string packageName, string version, Stream tgzStream, CancellationToken cancellationToken = default)
    {
        using var gzipStream = new GZipStream(tgzStream, CompressionMode.Decompress);
        using var memoryTar = new MemoryStream();
        await gzipStream.CopyToAsync(memoryTar, cancellationToken);
        memoryTar.Position = 0;

        using var tarReader = new TarReader(memoryTar);

        TarEntry? entry;
        while ((entry = await tarReader.GetNextEntryAsync(cancellationToken: cancellationToken)) != null)
        {
            if (entry.EntryType == TarEntryType.Directory || entry.DataStream == null)
            {
                continue;
            }

            // npm tarballs usually have a 'package/' root folder. We want to strip that.
            // e.g., 'package/package.json' -> 'package.json'
            //       'package/dist/jquery.js' -> 'dist/jquery.js'
            var name = entry.Name;
            if (name.StartsWith("package/", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring("package/".Length);
            }

            // Re-copy the data stream because TarEntry.DataStream doesn't support async reads well natively.
            using var fileStream = new MemoryStream();
            await entry.DataStream.CopyToAsync(fileStream, cancellationToken);
            fileStream.Position = 0;

            await _storage.WriteFileAsync(packageName, version, name, fileStream, cancellationToken);
        }
    }
}