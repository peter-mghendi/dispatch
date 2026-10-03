using System.Data.Common;
using Amazon.Runtime;
using Amazon.S3;
using CmdScale.EntityFrameworkCore.TimescaleDB;
using Dispatch.Server.Data;
using Dispatch.Server.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<DispatchDatabaseContext>("dispatch", configureDbContextOptions: db => db.UseTimescaleDb());
builder.Services.AddSingleton<IAmazonS3>(sp =>
{
    var cs = sp.GetRequiredService<IConfiguration>().GetConnectionString("rustfs-dispatch")
             ?? throw new InvalidOperationException("Missing connection string 'rustfs-dispatch'");
    var csb = new DbConnectionStringBuilder { ConnectionString = cs };

    return new AmazonS3Client(
        new BasicAWSCredentials((string)csb["AccessKey"], (string)csb["SecretKey"]),
        new AmazonS3Config
        {
            ServiceURL = (string)csb["Endpoint"],
            ForcePathStyle = true,
            AuthenticationRegion = "us-east-1", // matches the toolkit's default signing region
        }
    );
});

builder.Services.AddOpenApi();
builder.Services.AddCors(cors => cors.AddPolicy(nameof(CrossOrigin.AllowAll), CrossOrigin.AllowAll));
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"])
    .AddCheck<RustFsHealthCheck>("rustfs", tags: ["ready"]);

if (int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var port))
{
    builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(port));
}

var app = builder.Build();

using var scope = app.Services.CreateScope();
await using var context = scope.ServiceProvider.GetRequiredService<DispatchDatabaseContext>();
await context.Database.MigrateAsync();

app.MapDefaultEndpoints();
app.MapHealthChecks("/ready", new() { Predicate = check => check.Tags.Contains("ready") });

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors(nameof(CrossOrigin.AllowAll));
app.Run();