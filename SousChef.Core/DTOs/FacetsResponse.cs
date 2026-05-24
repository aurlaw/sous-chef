namespace SousChef.Core.DTOs;

public record FacetsResponse(
    IReadOnlyList<string> CuisineTypes,
    IReadOnlyList<string> Courses,
    IReadOnlyList<string> Difficulties,
    IReadOnlyList<string> Tags);
