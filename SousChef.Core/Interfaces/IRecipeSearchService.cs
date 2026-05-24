using SousChef.Core.Common;
using SousChef.Core.DTOs;

namespace SousChef.Core.Interfaces;

public interface IRecipeSearchService
{
    Task<Result<PagedResult<RecipeSummaryDto>>> GetRecipesAsync(
        string userId, string? cursor, int pageSize, CancellationToken ct = default);

    Task<Result<PagedResult<RecipeSummaryDto>>> SearchRecipesAsync(
        string userId, SearchRequest request, CancellationToken ct = default);

    Task<Result<RecipeDetailDto>> GetRecipeByIdAsync(
        Guid id, string userId, CancellationToken ct = default);

    Task<Result<FacetsResponse>> GetFacetsAsync(
        string userId, CancellationToken ct = default);
}
