using System.Diagnostics;
using System.Text;
using Dispatch.Server.Models;
using Microsoft.Extensions.Caching.Memory;

namespace Dispatch.Server.Services;

public sealed class HandlerRunner(HandlerStorage storage, ILogger<HandlerRunner> logger)
{
    private static readonly TimeSpan ExecutionTimeout = TimeSpan.FromSeconds(60);
    // private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);

    public async Task<Response> RunAsync(Handler handler, Request request, CancellationToken cancellation)
    {
        using var scope = logger.BeginScope(
            "Handler {HandlerId} ({ContentHash}, {ContentSize} bytes)",
            handler.Id,
            handler.ContentHash,
            handler.ContentSize
        );
        var component = await LoadAsync(handler, cancellation);
        var input = request with { Path = "/" + (request.Path ?? string.Empty).TrimStart('/') };

        var timer = Stopwatch.StartNew();
        try
        {
            var response = await Task.Run(() => GuestInvoker.Invoke(component, input), cancellation)
                .WaitAsync(ExecutionTimeout, cancellation);

            if (response.Status is < 100 or > 599)
            {
                logger.LogWarning("Handler returned invalid status {Status}", response.Status);
                return Failure(502, "Handler returned an invalid status code.");
            }

            logger.LogInformation("Handler returned {Status} in {Elapsed} ms.", response.Status, timer.ElapsedMilliseconds);
            return response;
        }
        catch (TimeoutException)
        {
            logger.LogWarning("Handler timed out after {Timeout}", ExecutionTimeout);
            return Failure(504, "Handler timed out.");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Handler failed");
            return Failure(500, "Handler failed.");
        }
    }

    // TODO: [Cache] Enable caching for these after verifying that modules can be streamed from object storage
    private async Task<byte[]> LoadAsync(Handler handler, CancellationToken cancellation)
    {
        // var key = $"handlers:{handler.ContentHash}";
        // if (cache.TryGetValue(key, out byte[]? cached) && cached is not null)
        // {
        //     logger.LogDebug("Loaded {Loaded} bytes from cache.", cached.Length);
        //     return cached;
        // }

        logger.LogDebug("Downloading handler.");

        await using var stream = await storage.DownloadAsync(handler.ContentHash, cancellation);
        using var memory = new MemoryStream(capacity: (int)handler.ContentSize);
        await stream.CopyToAsync(memory, cancellation);
        var bytes = memory.ToArray();

        logger.LogDebug("Downloaded {Downloaded} bytes.", bytes.Length);

        // cache.Set(key, bytes, new MemoryCacheEntryOptions { SlidingExpiration = CacheLifetime });
        return bytes;
    }

    private static Response Failure(int status, string message) =>
        new(status, [("Content-Type", "text/plain; charset=utf-8")], Encoding.UTF8.GetBytes(message));

    public sealed record Request(string Method, string? Path, (string Name, string Value)[] Headers, byte[] Body);

    public sealed record Response(int Status, (string Name, string Value)[] Headers, byte[] Body);
}