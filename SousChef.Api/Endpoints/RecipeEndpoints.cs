using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SousChef.Core.Common;
using SousChef.Core.DTOs;
using SousChef.Core.Interfaces;
using SousChef.Infrastructure.Data;

namespace SousChef.Api.Endpoints;

public static class RecipeEndpoints
{
    private const string PlaceholderUserId = "anonymous";

    public static IEndpointRouteBuilder MapRecipeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapDelete("/api/recipes/{id:guid}", DeleteRecipe)
            .WithName("DeleteRecipe")
            .WithSummary("Delete a recipe and its source PDF")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        app.MapGet("/api/recipes", GetRecipes)
            .WithName("GetRecipes")
            .WithSummary("Get all recipes (alphabetical, cursor paginated)")
            .Produces<PagedResult<RecipeSummaryDto>>();

        app.MapGet("/api/recipes/search", SearchRecipes)
            .WithName("SearchRecipes")
            .WithSummary("Hybrid search — facets + semantic (optional)")
            .Produces<PagedResult<RecipeSummaryDto>>()
            .Produces(StatusCodes.Status400BadRequest);

        app.MapGet("/api/recipes/facets", GetFacets)
            .WithName("GetFacets")
            .WithSummary("Get distinct facet filter values (cached 60s)")
            .Produces<FacetsResponse>();

        app.MapGet("/api/recipes/{id:guid}", GetRecipeById)
            .WithName("GetRecipeById")
            .WithSummary("Get full recipe detail")
            .Produces<RecipeDetailDto>()
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> DeleteRecipe(
        Guid id,
        SousChefDbContext db,
        IStorageService storage,
        IMemoryCache cache,
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

        cache.Remove($"recipe-facets-{PlaceholderUserId}");

        return Results.NoContent();
    }

    private static async Task<IResult> GetRecipes(
        IRecipeSearchService searchService,
        string? cursor,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await searchService.GetRecipesAsync(PlaceholderUserId, cursor, pageSize, ct);
        return result.ToApiResult();
    }

    private static async Task<IResult> SearchRecipes(
        IRecipeSearchService searchService,
        string? query,
        string? cuisineTypes,
        string? courses,
        string? difficulties,
        int? maxTotalMinutes,
        string? tags,
        string? includeIngredients,
        string? excludeIngredients,
        string? cursor,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var filters = new SearchFilters(
            ParseList(cuisineTypes),
            ParseList(courses),
            ParseList(difficulties),
            maxTotalMinutes,
            ParseList(tags),
            ParseList(includeIngredients),
            ParseList(excludeIngredients));

        var request = new SearchRequest(query, filters, cursor, pageSize);
        var result = await searchService.SearchRecipesAsync(PlaceholderUserId, request, ct);
        return result.ToApiResult();
    }

    private static async Task<IResult> GetFacets(
        IRecipeSearchService searchService,
        IMemoryCache cache,
        CancellationToken ct = default)
    {
        var cacheKey = $"recipe-facets-{PlaceholderUserId}";

        if (cache.TryGetValue(cacheKey, out FacetsResponse? cached))
            return Results.Ok(cached);

        var result = await searchService.GetFacetsAsync(PlaceholderUserId, ct);
        if (!result.IsSuccess)
            return result.ToApiResult();

        cache.Set(cacheKey, result.Value, TimeSpan.FromSeconds(60));
        return Results.Ok(result.Value);
    }

    private static async Task<IResult> GetRecipeById(
        Guid id,
        IRecipeSearchService searchService,
        CancellationToken ct = default)
    {
        var result = await searchService.GetRecipeByIdAsync(id, PlaceholderUserId, ct);
        return result.ToApiResult();
    }

    private static List<string>? ParseList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                   .ToList();
}
