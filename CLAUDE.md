# CLAUDE.md

Domain-specific knowledge has been moved to skills in `.claude/skills/sous-chef/`. Reference those files for detail on specific subsystems.

---

## Commands

```bash
# Start full local stack
make dev          # or: aspire run

# Build
dotnet build SousChef.sln

# Add EF Core migration
dotnet ef migrations add <Name> \
  --project SousChef.Infrastructure \
  --startup-project SousChef.Api

# Apply migrations manually
make migrate

# Vue frontend (from sous-chef-web/)
npm run dev
npm run build

# Test upload
curl -X POST http://localhost:<port>/api/uploads \
  -F "file=@/path/to/recipe.pdf" \
  -H "Accept: application/json"

# Check job status
curl http://localhost:<port>/api/jobs/<job-id>
```

---

## Project Structure

```
SousChef.Api → SousChef.Core
SousChef.Api → SousChef.Infrastructure
SousChef.Api → SousChef.ServiceDefaults
SousChef.Infrastructure → SousChef.Core
```

- **`SousChef.Core`** — interfaces (`Interfaces/`), DTOs, models, Result types, Options classes. No infrastructure dependencies.
- **`SousChef.Infrastructure`** — EF Core, service implementations. DI registration in `ServiceCollectionExtensions.AddSousChefInfrastructure()`.
- **`SousChef.Api`** — Minimal API. Routes in `Endpoints/` as static `IEndpointRouteBuilder` extensions. Workers in `Workers/`. `UseAntiforgery()` in pipeline; file-upload routes call `.DisableAntiforgery()`.

---

## Non-Negotiable Constraints

These apply to every task regardless of context. Never violate them.

### 1. Raw SQL — PascalCase double-quoted column names
EF Core does not apply snake_case. All table and column names are PascalCase and **must be double-quoted** in raw SQL:
```sql
-- CORRECT
SELECT r."Id", r."Title", r."CuisineType" FROM "Recipes" r

-- WRONG — PostgreSQL folds to lowercase, no match
SELECT r.Id, r.Title, r.CuisineType FROM Recipes r
```
Applies to migrations, `FromSqlRaw`, and any ad-hoc SQL.

### 2. pgvector / Npgsql 10.x workaround
`Pgvector.EntityFrameworkCore 0.3.0` has a partial incompatibility with Npgsql 10.x. The established workaround in `ServiceCollectionExtensions`:
- Build `NpgsqlDataSource` with `NpgsqlDataSourceBuilder.UseVector()`
- Pass pre-built source to `UseNpgsql(dataSource)`
- Call `o.UseVector()` on the EF options builder for model-level type mapping only

**Do not change this pattern** without verifying pgvector/Npgsql compatibility.

### 3. Connection string key
Aspire injects Postgres at runtime as `ConnectionStrings__souschef`. Always use `GetConnectionString("souschef")` — never `"DefaultConnection"` or any other key.

### 4. Migrations always use SousChef.Api as startup project
```bash
dotnet ef migrations add <Name> \
  --project SousChef.Infrastructure \
  --startup-project SousChef.Api
```
`SousChef.Api` holds `appsettings.json` and EF Design tooling.

### 5. apphost.cs is root-level
`apphost.cs` is a root-level C# script, not inside a project folder. Do not move or convert it to a `.csproj`-based project. New Aspire integrations added as `#:package` directives.

### 6. aspire.config.json
Do not modify. It is gitignored.

### 7. appsettings.Development.json
Must NOT be gitignored — it contains non-sensitive dev config and must be tracked.

### 8. Secrets
All sensitive values (API keys, storage credentials, Auth0) live exclusively in .NET User Secrets for `SousChef.Api`. Nothing sensitive in `appsettings.json` or git.

### 9. EmbeddingInputBuilder consistency
`EmbeddingInputBuilder.Build()` must be used for BOTH recipe indexing (approve endpoint) AND query embedding (search). Same method = same embedding space. Never inline a different concatenation.

### 10. Result<T> pattern
All service return types use `Result<T>` / `Result`. Never throw for business logic. `ResultExtensions.ToApiResult()` maps to `IResult` HTTP responses.

### 11. PlaceholderUserId
`"anonymous"` in all endpoints until Phase 5. Replace only the source of the value — no structural changes.

### 12. BackgroundService scoped DI
`ExtractionBackgroundService` uses `IServiceScopeFactory` for `DbContext` access. Never inject `DbContext` directly into a singleton/hosted service. `IHubContext<JobStatusHub>` is singleton-safe and can be injected directly.

---

## Serilog + OTEL

`builder.Host.UseSerilog(...)` in `Program.cs` before `builder.Build()`. Writes to console and Aspire OTEL sink. `OTEL_EXPORTER_OTLP_ENDPOINT` injected automatically by Aspire.

---

## Domain Skills

Load the relevant skill file when working on a specific subsystem:

| Subsystem | Skill file |
|---|---|
| PDF extraction, Tesseract, OCR, PipelineStage | `.claude/skills/sous-chef/pdf-extraction.md` |
| SignalR hub, hub messages, CORS | `.claude/skills/sous-chef/signalr.md` |
| Search, pgvector queries, cursor pagination, facets | `.claude/skills/sous-chef/search.md` |
| BackgroundService pipeline, job status, approve/reject/retry | `.claude/skills/sous-chef/pipeline.md` |

---

## Spec Files

Brief files live in the Obsidian vault at the configured Obsidian path. Check the vault before asking the user to paste content.

## Implementation

Before implementing, scan the brief for every external API call, library method, and framework attribute. Verify each against local package inspection or docs. Produce a one-paragraph audit note listing any discrepancies before writing code.
