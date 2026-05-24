namespace SousChef.Core.DTOs;

public record RecipeSummaryDto(
    Guid Id,
    string Title,
    string? CuisineType,
    string? Course,
    string? Difficulty,
    int? TotalTimeMinutes,
    int? Servings,
    string[] Tags,
    DateTimeOffset CreatedAt);
