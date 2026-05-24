using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using SousChef.Api.Hubs;
using SousChef.Core.Common;
using SousChef.Core.DTOs;
using SousChef.Core.Interfaces;
using SousChef.Core.Models;
using SousChef.Infrastructure.Data;
using System.Text.Json;

namespace SousChef.Api.Endpoints;

public static class JobEndpoints
{
    private const string PlaceholderUserId = "anonymous";
    private const int MaxRetryAttempts = 3;

    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/jobs/{id:guid}", GetJobStatus)
            .WithName("GetJobStatus")
            .WithSummary("Get extraction job status")
            .Produces<JobStatusResponse>()
            .Produces(StatusCodes.Status404NotFound);

        app.MapPatch("/api/jobs/{id:guid}", SaveJobEdits)
            .WithName("SaveJobEdits")
            .WithSummary("Save edits to extracted recipe data")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        app.MapPatch("/api/jobs/{id:guid}/approve", ApproveJob)
            .WithName("ApproveJob")
            .WithSummary("Approve extracted recipe and add to library")
            .Produces<ApproveJobResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        app.MapDelete("/api/jobs/{id:guid}/reject", RejectJob)
            .WithName("RejectJob")
            .WithSummary("Reject extraction job and delete uploaded PDF")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        app.MapPatch("/api/jobs/{id:guid}/retry", RetryJob)
            .WithName("RetryJob")
            .WithSummary("Retry a failed extraction job")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> GetJobStatus(
        Guid id,
        SousChefDbContext db,
        CancellationToken ct)
    {
        var job = await db.ExtractionJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == id, ct);

        if (job is null)
            return Results.NotFound();

        return Results.Ok(new JobStatusResponse(
            job.Id,
            job.OriginalFilename,
            job.Status.ToString(),
            job.PdfType,
            job.ExtractedText,
            job.ExtractedData,            
            job.Error,
            job.Attempts,
            job.CreatedAt,
            job.ProcessedAt));
    }

    private static async Task<IResult> SaveJobEdits(
        Guid id,
        SaveJobEditsRequest request,
        SousChefDbContext db,
        CancellationToken ct)
    {
        var job = await db.ExtractionJobs.FindAsync([id], ct);

        if (job is null)
            return Results.NotFound();

        if (job.Status != ExtractionJobStatus.Review)
            return Results.Conflict($"Job is in '{job.Status}' status — edits only allowed in Review.");

        job.ExtractedData = request.ExtractedData;
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static async Task<IResult> ApproveJob(
        Guid id,
        SousChefDbContext db,
        IEmbeddingService embeddingService,
        IHubContext<JobStatusHub> hub,
        CancellationToken ct)
    {
        var job = await db.ExtractionJobs.FindAsync([id], ct);

        if (job is null)
            return Results.NotFound();

        if (job.Status != ExtractionJobStatus.Review)
            return Results.Conflict($"Job is in '{job.Status}' status — only Review jobs can be approved.");

        if (string.IsNullOrWhiteSpace(job.ExtractedData))
            return Results.UnprocessableEntity("No extracted data found on job.");

        RecipeDto? recipeDto;
        try
        {
            recipeDto = JsonSerializer.Deserialize<RecipeDto>(job.ExtractedData,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            return Results.UnprocessableEntity($"Failed to parse extracted data: {ex.Message}");
        }

        if (recipeDto is null)
            return Results.UnprocessableEntity("Extracted data could not be deserialized.");

        var embeddingInput = EmbeddingInputBuilder.Build(recipeDto);
        var embeddingResult = await embeddingService.GenerateAsync(embeddingInput, ct);

        if (!embeddingResult.IsSuccess)
            return Results.Problem($"Embedding generation failed: {embeddingResult.Error!.Message}");

        var recipeId = Guid.NewGuid();

        var recipe = new Recipe
        {
            Id = recipeId,
            UserId = PlaceholderUserId,
            Title = recipeDto.Title,
            Description = recipeDto.Description,
            CuisineType = recipeDto.CuisineType,
            Course = recipeDto.Course,
            PrepTimeMinutes = recipeDto.PrepTimeMinutes,
            CookTimeMinutes = recipeDto.CookTimeMinutes,
            TotalTimeMinutes = (recipeDto.PrepTimeMinutes ?? 0) + (recipeDto.CookTimeMinutes ?? 0),
            Servings = recipeDto.Servings,
            Difficulty = recipeDto.Difficulty,
            Tags = recipeDto.Tags,
            SourceFileKey = job.FileKey,
            Embedding = new Vector(embeddingResult.Value!),
            CreatedAt = DateTimeOffset.UtcNow
        };

        recipe.Ingredients = recipeDto.Ingredients.Select(i => new Ingredient
        {
            Id = Guid.NewGuid(),
            RecipeId = recipeId,
            Name = i.Name,
            Quantity = i.Quantity,
            Unit = i.Unit,
            Notes = i.Notes
        }).ToList();

        recipe.Steps = recipeDto.Steps.Select(s => new RecipeStep
        {
            Id = Guid.NewGuid(),
            RecipeId = recipeId,
            StepNumber = s.StepNumber,
            Instruction = s.Instruction
        }).ToList();

        job.Status = ExtractionJobStatus.Completed;
        job.ProcessedAt = DateTimeOffset.UtcNow;

        db.Recipes.Add(recipe);
        await db.SaveChangesAsync(ct);

        await hub.Clients.All.SendAsync("JobStatusChanged",
            new JobStatusChanged(job.Id, "Completed", null, "Recipe approved and added to library"), ct);

        return Results.Ok(new ApproveJobResponse(recipeId, recipe.Title));
    }

    private static async Task<IResult> RejectJob(
        Guid id,
        SousChefDbContext db,
        IStorageService storage,
        IHubContext<JobStatusHub> hub,
        CancellationToken ct)
    {
        var job = await db.ExtractionJobs.FindAsync([id], ct);

        if (job is null)
            return Results.NotFound();

        var rejectableStatuses = new[]
        {
            ExtractionJobStatus.Review,
            ExtractionJobStatus.InvalidContent,
            ExtractionJobStatus.Failed
        };

        if (!rejectableStatuses.Contains(job.Status))
            return Results.Conflict($"Job is in '{job.Status}' status — cannot reject.");

        await storage.DeleteAsync(job.FileKey, ct);

        job.Status = ExtractionJobStatus.Rejected;
        job.ProcessedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await hub.Clients.All.SendAsync("JobStatusChanged",
            new JobStatusChanged(job.Id, "Rejected", null, "Job rejected and file deleted"), ct);

        return Results.NoContent();
    }

    private static async Task<IResult> RetryJob(
        Guid id,
        SousChefDbContext db,
        IHubContext<JobStatusHub> hub,
        CancellationToken ct)
    {
        var job = await db.ExtractionJobs.FindAsync([id], ct);

        if (job is null)
            return Results.NotFound();

        if (job.Status != ExtractionJobStatus.Failed)
            return Results.Conflict($"Job is in '{job.Status}' status — only Failed jobs can be retried.");

        if (job.Attempts >= MaxRetryAttempts)
            return Results.Conflict(
                $"Job has reached the maximum of {MaxRetryAttempts} attempts. Re-upload the PDF to try again.");

        job.Status = ExtractionJobStatus.Pending;
        job.Error = null;
        job.ExtractedData = null;
        job.ProcessedAt = null;

        await db.SaveChangesAsync(ct);

        await hub.Clients.All.SendAsync("JobStatusChanged",
            new JobStatusChanged(job.Id, "Pending", null,
                string.IsNullOrWhiteSpace(job.ExtractedText)
                    ? "Retrying from scratch"
                    : "Retrying LLM extraction (text already extracted)"), ct);

        return Results.NoContent();
    }
}

public record JobStatusResponse(
    Guid Id,
    string OriginalFilename,
    string Status,
    string? PdfType,
    string? ExtractedText,   // raw OCR/parsed text — shown on InvalidContent state
    string? ExtractedData,   // structured RecipeDto JSON — used to pre-populate review form    
    string? Error,
    int Attempts,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ProcessedAt);

public record SaveJobEditsRequest(string ExtractedData);

public record ApproveJobResponse(Guid RecipeId, string Title);
