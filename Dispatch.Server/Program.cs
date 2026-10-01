using CmdScale.EntityFrameworkCore.TimescaleDB;
using Dispatch.Server.Data;
using Dispatch.Server.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<DispatchDatabaseContext>("database", configureDbContextOptions: db => db.UseTimescaleDb());

builder.Services.AddOpenApi();
builder.Services.AddCors(cors => cors.AddPolicy(nameof(CrossOrigin.AllowAll), CrossOrigin.AllowAll));
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

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