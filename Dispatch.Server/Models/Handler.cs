namespace Dispatch.Server.Models;

public class Handler
{
    public required Guid Id { get; set; }

    public required string Slug { get; set; }

    public required string Title { get; set; }

    public required string Description { get; set; }

    public required string ContentHash { get; set; }
    
    public required long ContentSize { get; init; }

    public required DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public bool IsActive => DeletedAt is null;
}   