using Dispatch.Server.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Dispatch.Server.Infrastructure;

public class DatabaseHealthCheck(DispatchDatabaseContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext _,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            await db.Database.ExecuteSqlAsync($"SELECT 1", cancellationToken);
            return HealthCheckResult.Healthy("Database connection is healthy.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded("Database probe failed.", ex);
        }
    }
}