using System.Net;

var builder = DistributedApplication.CreateBuilder(args);

var postgresUsername = builder.AddParameter(name: "postgres-username", secret: true);
var postgresPassword = builder.AddParameter(name: "postgres-password", secret: true);

var bucket = builder.AddRustFs("rustfs")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent)
    .AddBucket("dispatch");

var database = builder.AddPostgres(name: "postgres", postgresUsername, postgresPassword)
    .WithImage(image: "timescale/timescaledb-ha", tag: "pg18")
    .WithDataVolume(isReadOnly: false)
    .WithLifetime(ContainerLifetime.Persistent)
    .AddDatabase("dispatch");

var server = builder.AddProject<Projects.Dispatch_Server>(name: "server")
    .WaitFor(bucket)
    .WaitFor(database)
    .WithReference(bucket)
    .WithReference(database)
    .WithHttpEndpoint(name: "http")
    .WithHttpHealthCheck(path: "/health", statusCode: (int)HttpStatusCode.OK);

// The server's own address for handler endpoint URLs; the web app only sees it through the gateway's proxy.
server.WithEnvironment("PublicUrl", server.GetEndpoint("https"));

var web = builder.AddBlazorWasmProject<Projects.Dispatch_Web>(name: "web")
    .WithReference(server.GetEndpoint("http"));

var gateway = builder.AddDotnetProjectBlazorGateway("gateway")
    .WithExternalHttpEndpoints()
    .WithBlazorClientApp(web);

builder.Build().Run();
