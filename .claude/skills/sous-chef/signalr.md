---
description: SignalR hub — JobStatusHub, hub message types, CORS policy, IHubContext injection, push points in pipeline, Auth Phase 5 notes, Vue composable Phase 6
globs:
  - "SousChef.Api/Hubs/**"
  - "SousChef.Api/Workers/ExtractionBackgroundService.cs"
  - "SousChef.Api/Endpoints/JobEndpoints.cs"
alwaysApply: false
---

# SignalR Skill

## When to use this skill
Load this skill when working on any of the following:
- `SousChef.Api/Hubs/JobStatusHub.cs`
- `SousChef.Api/Workers/ExtractionBackgroundService.cs` (hub notification calls)
- Any endpoint that pushes hub notifications (approve, reject, retry)
- CORS configuration
- Vue SignalR client (`useJobHub` composable — Phase 6)

---

## Hub Overview

`JobStatusHub` at `/hubs/jobs` — broadcasts job status changes to all connected clients.

**Connection scope**: Admin page connects on mount for all pending jobs. Review page filters by job ID client-side.

**Transport**: WebSockets with SSE fallback. Single instance — no sticky session concern.

## Hub Message Types

All records live in `SousChef.Core/Models/HubMessages.cs`:

```csharp
public record JobStatusChanged(Guid JobId, string Status, string? Stage, string? Message);
public record JobReadyForReview(Guid JobId, string ExtractedData);
public record JobFailed(Guid JobId, string Stage, string Message, string? Detail);
public record JobInvalidContent(Guid JobId, string Reason, string ExtractedText);
```

## Pushing from BackgroundService

`IHubContext<JobStatusHub>` is injected into `ExtractionBackgroundService` via constructor (not `IServiceScopeFactory` — hub context is singleton-safe):

```csharp
await _hub.Clients.All.SendAsync("JobStatusChanged",
    new JobStatusChanged(job.Id, "Processing", "Download", "Downloading PDF..."), ct);
```

Push at each pipeline stage:
- Job claimed → `JobStatusChanged` (Processing)
- After PDF extraction → `JobStatusChanged` (stage: DocumentExtraction)
- After LLM extraction → `JobStatusChanged` (stage: LlmExtraction)
- On success → `JobReadyForReview` (with extractedData)
- On InvalidContent → `JobInvalidContent` (with reason + extractedText)
- On failure → `JobFailed` (with structured error)

## CORS Policy

Named `"VueFrontend"`, configured in `Program.cs`:

```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("VueFrontend", policy =>
        policy.WithOrigins(
                "http://localhost:5173",
                "https://souschef.aurlaw.dev")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());  // required for SignalR WebSocket
});

// Middleware order matters:
app.UseCors("VueFrontend");  // before MapHub
app.MapHub<JobStatusHub>("/hubs/jobs");
```

## Registration in Program.cs

```csharp
builder.Services.AddSignalR();
```

## Auth (Phase 5)

SignalR hub requires JWT auth in Phase 5. Hub connection sends bearer token in the query string — standard SignalR pattern with JWT.

## Vue Composable (Phase 6)

`useJobHub()` — manages connection lifecycle for Admin page. Exposes reactive job status map keyed by job ID. Review page filters the stream for its specific job ID.

Install: `@microsoft/signalr` npm package in `sous-chef-web/`.
