namespace Dispatch.Kit.Models;

public class EndpointResponse
{
    public required int StatusCode { get; set; }
    public required List<HeaderData> Headers { get; set; }
    public required string Body { get; set; }
}