namespace SousChef.Core.DTOs;

public record RecipeDetailDto(
    Guid Id,
    string Title,
    string? Description,
    string? CuisineType,
    string? Course,
    string? Difficulty,
    int? PrepTimeMinutes,
    int? CookTimeMinutes,
    int? TotalTimeMinutes,
    int? Servings,
    string[] Tags,
    DateTimeOffset CreatedAt,
    IReadOnlyList<IngredientDetailDto> Ingredients,
    IReadOnlyList<StepDetailDto> Steps);

public record IngredientDetailDto(
    Guid Id,
    string Name,
    string? Quantity,
    string? Unit,
    string? Notes);

public record StepDetailDto(
    Guid Id,
    int StepNumber,
    string Instruction);
