using Microsoft.Extensions.DependencyInjection;

using NpmCdn.NpmRegistry;

using TUnit.Assertions;
using TUnit.Core;

namespace NpmCdn.NpmRegistry.Tests;

public class EntrypointResolutionTests
{
    private INpmRegistryClient? _client;

    [Before(Test)]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();

        services.AddHttpClient<INpmRegistryClient, NpmRegistryClient>(client =>
        {
            client.BaseAddress = new Uri("https://registry.npmjs.org");
        });

        var provider = services.BuildServiceProvider();
        _client = provider.GetRequiredService<INpmRegistryClient>();
    }

    [Test]
    public async Task ResolveEntrypointAsync_JQuery_ResolvesToMain()
    {
        // jQuery 3.7.1 has "main": "dist/jquery.js"
        var result = await _client!.ResolveEntrypointAsync("jquery", "3.7.1");
        await Assert.That(result).IsEqualTo("dist/jquery.js");
    }

    [Test]
    public async Task ResolveEntrypointAsync_React_ResolvesToMain()
    {
        // React 18.2.0 has "main": "index.js"
        var result = await _client!.ResolveEntrypointAsync("react", "18.2.0");
        await Assert.That(result).IsEqualTo("index.js");
    }
}