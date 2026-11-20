using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Dispatch.Kit.Models;
using Dispatch.Server.Data;
using Dispatch.Server.Models;
using Dispatch.Server.Models.Request;
using Dispatch.Server.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static Microsoft.AspNetCore.Http.TypedResults;
using static Npgsql.PostgresErrorCodes;

namespace Dispatch.Server.Handlers;

public sealed partial class HandlerHandler
{
    private const int MaxWasmBytes = 5 * 1024 * 1024;
    private const int SlugLength = 8;

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,62}$")]
    private static partial Regex SlugPattern { get; }

    public static async Task<Ok<HandlerData[]>> ListHandlers(
        [FromServices] DispatchDatabaseContext context,
        [FromServices] EndpointAddress endpoints,
        HttpContext http
    )
    {
        var query = from handler in context.Handlers.AsNoTracking()
            orderby handler.CreatedAt descending
            select handler;

        var handlers = await query.ToArrayAsync();
        return Ok(handlers.Select(handler => handler.ToData(endpoints.For(http, handler.Slug))).ToArray());
    }

    public static async Task<Results<CreatedAtRoute, Conflict, ValidationProblem>> CreateHandler(
        [FromServices] DispatchDatabaseContext context,
        [FromServices] HandlerStorage storage,
        [FromForm] HandlerRequest request,
        CancellationToken ct
    )
    {
        var slug = request.Slug?.Trim() switch
        {
            [_, ..] text => text,
            _ => RandomNumberGenerator.GetString("abcdefghijklmnopqrstuvwxyz0123456789", SlugLength)
        };

        if (!SlugPattern.IsMatch(slug))
            return Error("slug", "Use 1-63 chars: a-z, 0-9, '-'; must not start with '-'; not a reserved word.");

        if (string.IsNullOrWhiteSpace(request.Title))
            return Error("title", "Title is required.");

        if (request.File.Length is 0 or > MaxWasmBytes)
            return Error("file", $"Module must be 1 byte to {MaxWasmBytes / 1024 / 1024} MB.");

        var bytes = new byte[request.File.Length];
        await using (var stream = request.File.OpenReadStream()) await stream.ReadExactlyAsync(bytes, ct);

        if (bytes.Length < 8 || !bytes.AsSpan(0, 4).SequenceEqual("\0asm"u8))
            return ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["Not a WebAssembly binary."] });

        var hash = HandlerStorage.Hash(bytes);
        var handler = new Handler
        {
            Id = Guid.CreateVersion7(),
            Slug = slug,
            Title = request.Title.Trim(),
            Description = request.Description,
            ContentHash = hash,
            ContentSize = bytes.Length,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        try
        {
            await context.Database.CreateExecutionStrategy().ExecuteAsync(async cancellation =>
            {
                await using var transaction = await context.BeginContentTransactionAsync(hash, cancellation);

                context.Handlers.Add(handler);

                await context.SaveChangesAsync(cancellation);
                await storage.StoreAsync(bytes, cancellation);
                await transaction.CommitAsync(cancellation);
            }, ct);

            return CreatedAtRoute(nameof(GetHandler), new RouteValueDictionary { ["id"] = handler.Id });
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            return Conflict();
        }
    }

    public static async Task<Results<Ok<HandlerData>, NotFound>> GetHandler(
        [FromServices] DispatchDatabaseContext context,
        [FromServices] EndpointAddress endpoints,
        HttpContext http,
        [FromRoute] Guid id
    )
    {
        if (await context.Handlers.FindAsync(id) is not {} handler) return NotFound();

        return Ok(handler.ToData(endpoints.For(http, handler.Slug)));
    }

    public static async Task<NoContent> DeleteHandler(
        [FromServices] DispatchDatabaseContext context,
        [FromRoute] Guid id
    )
    {
        await context.Handlers
            .Where(handler => handler.Id == id && handler.DeletedAt == null)
            .ExecuteUpdateAsync(setter => setter.SetProperty(handler => handler.DeletedAt, DateTimeOffset.UtcNow));

        return NoContent();
    }

    private static ValidationProblem Error(string key, string value) =>
        ValidationProblem(new Dictionary<string, string[]> { [key] = [value] });
}