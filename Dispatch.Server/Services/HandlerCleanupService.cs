using Dispatch.Server.Data;
using Dispatch.Server.Models;
using Microsoft.EntityFrameworkCore;
using PurgeResult = (int Rows, bool Referenced);

namespace Dispatch.Server.Services;

public class HandlerCleanupService(
    IServiceScopeFactory scopes,
    ILogger<HandlerCleanupService> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private const int BatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Interval);

        try
        {
            do await PerformCleanupAsync(ct); while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogInformation("Stopping handler cleanup");
        }
    }

    private async Task PerformCleanupAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<DispatchDatabaseContext>();
            var storage = scope.ServiceProvider.GetRequiredService<HandlerStorage>();

            var cutoff = DateTimeOffset.UtcNow - Retention;
            var hashes = await context.Handlers.AsNoTracking()
                .Where(h => h.DeletedAt < cutoff)
                .Select(h => h.ContentHash)
                .Distinct()
                .Take(BatchSize)
                .ToListAsync(ct);

            foreach (var hash in hashes)
            {
                try
                {
                    var (removed, referenced) = await PurgeAsync(context, storage, hash, cutoff, ct);
                    var action = referenced switch
                    {
                        true => "object kept (still referenced)",
                        false => "object removed (no references)"
                    };

                    logger.LogInformation("Purged {Count} handler(s) for {Hash} - {Action}", removed, hash, action);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogError(exception, "Failed to purge handlers for hash {Hash}", hash);
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Handler cleanup pass failed");
        }
    }

    private static async Task<PurgeResult> PurgeAsync(
        DispatchDatabaseContext context,
        HandlerStorage storage,
        string hash,        
        DateTimeOffset cutoff,
        CancellationToken ct
    )
    {
        var database = context.Database;

        await using var transaction = await database.BeginTransactionAsync(ct);
        await database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({hash}, 0))", ct);

        var removed = await context.Handlers
            .Where(h => h.ContentHash == hash && h.DeletedAt < cutoff)
            .ExecuteDeleteAsync(ct);

        var referenced = await context.Handlers.AnyAsync(h => h.ContentHash == hash, ct);
        if (!referenced) await storage.DeleteAsync(hash, ct);

        await transaction.CommitAsync(ct);
        return (Rows: removed, Referenced: referenced);
    }
}