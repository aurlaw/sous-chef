using Microsoft.EntityFrameworkCore;
using SousChef.Core.Interfaces;
using SousChef.Infrastructure.Data;

namespace SousChef.Api.Endpoints;

public static class RecipeEndpoints
{
    public static IEndpointRouteBuilder MapRecipeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapDelete("/api/recipes/{id:guid}", DeleteRecipe)
            .WithName("DeleteRecipe")
            .WithSummary("Delete a recipe and its source PDF")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> DeleteRecipe(
        Guid id,
        SousChefDbContext db,
        IStorageService storage,
        CancellationToken ct)
    {
        var recipe = await db.Recipes
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (recipe is null)
            return Results.NotFound();

        if (!string.IsNullOrWhiteSpace(recipe.SourceFileKey))
            await storage.DeleteAsync(recipe.SourceFileKey, ct);

        db.Recipes.Remove(recipe);
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}
