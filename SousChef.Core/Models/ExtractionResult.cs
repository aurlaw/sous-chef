using SousChef.Core.DTOs;

namespace SousChef.Core.Models;

public record ExtractionResult(RecipeDto? Recipe, string? NotARecipeReason = null)
{
    public bool IsNotARecipe => NotARecipeReason is not null;
}
