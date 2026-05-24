---
description: Search API — RecipeSearchService, pgvector semantic SQL, EmbeddingInputBuilder consistency, SearchOptions/threshold, cursor pagination, facets cache, raw SQL naming, RecipeSearchResult keyless entity
globs:
  - "SousChef.Infrastructure/Search/**"
  - "SousChef.Api/Endpoints/RecipeEndpoints.cs"
  - "SousChef.Core/Common/SearchCursor.cs"
  - "SousChef.Core/Common/SearchOptions.cs"
  - "SousChef.Core/Common/EmbeddingInputBuilder.cs"
alwaysApply: false
---

# Search Skill

## When to use this skill
Load this skill when working on any of the following:
- `SousChef.Infrastructure/Search/RecipeSearchService.cs`
- `SousChef.Api/Endpoints/RecipeEndpoints.cs` (search/list/facets/detail)
- `SousChef.Core/Common/SearchCursor.cs`
- `SousChef.Core/Common/EmbeddingInputBuilder.cs`
- `SousChef.Core/Common/SearchOptions.cs`
- Any code touching `recipes.embedding` or pgvector queries

---

## Architecture

All query logic lives in `RecipeSearchService` (`SousChef.Infrastructure/Search/`). Endpoint handlers are thin delegators — no query logic in handlers.

```
RecipeEndpoints → IRecipeSearchService → RecipeSearchService
                                              ↓
                                   SousChefDbContext + IEmbeddingService
```

## Search Paths

**Alphabetical list** (no query, no filters): pure LINQ, `OrderBy(r => r.Title).ThenBy(r => r.Id)`.

**Faceted-only** (no query, with filters): LINQ + `ApplyFacetFilters` helper.

**Semantic / hybrid** (with query): raw SQL required — pgvector `<=>` operator not supported in EF Core LINQ.

## EmbeddingInputBuilder — CRITICAL

`EmbeddingInputBuilder.Build()` must be used for BOTH:
1. Recipe indexing (approve endpoint)
2. Query embedding (search)

Same method = same embedding space = consistent similarity scores. Never inline a different concatenation.

## Semantic SQL

pgvector cosine distance operator: `<=>` (lower = more similar, range 0–2).

```sql
SELECT r."Id", r."Title", ..., (r."Embedding" <=> @queryVector)::real AS "Distance"
FROM "Recipes" r
WHERE r."UserId" = @userId
AND (r."Embedding" <=> @queryVector) <= @distanceThreshold
-- facet filters appended here
ORDER BY "Distance" ASC, r."Id" ASC
LIMIT @take
```

**Raw SQL naming**: all PostgreSQL column names are PascalCase and MUST be double-quoted. `"Recipes"`, `"Embedding"`, `"UserId"`, `"CuisineType"`, `"TotalTimeMinutes"` etc. Unquoted = lowercase fold = no match.

## RecipeSearchResult

Keyless entity in `SousChef.Infrastructure/Data/`, registered via `HasNoKey().ToView(null)` in `SousChefDbContext`. Used as projection type for `FromSqlRaw`. Distance cast to `::real` in SQL to match `float Distance` property.

## SearchOptions

`SousChef.Core/Common/SearchOptions.cs`, registered from `"Search"` config section:

```json
"Search": {
  "SemanticDistanceThreshold": 0.5,
  "DefaultPageSize": 20,
  "MaxPageSize": 50
}
```

Tune `SemanticDistanceThreshold` in `appsettings.json` without a code change:
- `< 0.3` — very strong match
- `0.3–0.5` — good match
- `0.5–0.7` — loose match
- `> 0.7` — likely irrelevant

## Cursor Pagination

`SearchCursor` in `SousChef.Core/Common/SearchCursor.cs` — Base64-encoded JSON.

| Mode | Cursor fields |
|---|---|
| Alphabetical | `(title, id)` |
| Semantic | `(distance, id)` |

`SearchCursor.Decode()` swallows errors and returns null — invalid cursor restarts from page 1.

Page fetch: `Take(pageSize + 1)` — extra item determines `HasMore`. Remove last item before returning.

## Facets Cache

`IMemoryCache`, TTL 60s, key `recipe-facets-{userId}`.

**Must evict on**:
- `ApproveJob` — after `SaveChangesAsync()`
- `DeleteRecipe` — after `SaveChangesAsync()`

```csharp
cache.Remove($"recipe-facets-{userId}");
```

`IMemoryCache` is process-local — fine for single-instance MVP.

## Query Params

Multi-value filters use comma-separated strings: `cuisineTypes=Italian,French`.
`ParseList` helper in `RecipeEndpoints` splits on `,` with trimming.
`query` param: max 500 chars — return 400 on violation before any embedding call.

## Endpoint Summary

| Method | Route | Notes |
|---|---|---|
| GET | `/api/recipes` | Alphabetical, cursor paginated |
| GET | `/api/recipes/search` | Hybrid, cursor paginated, 500 char limit |
| GET | `/api/recipes/facets` | Cached 60s |
| GET | `/api/recipes/{id}` | Full detail with ingredients + steps |
