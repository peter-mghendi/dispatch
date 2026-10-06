using Dispatch.Server.Data;
using Dispatch.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static System.StringComparison;
using static Microsoft.AspNetCore.Http.TypedResults;
using static Microsoft.Net.Http.Headers.HeaderNames;

namespace Dispatch.Server.Handlers;

public sealed class EndpointHandler
{
    public static async Task<IResult> InvokeEndpoint(
        [FromServices] DispatchDatabaseContext context,
        [FromServices] HandlerRunner runner,
        [FromServices] IHttpContextAccessor accessor,
        [FromServices] ILogger<EndpointHandler> logger,
        [FromRoute] string slug,
        [FromRoute] string? path,
        CancellationToken cancellation
    )
    {
        if (accessor.HttpContext is not { } http)
        {
            logger.LogCritical("Unable to access HTTP context");
            return InternalServerError();
        }

        var handler = await context.Handlers.Where(handler => handler.Slug == slug).SingleOrDefaultAsync(cancellation);
        if (handler is null or { DeletedAt: not null }) return NotFound();

        using var stream = new MemoryStream(capacity: (int)(http.Request.ContentLength ?? 0));
        await http.Request.Body.CopyToAsync(destination: stream, cancellation);

        var request = new HandlerRunner.Request(
            Method: http.Request.Method,
            Path: path,
            Headers: [.. http.Request.Headers.SelectMany(h => h.Value.Select(value => (h.Key, value ?? string.Empty)))],
            Body: stream.ToArray()
        );

        var response = await runner.RunAsync(handler, request, cancellation);
        var mime = response.Headers.FirstOrDefault(h => h.Name.Equals(ContentType, OrdinalIgnoreCase)).Value;

        foreach (var (name, value) in response.Headers) http.Response.Headers.Append(name, value);
        http.Response.StatusCode = response.Status;

        return Results.Bytes(response.Body, mime);
    }
}