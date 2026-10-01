using System.Net;

var builder = DistributedApplication.CreateBuilder(args);

var postgresUsername = builder.AddParameter(name: "cluster-username", secret: true);
var postgresPassword = builder.AddParameter(name: "cluster-password", secret: true);

var postgres = builder.AddPostgres(name: "postgres", postgresUsername, postgresPassword)
    .WithImage(image: "timescale/timescaledb-ha", tag: "pg18")
    .WithDataVolume(isReadOnly: false)
    .WithLifetime(ContainerLifetime.Persistent);

var database = postgres.AddDatabase("database");

var server = builder.AddProject<Projects.Dispatch_Server>(name: "server")
    .WaitFor(database)
    .WithReference(database)
    .WithHttpEndpoint(name: "http")
    .WithHttpHealthCheck(path: "/health", statusCode: (int)HttpStatusCode.OK);

var web = builder.AddBlazorWasmProject<Projects.Dispatch_Web>(name: "web")
    .WithReference(server.GetEndpoint("http"));

var gateway = builder.AddDotnetProjectBlazorGateway("gateway")
    .WithExternalHttpEndpoints()
    .WithBlazorClientApp(web);

builder.Build().Run();
