using System.Diagnostics;
using System.Text;
using Dispatch.Server.Models;

namespace Dispatch.Server.Services;

public sealed class HandlerRunner(HandlerStorage storage, ILogger<HandlerRunner> logger)
{
    private static readonly TimeSpan ExecutionTimeout = TimeSpan.FromSeconds(60);

    public async Task<Response> RunAsync(Handler handler, Request request, CancellationToken cancellation)
    {
        using var scope = logger.BeginScope(
            "Handler {HandlerId} ({ContentHash}, {ContentSize} bytes)",
            handler.Id,
            handler.ContentHash,
            handler.ContentSize
        );
        var component = await storage.FetchAsync(handler.ContentHash, cancellation);
        logger.LogDebug("Loaded {Bytes} bytes", component.Length);

        var input = request with { Path = "/" + (request.Path ?? string.Empty).TrimStart('/') };

        var timer = Stopwatch.StartNew();
        try
        {
            var (response, fuel) = await Task.Run(() => GuestInvoker.Invoke(component, input), cancellation)
                .WaitAsync(ExecutionTimeout, cancellation);

            if (response.Status is < 100 or > 599)
            {
                logger.LogWarning("Handler returned invalid status {Status}", response.Status);
                return Failure(502, "Handler returned an invalid status code.");
            }

            logger.LogDebug(
                "status={Status} elapsed_ms={Elapsed} initialize_fuel={InitializeFuel} handle_fuel={HandlerFuel}",
                response.Status,
                timer.ElapsedMilliseconds,
                fuel.Initialize,
                fuel.Handle
            );
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

    private static Response Failure(int status, string message) =>
        new(status, [("Content-Type", "text/plain; charset=utf-8")], Encoding.UTF8.GetBytes(message));

    public sealed record Request(string Method, string? Path, (string Name, string Value)[] Headers, byte[] Body);

    public sealed record Response(int Status, (string Name, string Value)[] Headers, byte[] Body);

    /// <summary>Instructions the guest executed in its _initialize export and in handle.</summary>
    public sealed record Fuel(long Initialize, long Handle);
}