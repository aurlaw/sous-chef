# SousChef

SousChef is a personal recipe management app that extracts structured recipes from PDF cookbooks using AI, stores them with semantic embeddings, and makes them searchable. Upload a cookbook, get back searchable, queryable recipes.

## Tech Stack

| Layer | Technology |
|---|---|
| Orchestration | .NET Aspire 13.3.3 |
| API | ASP.NET Core 10 (Minimal API) |
| Frontend | Vue 3 + TypeScript + Vite |
| Database | PostgreSQL 16 + pgvector |
| Object Storage | MinIO (S3-compatible) |
| PDF Extraction | Claude (Anthropic API) |
| Embeddings | OpenAI `text-embedding-3-small` (1536-dim) |
| ORM | EF Core 10 + Npgsql |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [.NET Aspire CLI](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/setup-tooling) (`dotnet workload install aspire`)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for Postgres, MinIO containers)
- [Node.js 20+](https://nodejs.org/) (for the Vue frontend)

## Getting Started

### 1. Clone and restore

```bash
git clone https://github.com/aurlaw/sous-chef.git
cd sous-chef
dotnet restore
cd sous-chef-web && npm install && cd ..
```

### 2. Add secrets

After restoring, set the required secrets for the API project:

```bash
dotnet user-secrets init --project SousChef.Api
dotnet user-secrets set "Storage:AccessKeyId"     "souschef"              --project SousChef.Api
dotnet user-secrets set "Storage:SecretAccessKey"  "souschef_secret"       --project SousChef.Api
dotnet user-secrets set "Extraction:ApiKey"        "<your Anthropic key>"  --project SousChef.Api
dotnet user-secrets set "Embedding:ApiKey"         "<your OpenAI key>"     --project SousChef.Api
```

### 3. Start the environment

```bash
make dev
# or: aspire start
```

Aspire starts all containers (Postgres + pgvector, MinIO) and launches both the API and the Vue dev server. Open the Aspire dashboard URL printed in the terminal.

### 4. Create the MinIO bucket (first run only)

1. Open the Aspire dashboard and click the MinIO console link (port 9001)
2. Log in with `souschef` / `souschef_secret`
3. Create a bucket named **`souschef`**

EF Core migrations run automatically on API startup in Development.

## Project Structure

```
sous-chef/
├── apphost.cs                  # Aspire AppHost — wires all services
├── aspire.config.json          # Aspire local run profiles
├── Makefile                    # dev / migrate targets
│
├── SousChef.Api/               # ASP.NET Core Minimal API
│   ├── Endpoints/              # Route handlers (added per phase)
│   ├── Workers/                # BackgroundService workers (added per phase)
│   └── Program.cs
│
├── SousChef.Core/              # Domain layer — no infrastructure dependencies
│   ├── Common/                 # Result<T>, Error, options classes
│   ├── DTOs/                   # RecipeDto, IngredientDto, RecipeStepDto
│   ├── Interfaces/             # IStorageService, IExtractionService, etc.
│   └── Models/                 # DocumentExtractionResult, ExtractionResult
│
├── SousChef.Infrastructure/    # Infrastructure implementations
│   ├── Data/                   # EF Core entities + SousChefDbContext
│   ├── Migrations/             # EF Core migrations
│   ├── Storage/                # MinIO / S3 (Phase 2)
│   ├── Extraction/             # Claude PDF extraction (Phase 3)
│   └── Embedding/              # OpenAI embeddings (Phase 3)
│
├── SousChef.ServiceDefaults/   # Aspire shared observability setup
│
└── sous-chef-web/              # Vue 3 + TypeScript frontend
```

## Development Commands

```bash
make dev        # Start full local environment (aspire start)
make migrate    # Run EF Core migrations manually
```

Manual migration (if needed):

```bash
dotnet ef migrations add <MigrationName> \
  --project SousChef.Infrastructure \
  --startup-project SousChef.Api

dotnet ef database update \
  --project SousChef.Infrastructure \
  --startup-project SousChef.Api
```

## Local Services

| Service | URL | Notes |
|---|---|---|
| Aspire Dashboard | printed at startup | Service health, logs, traces |
| API | `https://localhost:7xxx` | Aspire-assigned port |
| Scalar UI | `<api-url>/scalar/v1` | Interactive API docs |
| MinIO Console | `http://localhost:9001` | Object storage UI |
| Vue Dev Server | `http://localhost:5173` | Hot-reload frontend |

## Configuration

Non-secret config lives in `SousChef.Api/appsettings.json`. Secrets are injected via .NET User Secrets in Development (never committed).

| Secret key | Description |
|---|---|
| `Storage:AccessKeyId` | MinIO access key |
| `Storage:SecretAccessKey` | MinIO secret key |
| `Extraction:ApiKey` | Anthropic API key |
| `Embedding:ApiKey` | OpenAI API key |

Auth0 credentials are added in Phase 5.

## Architecture Notes

- **Result pattern** — all service methods return `Result<T>` or `Result`; no exceptions for business logic. `ResultExtensions.ToApiResult()` maps these to HTTP responses.
- **pgvector** — recipe embeddings are stored as `vector(1536)` columns. HNSW index is deferred to Phase 4 when search is implemented.
- **Extraction pipeline** — PDF upload → MinIO storage → Claude extraction → structured recipe + embedding → Postgres. Orchestrated by a `BackgroundService` worker polling `ExtractionJobs` by status.
- **EF Core migrations** — applied automatically on startup in Development. Production uses an explicit pre-deploy step (Phase 7).

## License

MIT
