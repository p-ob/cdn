using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

using Moq;

using NpmCdn.NpmRegistry;

using TUnit.Assertions;
using TUnit.Core;

namespace NpmCdn.NpmRegistry.Tests;

public class VersionResolutionTests
{
    private INpmRegistryClient? _client;

    [Before(Test)]
    public void Setup()
    {
        // Minimal setup for exact tests. 
        // We will need to mock HttpMessageHandler to simulate NPM registry responses safely without hitting the network on every run.
        var services = new ServiceCollection();

        services.AddHttpClient<INpmRegistryClient, NpmRegistryClient>(client =>
        {
            client.BaseAddress = new Uri("https://registry.npmjs.org");
        });

        var provider = services.BuildServiceProvider();
        _client = provider.GetRequiredService<INpmRegistryClient>();
    }

    [Test]
    public async Task ResolveVersionAsync_ExactVersion_ReturnsSameVersion()
    {
        var result = await _client!.ResolveVersionAsync("jquery", "3.7.1");
        await Assert.That(result).IsEqualTo("3.7.1");
    }

    [Test]
    public async Task ResolveVersionAsync_LatestTag_ReturnsLatestVersion()
    {
        // "latest" should resolve to some valid semver string like "3.7.1"
        var result = await _client!.ResolveVersionAsync("jquery", "latest");
        await Assert.That(result).IsNotNull();
        await Assert.That(result).IsNotEmpty();
    }
}