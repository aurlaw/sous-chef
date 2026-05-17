using Microsoft.EntityFrameworkCore;
using SousChef.Infrastructure.Data;

namespace SousChef.Api.Endpoints;

public static class JobEndpoints
{
    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/jobs/{id:guid}", GetJobStatus)
            .WithName("GetJobStatus")
            .WithSummary("Get extraction job status")
            .Produces<JobStatusResponse>()
            .Produces(StatusCodes.Status404NotFound);

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
            job.Error,
            job.Attempts,
            job.CreatedAt,
            job.ProcessedAt));
    }
}

public record JobStatusResponse(
    Guid Id,
    string OriginalFilename,
    string Status,
    string? PdfType,
    string? Error,
    int Attempts,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ProcessedAt);
