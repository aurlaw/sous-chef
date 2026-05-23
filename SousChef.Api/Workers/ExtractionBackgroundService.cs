using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SousChef.Api.Hubs;
using SousChef.Core.Interfaces;
using SousChef.Core.Models;
using SousChef.Infrastructure.Data;

namespace SousChef.Api.Workers;

public class ExtractionBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExtractionBackgroundService> _logger;
    private readonly IHubContext<JobStatusHub> _hub;
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions CamelCaseOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public ExtractionBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<ExtractionBackgroundService> logger,
        IHubContext<JobStatusHub> hub)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _hub = hub;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ExtractionBackgroundService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessPendingJobsAsync(stoppingToken);
            await Task.Delay(PollingInterval, stoppingToken);
        }

        _logger.LogInformation("ExtractionBackgroundService stopped.");
    }

    private async Task ProcessPendingJobsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SousChefDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IStorageService>();
        var extractor = scope.ServiceProvider.GetRequiredService<IDocumentExtractor>();
        var llm = scope.ServiceProvider.GetRequiredService<IExtractionService>();

        var job = await db.ExtractionJobs
            .Where(j => j.Status == ExtractionJobStatus.Pending)
            .OrderBy(j => j.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (job is null) return;

        job.Status = ExtractionJobStatus.Processing;
        job.Attempts++;
        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Processing job {JobId} (attempt {Attempt}): {Filename}",
            job.Id, job.Attempts, job.OriginalFilename);

        // Stage 1: Download from R2
        await _hub.Clients.All.SendAsync("JobStatusChanged",
            new JobStatusChanged(job.Id, "Processing", "Download", "Downloading PDF..."), ct);

        var downloadResult = await storage.DownloadAsync(job.FileKey, ct);
        if (!downloadResult.IsSuccess)
        {
            await FailJobAsync(db, job, PipelineStage.Download,
                downloadResult.Error!.Message, ct);
            return;
        }

        // Stage 2: PDF text extraction + keyword pre-filter
        await _hub.Clients.All.SendAsync("JobStatusChanged",
            new JobStatusChanged(job.Id, "Processing", "DocumentExtraction", "Extracting text..."), ct);

        var extractResult = await extractor.ExtractTextAsync(downloadResult.Value!, ct);
        if (!extractResult.IsSuccess)
        {
            var stage = extractResult.Error!.Code == "VALIDATION"
                ? PipelineStage.RecipeValidation
                : PipelineStage.DocumentExtraction;

            await FailJobAsync(db, job, stage, extractResult.Error.Message, ct);
            return;
        }

        job.PdfType = extractResult.Value!.Type.ToString().ToLower();
        job.ExtractedText = extractResult.Value.Text;

        _logger.LogInformation(
            "Job {JobId}: {Type} PDF, {Pages} pages, {Chars} chars extracted",
            job.Id, extractResult.Value.Type,
            extractResult.Value.PageCount,
            extractResult.Value.Text.Length);

        // Stage 3: LLM extraction
        await _hub.Clients.All.SendAsync("JobStatusChanged",
            new JobStatusChanged(job.Id, "Processing", "LlmExtraction", "Analyzing recipe..."), ct);

        var llmResult = await llm.ExtractRecipesAsync(job.ExtractedText, ct);
        if (!llmResult.IsSuccess)
        {
            await db.SaveChangesAsync(ct); // persist extracted_text before failing
            await FailJobAsync(db, job, PipelineStage.LlmExtraction,
                llmResult.Error!.Message, ct);
            return;
        }

        // Claude determined not a recipe
        if (llmResult.Value!.IsNotARecipe)
        {
            var pipelineError = new PipelineError(
                PipelineStage.NotARecipe,
                "Claude determined this document is not a recipe.",
                llmResult.Value.NotARecipeReason);

            job.Status = ExtractionJobStatus.InvalidContent;
            job.Error = pipelineError.ToJson();
            job.ProcessedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            await _hub.Clients.All.SendAsync("JobInvalidContent",
                new JobInvalidContent(job.Id, llmResult.Value.NotARecipeReason!, job.ExtractedText!), ct);

            _logger.LogWarning(
                "Job {JobId} marked InvalidContent: {Reason}",
                job.Id, llmResult.Value.NotARecipeReason);
            return;
        }

        // Serialize recipe JSON
        string extractedJson;
        try
        {
            extractedJson = JsonSerializer.Serialize(llmResult.Value.Recipe, CamelCaseOptions);
        }
        catch (Exception ex)
        {
            await FailJobAsync(db, job, PipelineStage.JsonParsing,
                $"Failed to serialize recipe: {ex.Message}", ct);
            return;
        }

        // Success — transition to Review
        job.Status = ExtractionJobStatus.Review;
        job.ExtractedData = extractedJson;
        job.ProcessedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await _hub.Clients.All.SendAsync("JobReadyForReview",
            new JobReadyForReview(job.Id, extractedJson), ct);

        _logger.LogInformation(
            "Job {JobId} ready for review: {Title}",
            job.Id, llmResult.Value.Recipe!.Title);
    }

    private async Task FailJobAsync(
        SousChefDbContext db,
        ExtractionJob job,
        PipelineStage stage,
        string message,
        CancellationToken ct,
        string? detail = null)
    {
        var error = new PipelineError(stage, message, detail);
        job.Status = ExtractionJobStatus.Failed;
        job.Error = error.ToJson();
        job.ProcessedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await _hub.Clients.All.SendAsync("JobFailed",
            new JobFailed(job.Id, stage.ToString(), message, detail), ct);

        _logger.LogError(
            "Job {JobId} failed at stage {Stage}: {Message}",
            job.Id, stage, message);
    }
}
