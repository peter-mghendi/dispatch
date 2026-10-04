namespace Dispatch.Server.Models.Request;

public class HandlerRequest
{
    public string? Slug { get; set; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public required IFormFile File { get; set; }
}