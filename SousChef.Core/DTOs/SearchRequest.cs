namespace SousChef.Core.DTOs;

public record SearchRequest(
    string? Query,
    SearchFilters? Filters,
    string? Cursor,
    int PageSize = 20);

public record SearchFilters(
    List<string>? CuisineTypes,
    List<string>? Courses,
    List<string>? Difficulties,
    int? MaxTotalMinutes,
    List<string>? Tags,
    List<string>? IncludeIngredients,
    List<string>? ExcludeIngredients);
