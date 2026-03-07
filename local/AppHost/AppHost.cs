var builder = DistributedApplication.CreateBuilder(args);

var cache = builder.AddRedis("cache");

var api = builder.AddProject<Projects.NpmCdn_Api>("npm-cdn-api")
    .WithReference(cache);

builder.AddProject<Projects.Demo>("demo")
    .WithEnvironment("CdnUrl", api.GetEndpoint("https"));

builder.Build().Run();