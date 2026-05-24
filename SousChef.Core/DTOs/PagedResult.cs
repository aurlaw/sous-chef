namespace SousChef.Core.DTOs;

public record PagedResult<T>(
    IReadOnlyList<T> Items,
    string? NextCursor,
    int PageSize,
    bool HasMore);
