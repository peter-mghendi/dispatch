using Microsoft.AspNetCore.Cors.Infrastructure;

namespace Dispatch.Server.Infrastructure;

public static class CrossOrigin
{
    public static void AllowAll(CorsPolicyBuilder policy) => policy
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowAnyOrigin()
        .WithExposedHeaders("*");
}