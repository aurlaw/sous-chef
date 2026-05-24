# Hook: Pre-Search Service Warning

## Trigger
Fires before any edit to `SousChef.Infrastructure/Search/RecipeSearchService.cs` or `SousChef.Api/Endpoints/RecipeEndpoints.cs`.

## Warning

⚠️ **EmbeddingInputBuilder.Build() must be used for both indexing and query embedding.**

`EmbeddingInputBuilder.Build(RecipeDto recipe)` in `SousChef.Core/Common/EmbeddingInputBuilder.cs` defines the canonical way to build an embedding input string. It must be used in:

1. **Approve endpoint** — when indexing the approved recipe
2. **Search endpoint** — when embedding the user's query

Using different concatenation logic in either place puts the query vector and recipe vectors in different parts of the embedding space, causing poor or random search results.

```csharp
// CORRECT — both paths use the same builder
var embeddingInput = EmbeddingInputBuilder.Build(recipeDto);  // approve
var embeddingInput = EmbeddingInputBuilder.Build(queryAsRecipeDto);  // search (if applicable)

// WRONG — do not inline a different concatenation
var embeddingInput = $"{recipe.Title} {string.Join(" ", recipe.Tags)}";
```

**Also remember:**
- Raw SQL for pgvector uses `<=>` cosine distance operator (lower = more similar)
- All column names in raw SQL must be double-quoted PascalCase: `"Embedding"`, `"UserId"`, etc.
- `SemanticDistanceThreshold` is configurable in `appsettings.json` under `"Search"` — do not hardcode a threshold value
- `RecipeSearchResult` is a keyless entity registered via `HasNoKey().ToView(null)` — do not add a primary key
