# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Start full local stack (Aspire + all containers + Vue dev server)
make dev          # or: aspire start

# Build the .NET solution
dotnet build SousChef.sln

# Add an EF Core migration
dotnet ef migrations add <Name> \
  --project SousChef.Infrastructure \
  --startup-project SousChef.Api

# Apply migrations manually
make migrate

# Vue frontend (from sous-chef-web/)
npm run dev       # dev server
npm run build     # production build
```

There are no automated tests yet. When adding them, use `dotnet test` from the solution root.

```bash
# Test upload (replace port and path with actuals)
curl -X POST http://localhost:<port>/api/uploads \
  -F "file=@/path/to/recipe.pdf" \
  -H "Accept: application/json"

# Check job status
curl http://localhost:<port>/api/jobs/<job-id>
```

## Architecture

SousChef is a multi-project .NET Aspire solution. `apphost.cs` (root-level C# script, not inside a project folder) is the Aspire entry point — it wires all containers and projects. New Aspire hosting integrations are added there as `#:package` directives.

**Project dependency graph:**
```
SousChef.Api → SousChef.Core
SousChef.Api → SousChef.Infrastructure
SousChef.Api → SousChef.ServiceDefaults
SousChef.Infrastructure → SousChef.Core
```

**`SousChef.Core`** has no infrastructure dependencies. All service contracts are interfaces in `Interfaces/`; all service return types use `Result<T>` / `Result` (never throw for business logic). `ResultExtensions.ToApiResult()` maps results to `IResult` HTTP responses.

**`SousChef.Infrastructure`** contains EF Core entities, `SousChefDbContext`, and stub service folders (`Storage/`, `Extraction/`, `Embedding/`) ready to receive implementations. DI registration is in `ServiceCollectionExtensions.AddSousChefInfrastructure()`. New service implementations go in their respective subfolder and are wired by uncommenting the stub lines in that method.

**`SousChef.Api`** uses Minimal API. New routes belong in `Endpoints/` as static extension classes on `IEndpointRouteBuilder`, registered in `Program.cs` via `app.Map*Endpoints()`. Background workers belong in `Workers/` and are registered with `AddHostedService<T>()`. `UseAntiforgery()` is required in the middleware pipeline for `IFormFile` endpoints; individual file-upload routes call `.DisableAntiforgery()` since no CSRF token is involved.

- Upload endpoint streams directly from `IFormFile` to `IStorageService` — no temp file on disk
- `BackgroundService` uses `IServiceScopeFactory` for scoped DI (EF Core `DbContext` is scoped, never inject it directly into a singleton/hosted service)
- One job claimed per polling cycle intentionally — simple and debuggable; batching deferred to later phases
- `PlaceholderUserId = "anonymous"` used in `UploadEndpoints` until Phase 5 Auth0 integration; swap the source of the value only, no structural change required

**pgvector wiring**: `Pgvector.EntityFrameworkCore 0.3.0` (built against Npgsql 9.x) has a partial incompatibility with Npgsql 10.x. The workaround in `ServiceCollectionExtensions` is: build `NpgsqlDataSource` with `NpgsqlDataSourceBuilder.UseVector()` (base `Pgvector` package, Npgsql-10-compatible), pass the pre-built source to `UseNpgsql(dataSource)`, then call `o.UseVector()` on the EF options builder for EF model-level type mapping only — the data-source-builder path inside `o.UseVector()` is never invoked when a pre-built source is used. Do not change this pattern without verifying pgvector EF compatibility with the installed Npgsql version.

**Connection strings**: Aspire injects the Postgres connection string at runtime as `ConnectionStrings__souschef`. The key in `appsettings.json` and `GetConnectionString(...)` calls must be `"souschef"` to match the Aspire database resource name.

**Migrations**: EF Core migrations are applied automatically in `Program.cs` when `ASPNETCORE_ENVIRONMENT=Development`. The `--startup-project` must always be `SousChef.Api` because that project holds `appsettings.json` with the connection string and EF Design tooling.

**Serilog + OTEL**: `builder.Host.UseSerilog(...)` configured in `Program.cs` before `builder.Build()`. Writes to console and to the Aspire OTEL sink — structured logs appear in the Aspire dashboard under the `api` resource. `OTEL_EXPORTER_OTLP_ENDPOINT` env var controls the sink endpoint; Aspire injects it automatically.

**`extracted_text`** (text column on `extraction_jobs`) — raw PDF extraction output set in Phase 3a, never overwritten. `extracted_data` (jsonb) remains null until Phase 3b.

**`PipelineStage` enum** lives in `SousChef.Core/Models/PipelineError.cs` alongside the `PipelineError` record. `PipelineError.ToJson()` serializes to camelCase JSON for storage in the `error` column. Stages: `Download`, `DocumentExtraction`, `RecipeValidation`, `LlmExtraction`, `NotARecipe`, `JsonParsing`.

**`PdfDocumentExtractor`** in `SousChef.Infrastructure/Extraction/`: detects text vs image PDFs via PdfPig word-count heuristic (≥80% text pages → text path), extracts via PdfPig (text) or Docnet.Core + Tesseract OCR (image). Docnet returns raw BGRA bytes; these are converted to PNG via **SkiaSharp** before passing to Tesseract `Pix.LoadFromMemory` — no libgdiplus required.

**Tesseract native libs (macOS — run once):** `make setup-native-libs` installs Homebrew tesseract. The `LinkTesseractNativeLibsMacOS` MSBuild target in `SousChef.Api.csproj` then runs after every build on macOS and creates two symlinks in `{outputDir}/x64/`:
- `libleptonica-1.82.0.dylib` → `/opt/homebrew/lib/libleptonica.6.dylib`
- `libtesseract50.dylib` → `/opt/homebrew/lib/libtesseract.5.dylib`

These names match exactly what `InteropDotNet` (embedded in the `Tesseract` NuGet package) looks for — derived from the Windows DLL names `leptonica-1.82.0.dll` / `tesseract50.dll` that ship in the package's `x64/` folder. `InteropDotNet` checks `{entryAssemblyDir}/x64/` before falling back to a bare `dlopen`, so no `DYLD_LIBRARY_PATH` is needed and the Aspire env var approach does not work (macOS Hardened Runtime strips it or the process chain drops it before it reaches `dlopen`).

**`TESSDATA_PREFIX`** is set via Aspire env var to `./tessdata`; `eng.traineddata` is gitignored and must be downloaded manually (`make setup-native-libs` does not download it).

**`JobStatusHub`** at `/hubs/jobs` — broadcasts `JobStatusChanged`, `JobReadyForReview`, `JobFailed`, and `JobInvalidContent` messages to all connected clients. Strongly-typed message records live in `SousChef.Core/Models/HubMessages.cs`. CORS policy `VueFrontend` allows `localhost:5173` and `souschef.aurlaw.dev` with credentials (required for SignalR WebSocket). Vue SignalR client deferred to Phase 6.

**`ExtractionBackgroundService`** pipeline (Phase 3b): Pending → Processing (claim) → reads `extracted_text` from DB → LLM extraction (Claude) → Review (success, `extracted_data` populated) or `InvalidContent` (Claude returned not-a-recipe sentinel) or Failed (hard error). Phase 3b does NOT re-download from R2 or re-extract from PDF — it reads the `extracted_text` column set in Phase 3a. Hub pushes `JobInvalidContent` with Claude's reason and the raw `extracted_text` when not-a-recipe is detected.

**`LlmExtractionService`** in `SousChef.Infrastructure/Extraction/`: sends `extracted_text` to Claude via the `"anthropic"` named HttpClient (timeout 120s). Detects the `{"error":"not_a_recipe","reason":"..."}` sentinel before `RecipeDto` deserialization. Uses `JsonNamingPolicy.SnakeCaseLower` + `PropertyNameCaseInsensitive` to deserialize snake_case Claude JSON into the existing `RecipeDto` positional records. Token usage logged as structured log on each call.

**`LlmEmbeddingService`** in `SousChef.Infrastructure/Embedding/`: sends text to OpenAI embeddings API via the `"openai"` named HttpClient (timeout 30s). Registered but not called in Phase 3b — wired in Phase 4 approve endpoint.

**`ExtractionJobStatus.InvalidContent`** — Claude determined the document is not a recipe. Not a hard failure; user can reject (delete) or override (proceed to edit form, Phase 4). The `error` column stores a structured `PipelineError` with stage `NotARecipe` and Claude's reason as the detail.

**Anthropic HttpClient**: named `"anthropic"`, base address from `Extraction:Endpoint` config (default `https://api.anthropic.com`), timeout 120s.
**OpenAI HttpClient**: named `"openai"`, base address from `Embedding:Endpoint` config (default `https://api.openai.com`), timeout 30s.

**`EmbeddingInputBuilder.Build()`** — static helper in `SousChef.Core/Common/` — builds embedding input string from `RecipeDto` (title, description, tags, ingredient names joined with `. `). Used by approve endpoint and Phase 4b query embedding for consistency.

**Approve endpoint** (`PATCH /api/jobs/{id}/approve`): reads `extracted_data` from job, deserializes to `RecipeDto`, builds embedding, calls `IEmbeddingService`, writes recipe + job status update in a single `SaveChangesAsync()` — both succeed or both fail. `TotalTimeMinutes` computed as prep + cook at approve time (nulls treated as 0). Only valid for `Review` status.

**Reject endpoint** (`DELETE /api/jobs/{id}/reject`): valid for `Review`, `InvalidContent`, and `Failed` statuses. Calls `IStorageService.DeleteAsync` (result not checked — file may already be gone), then marks job `Rejected`.

**Retry endpoint** (`PATCH /api/jobs/{id}/retry`): only valid for `Failed` jobs. Clears `error`, `extracted_data`, `processed_at`; preserves `extracted_text` and `attempts`. Returns `409` if `attempts >= 3` (`MaxRetryAttempts = 3` constant in `JobEndpoints`). BackgroundService skips PDF extraction when `extracted_text` is populated.

**Delete recipe endpoint** (`DELETE /api/recipes/{id}`): removes recipe row (EF Core cascade removes ingredients and steps via `OnDelete(DeleteBehavior.Cascade)`), then deletes source PDF from storage.

**HNSW index** on `recipes.embedding` added in migration `AddHnswIndexToRecipeEmbedding` via raw SQL (`vector_cosine_ops` matches cosine similarity used in Phase 4b search).

## Key Decisions and Constraints

- `apphost.cs` is a root-level file — not inside a project folder. Do not move or convert it to a `.csproj`-based project.
- `aspire.config.json` must not be modified; it is gitignored.
- `appsettings.Development.json` must not be gitignored (the brief explicitly requires it tracked).
- Secrets (API keys, storage credentials) live exclusively in .NET User Secrets for the `SousChef.Api` project. Auth0 credentials are deferred to Phase 5.
- The `vector(1536)` dimension is tied to `text-embedding-3-small`; `EmbeddingOptions.Dimensions` is the source of truth. The HNSW index on `recipes.embedding` was added in Phase 4a.
- `ExtractionJob.Status` is stored as a `string` column (`.HasConversion<string>()`) with an index — the background worker queries by status frequently.


## Spec Files

Spec/brief files live in the Obsidian vault at the configured
Obsidian path before asking the user to paste content.

## Implementation

Before implementing, scan the brief for every external API call, library method,
and framework attribute. Verify each against local package inspection or docs.
Produce a one-paragraph audit note listing any discrepancies before writing code.