using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Dispatch.Server.Data;

public static class DispatchDatabaseExtensions
{
    extension(DispatchDatabaseContext context)
    {
        /// <summary>
        /// Starts a transaction that holds the content lock for <paramref name="key"/> until it ends.
        /// </summary>
        public async Task<IDbContextTransaction> BeginContentTransactionAsync(string key, CancellationToken ct = default)
        {
            var transaction = await context.Database.BeginTransactionAsync(ct);

            try
            {
                await context.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
                return transaction;
            }
            catch
            {
                await transaction.DisposeAsync();
                throw;
            }
        }
    }
}
