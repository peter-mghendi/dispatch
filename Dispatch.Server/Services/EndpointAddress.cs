using static Dispatch.Server.Handlers.EndpointHandler;

namespace Dispatch.Server.Services;

/// <summary>Builds the public URL a handler is invoked at.</summary>
public sealed class EndpointAddress(IConfiguration configuration, LinkGenerator links)
{
    // Set by the AppHost. Behind Aspire's proxies the server cannot see the address clients reach it at.
    private readonly string? _publicUrl = configuration["PublicUrl"]?.TrimEnd('/');

    public string For(HttpContext http, string slug)
    {
        var path = links.GetPathByName(nameof(InvokeEndpoint), new { slug })
                   ?? throw new InvalidOperationException($"No route named '{nameof(InvokeEndpoint)}'.");

        return $"{_publicUrl ?? $"{http.Request.Scheme}://{http.Request.Host}"}{path}";
    }
}
