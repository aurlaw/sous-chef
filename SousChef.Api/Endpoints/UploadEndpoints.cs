using SousChef.Core.Interfaces;
using SousChef.Infrastructure.Data;

namespace SousChef.Api.Endpoints;

public static class UploadEndpoints
{
    private const string PlaceholderUserId = "anonymous";

    public static IEndpointRouteBuilder MapUploadEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/uploads", HandleUpload)
            .DisableAntiforgery()
            .WithName("UploadRecipePdf")
            .WithSummary("Upload a recipe PDF for extraction")
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<UploadResponse>(StatusCodes.Status201Created)
            .Produces<string>(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async Task<IResult> HandleUpload(
        IFormFile file,
        IStorageService storage,
        SousChefDbContext db,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return Results.BadRequest("No file provided.");

        if (!file.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase)
            && !file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest("Only PDF files are accepted.");

        var jobId = Guid.NewGuid();
        var key = $"uploads/{PlaceholderUserId}/{jobId}/{file.FileName}";

        await using var stream = file.OpenReadStream();
        var uploadResult = await storage.UploadAsync(key, stream, "application/pdf", ct);

        if (!uploadResult.IsSuccess)
            return Results.Problem(uploadResult.Error!.Message);

        var job = new ExtractionJob
        {
            Id = jobId,
            UserId = PlaceholderUserId,
            FileKey = key,
            OriginalFilename = file.FileName,
            Status = ExtractionJobStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.ExtractionJobs.Add(job);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/jobs/{jobId}", new UploadResponse(jobId, key));
    }
}

public record UploadResponse(Guid JobId, string FileKey);
