using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using NpmCdn.Storage;

namespace NpmCdn.PerformanceTests;

[MemoryDiagnoser]
public class VolumeStorageBenchmarks
{
    private VolumeStorageProvider _provider = null!;
    private string _testDirectory = null!;
    private const string PackageName = "benchmark-pkg";
    private const string Version = "1.0.0";
    private const string FilePath = "dist/index.js";
    private byte[] _fileData = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"cdn-bench-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
        _provider = new VolumeStorageProvider(_testDirectory);

        // Create a 50KB dummy file in memory to write
        _fileData = new byte[50 * 1024];
        new Random(42).NextBytes(_fileData);

        // Pre-warm the cache for read tests
        using var stream = new MemoryStream(_fileData);
        await _provider.WriteFileAsync(PackageName, Version, FilePath, stream);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [Benchmark(Baseline = true)]
    public async Task<long> ReadFileAsync_Throughput()
    {
        using var stream = await _provider.ReadFileAsync(PackageName, Version, FilePath, CancellationToken.None);
        
        using var memoryStream = new MemoryStream();
        await stream!.CopyToAsync(memoryStream);
        return memoryStream.Length;
    }

    [Benchmark]
    public async Task WriteFileAsync_Throughput()
    {
        using var stream = new MemoryStream(_fileData);
        await _provider.WriteFileAsync(PackageName, Version, "dist/new-file.js", stream, CancellationToken.None);
    }
}
