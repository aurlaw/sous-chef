using SousChef.Core.DTOs;

namespace SousChef.Core.Common;

public static class EmbeddingInputBuilder
{
    public static string Build(RecipeDto recipe)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(recipe.Title))
            parts.Add(recipe.Title);

        if (!string.IsNullOrWhiteSpace(recipe.Description))
            parts.Add(recipe.Description);

        if (recipe.Tags.Length > 0)
            parts.Add(string.Join(", ", recipe.Tags));

        var ingredientNames = recipe.Ingredients
            .Where(i => !string.IsNullOrWhiteSpace(i.Name))
            .Select(i => i.Name);
        parts.AddRange(ingredientNames);

        return string.Join(". ", parts);
    }
}
