using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using NpmCdn.NpmRegistry;
using System.Text.Json;
using System.Net;

namespace NpmCdn.PerformanceTests;

public class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly string _responseContent;

    public FakeHttpMessageHandler(string responseContent)
    {
        _responseContent = responseContent;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(_responseContent)
        });
    }
}

[MemoryDiagnoser]
public class VersionResolutionBenchmarks
{
    private INpmRegistryClient _client = null!;
    private NpmRegistryClient _realClient = null!;

    [GlobalSetup]
    public void Setup()
    {
        var metadataJson = @"{
            ""name"": ""jquery"",
            ""dist-tags"": {
                ""latest"": ""3.7.1"",
                ""beta"": ""4.0.0-beta.2""
            },
            ""versions"": {
                ""3.6.4"": {},
                ""3.7.0"": {},
                ""3.7.1"": {},
                ""4.0.0-beta.2"": {}
            }
        }";

        var fakeHandler = new FakeHttpMessageHandler(metadataJson);
        var httpClient = new HttpClient(fakeHandler)
        {
            BaseAddress = new Uri("https://registry.npmjs.org")
        };

        _realClient = new NpmRegistryClient(httpClient);
        _client = _realClient;
    }

    [Benchmark(Baseline = true)]
    public async Task<string?> ResolveVersion_ExactMatch()
    {
        // 3.7.1 should short circuit without HTTP/JSON parsing completely since it parses as semver exactly
        return await _client.ResolveVersionAsync("jquery", "3.7.1", CancellationToken.None);
    }

    [Benchmark]
    public async Task<string?> ResolveVersion_TagMatch_Latest()
    {
        // 'latest' requires fetching the JSON and picking dist-tags.latest
        return await _client.ResolveVersionAsync("jquery", "latest", CancellationToken.None);
    }

    [Benchmark]
    public async Task<string?> ResolveVersion_PrefixMatch_Caret()
    {
        // '^3.6.0' invokes semver filtering over the versions dictionary
        return await _client.ResolveVersionAsync("jquery", "^3.6.0", CancellationToken.None);
    }
}
