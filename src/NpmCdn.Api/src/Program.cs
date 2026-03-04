using Aspire.StackExchange.Redis;

using NpmCdn.NpmRegistry;
using NpmCdn.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddRedisClient("cache");
#pragma warning disable EXTEXP0018
builder.Services.AddHybridCache(options =>
{
    options.DefaultEntryOptions = new Microsoft.Extensions.Caching.Hybrid.HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromMinutes(10),
        LocalCacheExpiration = TimeSpan.FromMinutes(10)
    };
});
#pragma warning restore EXTEXP0018

// Add services to the container.
builder.Services.AddOpenApi();
builder.Services.AddHttpClient<INpmRegistryClient, NpmRegistryClient>(client =>
{
    client.BaseAddress = new Uri("https://registry.npmjs.org");
});
builder.Services.AddHttpClient<INpmPackageDownloader, NpmPackageDownloader>(client =>
{
    client.BaseAddress = new Uri("https://registry.npmjs.org");
});
builder.Services.AddTransient<NpmPackageExtractor>();

// Configure Storage Provider (Defaulting to Volume for now)
var storagePath = builder.Configuration.GetValue<string>("Storage:VolumePath")
    ?? Path.Combine(Directory.GetCurrentDirectory(), "cdn-data");
builder.Services.AddSingleton<IStorageProvider>(new VolumeStorageProvider(storagePath));

builder.Services.Configure<NpmCdn.Api.Configuration.CacheOptions>(builder.Configuration.GetSection("CacheControl"));
builder.Services.Configure<NpmCdn.Api.Configuration.EvictionOptions>(builder.Configuration.GetSection("Eviction"));
builder.Services.AddHostedService<NpmCdn.Api.Services.EvictionBackgroundService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/npm/{*packageSpec}", NpmCdn.Api.NpmEndpointHandlers.HandlePackageSpecAsync);

app.Run();