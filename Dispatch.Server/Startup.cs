using Microsoft.EntityFrameworkCore;
using static Dispatch.Server.Handlers.EndpointHandler;
using static Dispatch.Server.Handlers.HandlerHandler;
using static Microsoft.AspNetCore.Http.HttpMethods;

namespace Dispatch.Server;

public static class Startup
{
    extension(WebApplication app)
    {
        public async Task MaintainDatabaseAsync<TContext>(CancellationToken cancellation = default) where TContext : DbContext
        {
            using var scope = app.Services.CreateScope();
            await using var context = scope.ServiceProvider.GetRequiredService<TContext>();
            await context.Database.MigrateAsync(cancellation);
        }

        public WebApplication MapEndpoints()
        {
            var routes = app.MapGroup("").DisableAntiforgery();
            
            var handlers = routes.MapGroup("/handlers/");
            handlers.MapPost("", CreateHandler).WithName(nameof(CreateHandler));
            handlers.MapGet("", ListHandlers).WithName(nameof(ListHandlers));
            handlers.MapGet("{id:guid}", GetHandler).WithName(nameof(GetHandler));
            handlers.MapDelete("{id:guid}", DeleteHandler).WithName(nameof(DeleteHandler));

            routes.MapMethods("/endpoints/{slug}/{**path}", [Delete, Get, Patch, Post, Put, Query], InvokeEndpoint)
                .WithName(nameof(InvokeEndpoint));

            return app;
        }
    }
}