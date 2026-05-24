---
description: Fires before migration commands or edits to migration files — reminds about double-quoted PascalCase column names in raw SQL
events:
  - pre_tool_use
globs:
  - "SousChef.Infrastructure/Data/Migrations/**"
alwaysApply: false
---

# Hook: Pre-Migration Warning

## Trigger
Fires before any `dotnet ef migrations add` command or before creating/editing any file in `SousChef.Infrastructure/Data/Migrations/`.

## Warning

⚠️ **Raw SQL in migrations must use double-quoted PascalCase column and table names.**

EF Core does not apply snake_case naming conventions. PostgreSQL folds unquoted identifiers to lowercase, which will not match the actual column names.

**Always write:**
```sql
-- CORRECT
migrationBuilder.Sql(
    "CREATE INDEX IF NOT EXISTS ix_recipes_embedding_hnsw " +
    "ON \"Recipes\" USING hnsw (\"Embedding\" vector_cosine_ops)");

ALTER TABLE \"Recipes\" ADD COLUMN \"NewColumn\" text;
```

**Never write:**
```sql
-- WRONG — PostgreSQL folds to lowercase, no match
ON Recipes USING hnsw (Embedding vector_cosine_ops)
```

**Applies to:**
- `migrationBuilder.Sql()`
- `migrationBuilder.CreateIndex()` with raw SQL
- Any `FromSqlRaw` or ad-hoc SQL added in migrations

**Table names to remember:** `"Recipes"`, `"Ingredients"`, `"RecipeSteps"`, `"ExtractionJobs"`

**Column names to remember:** `"Id"`, `"UserId"`, `"Title"`, `"Embedding"`, `"CuisineType"`, `"TotalTimeMinutes"`, `"ExtractedText"`, `"ExtractedData"`, `"Status"`, `"CreatedAt"`, `"ProcessedAt"`
