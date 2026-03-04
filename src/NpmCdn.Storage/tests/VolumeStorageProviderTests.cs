using System.Text;

using NpmCdn.Storage;

using TUnit.Assertions;
using TUnit.Core;

namespace NpmCdn.Storage.Tests;

public class VolumeStorageProviderTests
{
    private VolumeStorageProvider? _storage;
    private string? _tempDir;

    [Before(Test)]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "NpmCdnTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _storage = new VolumeStorageProvider(_tempDir);
    }

    [After(Test)]
    public void Cleanup()
    {
        if (_tempDir != null && Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Test]
    public async Task WriteAndReadFile_Success()
    {
        var packageName = "@myorg/mypackage";
        var version = "1.0.0";
        var filePath = "dist/app.js";
        var contentString = "console.log('hello');";

        using var writeStream = new MemoryStream(Encoding.UTF8.GetBytes(contentString));
        await _storage!.WriteFileAsync(packageName, version, filePath, writeStream);

        var exists = await _storage.FileExistsAsync(packageName, version, filePath);
        await Assert.That(exists).IsTrue();

        using var readStream = await _storage.ReadFileAsync(packageName, version, filePath);
        await Assert.That(readStream).IsNotNull();

        using var reader = new StreamReader(readStream!);
        var readContent = await reader.ReadToEndAsync();

        await Assert.That(readContent).IsEqualTo(contentString);
    }

    [Test]
    public async Task StalePackages_ReturnedCorrectly()
    {
        var packageName = "jquery";
        var version = "3.7.1";
        var filePath = "jquery.js";

        using var writeStream = new MemoryStream(Encoding.UTF8.GetBytes("stub"));
        await _storage!.WriteFileAsync(packageName, version, filePath, writeStream);
        await _storage.TouchPackageAccessTimeAsync(packageName, version);

        // Package was just touched, so it shouldn't be stale if cutoff is today minus 30 days
        var stale1 = await _storage.GetStalePackagesAsync(DateTime.UtcNow.AddDays(-30));
        await Assert.That(stale1).IsEmpty();

        // 1 second into the future should mark it as stale
        var stale2 = await _storage.GetStalePackagesAsync(DateTime.UtcNow.AddSeconds(5));
        await Assert.That(stale2).IsNotEmpty();
        await Assert.That(stale2.First().PackageName).IsEqualTo(packageName);
        await Assert.That(stale2.First().Version).IsEqualTo(version);
    }
}