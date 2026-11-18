namespace Dispatch.Kit.Models;

public record HandlerData(
    Guid Id,
    string Slug,
    string Endpoint,
    string Title,
    string Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeletedAt
);