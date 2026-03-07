using Microsoft.Extensions.DependencyInjection;

namespace NpmCdn.NpmRegistry.Tests;

public class PackageDownloaderTests
{
    private INpmPackageDownloader? _downloader;

    [Before(Test)]
    public void Setup()
    {
        var services = new ServiceCollection();

        services.AddHttpClient<INpmPackageDownloader, NpmPackageDownloader>(client =>
        {
            client.BaseAddress = new Uri("https://registry.npmjs.org");
        });

        var provider = services.BuildServiceProvider();
        _downloader = provider.GetRequiredService<INpmPackageDownloader>();
    }

    [Test]
    public async Task DownloadPackageTarball_NonScoped_Success()
    {
        // Actually download a small, very old package to keep the test extremely fast and lightweight
        // is-array@1.0.1 is 1.6kB
        using var stream = await _downloader!.DownloadPackageTarballAsync("is-array", "1.0.1");
        await Assert.That(stream).IsNotNull();
        await Assert.That(stream!.CanRead).IsTrue();
    }

    [Test]
    public async Task DownloadPackageTarball_Scoped_Success()
    {
        // @types/jquery@3.5.30 is small enough for a quick test
        using var stream = await _downloader!.DownloadPackageTarballAsync("@types/jquery", "3.5.30");
        await Assert.That(stream).IsNotNull();
        await Assert.That(stream!.CanRead).IsTrue();
    }
}