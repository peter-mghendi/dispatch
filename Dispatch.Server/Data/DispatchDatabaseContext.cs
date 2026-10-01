using Dispatch.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Dispatch.Server.Data;

public class DispatchDatabaseContext(DbContextOptions<DispatchDatabaseContext> options) : DbContext(options)
{
    public DbSet<Handler> Handlers => Set<Handler>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfiguration(HandlerConfiguration.Instance);
    }
}