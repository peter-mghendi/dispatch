using Dispatch.Kit.Models;

namespace Dispatch.Kit.Services;

public class EndpointService(HttpClient http)
{
    public async Task<EndpointResponse> InvokeAsync(
        string slug,
        EndpointRequest request,
        CancellationToken cancellation = default
    )
    {
        var method = HttpMethod.Parse(request.Method);
        var segments = from segment in (string[])["endpoints", slug, request.Path.TrimStart('/')]
            where segment is [_, ..]
            select segment;

        var message = new HttpRequestMessage(method, string.Join('/', segments));

        message.Content = new StringContent(request.Body);

        var headers = from header in request.Headers
            where !message.Headers.TryAddWithoutValidation(header.Name, header.Value)
            select header;

        foreach (var header in headers)
        {
            message.Content.Headers.TryAddWithoutValidation(header.Name, header.Value);
        }

        var response = await http.SendAsync(message, cancellation);

        var responseHeaders = response.Headers
            .SelectMany(h => h.Value.Select(value => new HeaderData(h.Key, value)));

        var responseContentHeaders = response.Content.Headers
            .SelectMany(h => h.Value.Select(value => new HeaderData(h.Key, value)));

        return new EndpointResponse
        {
            StatusCode = (int)response.StatusCode,
            Headers = [.. responseHeaders.Concat(responseContentHeaders)],
            Body = await response.Content.ReadAsStringAsync(cancellation)
        };
    }
}