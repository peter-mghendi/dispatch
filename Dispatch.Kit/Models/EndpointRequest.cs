namespace Dispatch.Kit.Models;

public class EndpointRequest
{
    public required string Method { get; set; }
    public required string Path { get; set; }
    public required List<HeaderData> Headers { get; set; }
    public required string Body { get; set; }
}