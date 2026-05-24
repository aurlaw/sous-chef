using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using Pgvector;
using SousChef.Core.Common;
using SousChef.Core.DTOs;
using SousChef.Core.Interfaces;
using SousChef.Infrastructure.Data;

namespace SousChef.Infrastructure.Search;

public class RecipeSearchService(
    SousChefDbContext db,
    IEmbeddingService embeddingService,
    IOptions<SearchOptions> searchOptions,
    ILogger<RecipeSearchService> logger) : IRecipeSearchService
{
    private readonly SousChefDbContext _db = db;
    private readonly IEmbeddingService _embeddingService = embeddingService;
    private readonly SearchOptions _searchOptions = searchOptions.Value;
    private readonly ILogger<RecipeSearchService> _logger = logger;

    public async Task<Result<PagedResult<RecipeSummaryDto>>> GetRecipesAsync(
        string userId, string? cursor, int pageSize, CancellationToken ct = default)
    {
        pageSize = Math.Min(pageSize, _searchOptions.MaxPageSize);
        var decoded = SearchCursor.Decode(cursor);

        var query = _db.Recipes
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .AsQueryable();

        if (decoded is not null)
        {
            query = query.Where(r =>
                r.Title.CompareTo(decoded.Title) > 0 ||
                (r.Title == decoded.Title && r.Id.CompareTo(decoded.Id) > 0));
        }

        var items = await query
            .OrderBy(r => r.Title)
            .ThenBy(r => r.Id)
            .Take(pageSize + 1)
            .Select(r => new RecipeSummaryDto(
                r.Id, r.Title, r.CuisineType, r.Course, r.Difficulty,
                r.TotalTimeMinutes, r.Servings, r.Tags, r.CreatedAt))
            .ToListAsync(ct);

        var hasMore = items.Count > pageSize;
        if (hasMore) items.RemoveAt(items.Count - 1);

        string? nextCursor = null;
        if (hasMore && items.Count > 0)
        {
            var last = items[^1];
            nextCursor = new SearchCursor(last.Title, null, last.Id).Encode();
        }

        return Result<PagedResult<RecipeSummaryDto>>.Success(
            new PagedResult<RecipeSummaryDto>(items, nextCursor, pageSize, hasMore));
    }

    public async Task<Result<PagedResult<RecipeSummaryDto>>> SearchRecipesAsync(
        string userId, SearchRequest request, CancellationToken ct = default)
    {
        var pageSize = Math.Min(request.PageSize, _searchOptions.MaxPageSize);
        var decoded = SearchCursor.Decode(request.Cursor);

        if (request.Query?.Length > 500)
            return Result<PagedResult<RecipeSummaryDto>>.Failure(
                Error.Validation("Search query must be 500 characters or fewer."));

        var hasQuery = !string.IsNullOrWhiteSpace(request.Query);

        if (hasQuery)
        {
            var embeddingResult = await _embeddingService.GenerateAsync(request.Query!, ct);
            if (!embeddingResult.IsSuccess)
                return Result<PagedResult<RecipeSummaryDto>>.Failure(embeddingResult.Error!);

            var queryVector = new Vector(embeddingResult.Value!);
            var threshold = _searchOptions.SemanticDistanceThreshold;
            var sql = BuildSemanticSql(request.Filters, decoded, pageSize + 1);
            var parameters = BuildSqlParameters(userId, request.Filters, decoded, queryVector, threshold);

            var rows = await _db.Set<RecipeSearchResult>()
                .FromSqlRaw(sql, parameters)
                .ToListAsync(ct);

            var hasMore = rows.Count > pageSize;
            if (hasMore) rows.RemoveAt(rows.Count - 1);

            string? nextCursor = null;
            if (hasMore && rows.Count > 0)
            {
                var last = rows[^1];
                nextCursor = new SearchCursor(null, last.Distance, last.Id).Encode();
            }

            _logger.LogInformation(
                "Semantic search for '{Query}' (threshold: {Threshold}): {Count} results",
                request.Query, threshold, rows.Count);

            return Result<PagedResult<RecipeSummaryDto>>.Success(
                new PagedResult<RecipeSummaryDto>(
                    rows.Select(ToSummaryDto).ToList(),
                    nextCursor, pageSize, hasMore));
        }

        // Faceted-only path (no query)
        var facetQuery = _db.Recipes
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .AsQueryable();

        facetQuery = ApplyFacetFilters(facetQuery, request.Filters);

        if (decoded is not null)
        {
            facetQuery = facetQuery.Where(r =>
                r.Title.CompareTo(decoded.Title) > 0 ||
                (r.Title == decoded.Title && r.Id.CompareTo(decoded.Id) > 0));
        }

        var facetItems = await facetQuery
            .OrderBy(r => r.Title)
            .ThenBy(r => r.Id)
            .Take(pageSize + 1)
            .Select(r => new RecipeSummaryDto(
                r.Id, r.Title, r.CuisineType, r.Course, r.Difficulty,
                r.TotalTimeMinutes, r.Servings, r.Tags, r.CreatedAt))
            .ToListAsync(ct);

        var facetHasMore = facetItems.Count > pageSize;
        if (facetHasMore) facetItems.RemoveAt(facetItems.Count - 1);

        string? facetNextCursor = null;
        if (facetHasMore && facetItems.Count > 0)
        {
            var last = facetItems[^1];
            facetNextCursor = new SearchCursor(last.Title, null, last.Id).Encode();
        }

        return Result<PagedResult<RecipeSummaryDto>>.Success(
            new PagedResult<RecipeSummaryDto>(facetItems, facetNextCursor, pageSize, facetHasMore));
    }

    public async Task<Result<RecipeDetailDto>> GetRecipeByIdAsync(
        Guid id, string userId, CancellationToken ct = default)
    {
        var recipe = await _db.Recipes
            .AsNoTracking()
            .Include(r => r.Ingredients)
            .Include(r => r.Steps.OrderBy(s => s.StepNumber))
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, ct);

        if (recipe is null)
            return Result<RecipeDetailDto>.Failure(Error.NotFound("Recipe not found."));

        return Result<RecipeDetailDto>.Success(new RecipeDetailDto(
            recipe.Id,
            recipe.Title,
            recipe.Description,
            recipe.CuisineType,
            recipe.Course,
            recipe.Difficulty,
            recipe.PrepTimeMinutes,
            recipe.CookTimeMinutes,
            recipe.TotalTimeMinutes,
            recipe.Servings,
            recipe.Tags,
            recipe.CreatedAt,
            recipe.Ingredients.Select(i => new IngredientDetailDto(
                i.Id, i.Name, i.Quantity, i.Unit, i.Notes)).ToList(),
            recipe.Steps.Select(s => new StepDetailDto(
                s.Id, s.StepNumber, s.Instruction)).ToList()));
    }

    public async Task<Result<FacetsResponse>> GetFacetsAsync(
        string userId, CancellationToken ct = default)
    {
        var recipes = await _db.Recipes
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .Select(r => new { r.CuisineType, r.Course, r.Difficulty, r.Tags })
            .ToListAsync(ct);

        var cuisineTypes = recipes
            .Where(r => r.CuisineType is not null)
            .Select(r => r.CuisineType!)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        var courses = recipes
            .Where(r => r.Course is not null)
            .Select(r => r.Course!)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        var difficulties = recipes
            .Where(r => r.Difficulty is not null)
            .Select(r => r.Difficulty!)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        var tags = recipes
            .SelectMany(r => r.Tags)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        return Result<FacetsResponse>.Success(
            new FacetsResponse(cuisineTypes, courses, difficulties, tags));
    }

    private static IQueryable<Recipe> ApplyFacetFilters(
        IQueryable<Recipe> query, SearchFilters? filters)
    {
        if (filters is null) return query;

        if (filters.CuisineTypes?.Count > 0)
            query = query.Where(r => filters.CuisineTypes.Contains(r.CuisineType!));

        if (filters.Courses?.Count > 0)
            query = query.Where(r => filters.Courses.Contains(r.Course!));

        if (filters.Difficulties?.Count > 0)
            query = query.Where(r => filters.Difficulties.Contains(r.Difficulty!));

        if (filters.MaxTotalMinutes.HasValue)
            query = query.Where(r => r.TotalTimeMinutes <= filters.MaxTotalMinutes.Value);

        if (filters.Tags?.Count > 0)
            query = query.Where(r => r.Tags.Any(t => filters.Tags.Contains(t)));

        if (filters.IncludeIngredients?.Count > 0)
            query = query.Where(r =>
                r.Ingredients.Any(i => filters.IncludeIngredients.Contains(i.Name)));

        if (filters.ExcludeIngredients?.Count > 0)
            query = query.Where(r =>
                !r.Ingredients.Any(i => filters.ExcludeIngredients.Contains(i.Name)));

        return query;
    }

    private static string BuildSemanticSql(
        SearchFilters? filters,
        SearchCursor? cursor,
        int take)
    {
        var sb = new StringBuilder();
        sb.AppendLine(@"
            SELECT r.""Id"", r.""Title"", r.""CuisineType"",
                   r.""Course"", r.""Difficulty"",
                   r.""TotalTimeMinutes"", r.""Servings"",
                   r.""Tags"", r.""CreatedAt"",
                   (r.""Embedding"" <=> @queryVector)::real AS ""Distance""
            FROM ""Recipes"" r
            WHERE r.""UserId"" = @userId");

        if (filters?.CuisineTypes?.Count > 0)
            sb.AppendLine(@"AND r.""CuisineType"" = ANY(@cuisineTypes)");
        if (filters?.Courses?.Count > 0)
            sb.AppendLine(@"AND r.""Course"" = ANY(@courses)");
        if (filters?.Difficulties?.Count > 0)
            sb.AppendLine(@"AND r.""Difficulty"" = ANY(@difficulties)");
        if (filters?.MaxTotalMinutes.HasValue == true)
            sb.AppendLine(@"AND r.""TotalTimeMinutes"" <= @maxTotalMinutes");
        if (filters?.Tags?.Count > 0)
            sb.AppendLine(@"AND r.""Tags"" && @tags");
        if (filters?.IncludeIngredients?.Count > 0)
            sb.AppendLine(@"AND EXISTS (SELECT 1 FROM ""Ingredients"" i WHERE i.""RecipeId"" = r.""Id"" AND i.""Name"" = ANY(@includeIngredients))");
        if (filters?.ExcludeIngredients?.Count > 0)
            sb.AppendLine(@"AND NOT EXISTS (SELECT 1 FROM ""Ingredients"" i WHERE i.""RecipeId"" = r.""Id"" AND i.""Name"" = ANY(@excludeIngredients))");

        sb.AppendLine(@"AND (r.""Embedding"" <=> @queryVector)::real <= @distanceThreshold");

        if (cursor?.Distance is not null)
            sb.AppendLine(@"
                AND ((r.""Embedding"" <=> @queryVector)::real > @cursorDistance
                 OR ((r.""Embedding"" <=> @queryVector)::real = @cursorDistance AND r.""Id"" > @cursorId))");

        sb.AppendLine($@"ORDER BY ""Distance"" ASC, r.""Id"" ASC");
        sb.AppendLine($"LIMIT {take}");

        return sb.ToString();
    }

    private static NpgsqlParameter[] BuildSqlParameters(
        string userId,
        SearchFilters? filters,
        SearchCursor? cursor,
        Vector queryVector,
        float distanceThreshold)
    {
        var parameters = new List<NpgsqlParameter>
        {
            new("userId", userId),
            new("queryVector", queryVector),
            new("distanceThreshold", distanceThreshold)
        };

        if (filters?.CuisineTypes?.Count > 0)
            parameters.Add(new NpgsqlParameter("cuisineTypes", NpgsqlDbType.Array | NpgsqlDbType.Text)
                { Value = filters.CuisineTypes.ToArray() });

        if (filters?.Courses?.Count > 0)
            parameters.Add(new NpgsqlParameter("courses", NpgsqlDbType.Array | NpgsqlDbType.Text)
                { Value = filters.Courses.ToArray() });

        if (filters?.Difficulties?.Count > 0)
            parameters.Add(new NpgsqlParameter("difficulties", NpgsqlDbType.Array | NpgsqlDbType.Text)
                { Value = filters.Difficulties.ToArray() });

        if (filters?.MaxTotalMinutes.HasValue == true)
            parameters.Add(new NpgsqlParameter("maxTotalMinutes", filters.MaxTotalMinutes.Value));

        if (filters?.Tags?.Count > 0)
            parameters.Add(new NpgsqlParameter("tags", NpgsqlDbType.Array | NpgsqlDbType.Text)
                { Value = filters.Tags.ToArray() });

        if (filters?.IncludeIngredients?.Count > 0)
            parameters.Add(new NpgsqlParameter("includeIngredients", NpgsqlDbType.Array | NpgsqlDbType.Text)
                { Value = filters.IncludeIngredients.ToArray() });

        if (filters?.ExcludeIngredients?.Count > 0)
            parameters.Add(new NpgsqlParameter("excludeIngredients", NpgsqlDbType.Array | NpgsqlDbType.Text)
                { Value = filters.ExcludeIngredients.ToArray() });

        if (cursor?.Distance is not null)
        {
            parameters.Add(new NpgsqlParameter("cursorDistance", cursor.Distance.Value));
            parameters.Add(new NpgsqlParameter("cursorId", cursor.Id));
        }

        return [.. parameters];
    }

    private static RecipeSummaryDto ToSummaryDto(RecipeSearchResult r) =>
        new(r.Id, r.Title, r.CuisineType, r.Course, r.Difficulty,
            r.TotalTimeMinutes, r.Servings, r.Tags, r.CreatedAt);
}
