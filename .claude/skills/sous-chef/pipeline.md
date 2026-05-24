# Pipeline Skill

## When to use this skill
Load this skill when working on any of the following:
- `SousChef.Api/Workers/ExtractionBackgroundService.cs`
- `SousChef.Api/Endpoints/JobEndpoints.cs`
- Any code touching `ExtractionJob`, `ExtractionJobStatus`, or job state transitions
- Approve, reject, retry endpoints

---

## Job Status Lifecycle

```
Pending → Processing → Review → Completed
                     → InvalidContent → Rejected
                     → Failed → Rejected (or Pending via retry, max 3)
```

## BackgroundService Responsibilities

Owns ONLY the automated portion:
```
Pending → Processing → Review | InvalidContent | Failed
```

Everything after `Review` is human-triggered via API endpoints.

## Pipeline Stages (Phase 3b)

BackgroundService checks `extracted_text` on pickup:
- **Populated** → skip download + PDF extraction, go straight to LLM
- **Empty** → full pipeline: Download → DocumentExtraction → RecipeValidation → LlmExtraction → JsonParsing

This is the retry path — `PATCH /api/jobs/{id}/retry` preserves `extracted_text`.

## ExtractionJobStatus Enum

```csharp
Pending | Processing | Review | InvalidContent | Approved | Rejected | Completed | Failed
```

Stored as string via `.HasConversion<string>()`. Index on `Status` column — BackgroundService queries by status frequently.

## API Endpoint Responsibilities

| Endpoint | Valid statuses | What it does |
|---|---|---|
| `PATCH /jobs/{id}` | Review | Save edits to `extracted_data` |
| `PATCH /jobs/{id}/approve` | Review | Embed → write recipe → Completed |
| `DELETE /jobs/{id}/reject` | Review, InvalidContent, Failed | Delete R2 file → Rejected |
| `PATCH /jobs/{id}/retry` | Failed | Reset to Pending, cap at 3 attempts |
| `DELETE /recipes/{id}` | Any | Cascade delete + R2 file cleanup |

## Approve Endpoint Detail

1. Deserialize `extracted_data` → `RecipeDto`
2. `EmbeddingInputBuilder.Build(recipeDto)` → embedding input string
3. `IEmbeddingService.GenerateAsync()` → `float[]`
4. Write `Recipe` + `Ingredient[]` + `RecipeStep[]` + update job status in ONE `SaveChangesAsync()` — both succeed or both fail
5. `TotalTimeMinutes` = prep + cook (nulls treated as 0)
6. Push `JobStatusChanged` hub notification

**If embedding fails**: return error, job stays in `Review`. No partial DB write.

## Retry Rules

- Only valid for `Failed` status — 409 if other status
- `MaxRetryAttempts = 3` constant in `JobEndpoints`
- 409 if `job.Attempts >= 3`
- Clears: `error`, `extracted_data`, `processed_at`
- Preserves: `extracted_text`, `attempts` (keeps incrementing toward cap)

## Reject Rules

- Valid for `Review`, `InvalidContent`, `Failed`
- Storage delete failure is WARNING not error — job is rejected regardless
- File may already be gone

## Hub Notifications

Push on every status transition from API endpoints:
- Approve → `JobStatusChanged` (Completed)
- Reject → `JobStatusChanged` (Rejected)
- Retry → `JobStatusChanged` (Pending) with message indicating full or LLM-only retry

## PlaceholderUserId

`"anonymous"` — used in all endpoints until Phase 5. Replace only the source of the value, no structural changes needed.

## extracted_text vs extracted_data

| Column | Set by | Cleared on retry? |
|---|---|---|
| `extracted_text` | Phase 3a BackgroundService | Never |
| `extracted_data` | Phase 3b BackgroundService + save edits endpoint | Yes |
