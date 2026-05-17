namespace SousChef.Core.DTOs;

public record RecipeDto(
    string Title,
    string? Description,
    string? CuisineType,
    string? Course,
    int? PrepTimeMinutes,
    int? CookTimeMinutes,
    int? TotalTimeMinutes,
    int? Servings,
    string? Difficulty,
    string[] Tags,
    IReadOnlyList<IngredientDto> Ingredients,
    IReadOnlyList<RecipeStepDto> Steps);

public record IngredientDto(
    string Name,
    string? Quantity,
    string? Unit,
    string? Notes);

public record RecipeStepDto(
    int StepNumber,
    string Instruction);
