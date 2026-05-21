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

**`PipelineStage` enum** lives in `SousChef.Core/Models/PipelineError.cs` alongside the `PipelineError` record. `PipelineError.ToJson()` serializes to camelCase JSON for storage in the `error` column. Stages: `Download`, `DocumentExtraction`, `RecipeValidation`, `LlmExtraction`, `JsonParsing`.

**`PdfDocumentExtractor`** in `SousChef.Infrastructure/Extraction/`: detects text vs image PDFs via PdfPig word-count heuristic (≥80% text pages → text path), extracts via PdfPig (text) or Docnet.Core + Tesseract OCR (image). Docnet BGRA bytes are converted to PNG via **SkiaSharp** (cross-platform, no libgdiplus required). `TESSDATA_PREFIX` is set via Aspire env var to `./tessdata`; `eng.traineddata` is gitignored and must be downloaded manually. **Native lib setup (macOS arm64, run once):** `make setup-native-libs` — installs Homebrew tesseract and creates Homebrew-path symlinks in `/opt/homebrew/lib` with the names the wrapper expects. The `SousChef.Api.csproj` `LinkTesseractNativeLibsMacOS` build target then symlinks `{outputDir}/x64/libleptonica-1.82.0.dylib` and `libtesseract41.dylib` into the build output after every build. `InteropDotNet` checks `{entryAssemblyDir}/x64/` first, so no `DYLD_LIBRARY_PATH` needed.

**`JobStatusHub`** at `/hubs/jobs` — broadcasts `JobStatusChanged`, `JobReadyForReview`, and `JobFailed` messages to all connected clients. Strongly-typed message records live in `SousChef.Core/Models/HubMessages.cs`. CORS policy `VueFrontend` allows `localhost:5173` and `souschef.aurlaw.dev` with credentials (required for SignalR WebSocket). Vue SignalR client deferred to Phase 6.

**`ExtractionBackgroundService`** pipeline (Phase 3a): Pending → Processing (claim) → Download (R2) → DocumentExtraction + RecipeValidation (PdfDocumentExtractor) → Review (success) or Failed (structured PipelineError). Hub notifications pushed at each stage transition. `IDocumentExtractor.ExtractTextAsync` returns `Error.Validation` code for keyword pre-filter failures, `Error.Internal` for extraction failures — the service uses `.Error.Code == "VALIDATION"` to set the correct `PipelineStage` on failure.

## Key Decisions and Constraints

- `apphost.cs` is a root-level file — not inside a project folder. Do not move or convert it to a `.csproj`-based project.
- `aspire.config.json` must not be modified; it is gitignored.
- `appsettings.Development.json` must not be gitignored (the brief explicitly requires it tracked).
- Secrets (API keys, storage credentials) live exclusively in .NET User Secrets for the `SousChef.Api` project. Auth0 credentials are deferred to Phase 5.
- The `vector(1536)` dimension is tied to `text-embedding-3-small`; `EmbeddingOptions.Dimensions` is the source of truth. The HNSW index on `recipes.embedding` is deferred to Phase 4.
- `ExtractionJob.Status` is stored as a `string` column (`.HasConversion<string>()`) with an index — the background worker queries by status frequently.


## Spec Files

Spec/brief files live in the Obsidian vault at the configured
Obsidian path before asking the user to paste content.

## Implementation

Before implementing, scan the brief for every external API call, library method,
and framework attribute. Verify each against local package inspection or docs.
Produce a one-paragraph audit note listing any discrepancies before writing code.