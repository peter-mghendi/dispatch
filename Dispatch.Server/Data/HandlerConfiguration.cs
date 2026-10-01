using Dispatch.Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dispatch.Server.Data;

public sealed class HandlerConfiguration : IEntityTypeConfiguration<Handler>
{
    private HandlerConfiguration() { }
    
    public static readonly HandlerConfiguration Instance = new();

    public void Configure(EntityTypeBuilder<Handler> builder)
    {
        builder.Ignore(handler => handler.IsActive);
    }
}