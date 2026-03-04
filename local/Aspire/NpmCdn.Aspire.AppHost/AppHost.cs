var builder = DistributedApplication.CreateBuilder(args);

var cache = builder.AddRedis("cache");

builder.AddProject<Projects.NpmCdn_Api>("npm-cdn-api")
    .WithReference(cache);

builder.Build().Run();