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

        var job = await db.ExtractionJobs
            .Where(j => j.Status == ExtractionJobStatus.Pending)
            .OrderBy(j => j.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (job is null) return;

        job.Status = ExtractionJobStatus.Processing;
        job.Attempts++;
        await db.SaveChangesAsync(ct);

        await _hub.Clients.All.SendAsync("JobStatusChanged",
            new JobStatusChanged(job.Id, "Processing", "Download", "Downloading PDF..."), ct);

        _logger.LogInformation(
            "Processing job {JobId} (attempt {Attempt}): {Filename}",
            job.Id, job.Attempts, job.OriginalFilename);

        // Stage 1: Download
        var downloadResult = await storage.DownloadAsync(job.FileKey, ct);
        if (!downloadResult.IsSuccess)
        {
            await FailJobAsync(db, job, PipelineStage.Download,
                downloadResult.Error!.Message, ct);
            return;
        }

        await _hub.Clients.All.SendAsync("JobStatusChanged",
            new JobStatusChanged(job.Id, "Processing", "DocumentExtraction", "Extracting text..."), ct);

        // Stage 2: PDF text extraction + keyword pre-filter
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

        job.Status = ExtractionJobStatus.Review;
        job.ProcessedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await _hub.Clients.All.SendAsync("JobReadyForReview",
            new JobReadyForReview(job.Id, string.Empty), ct);

        _logger.LogInformation(
            "Job {JobId} ready for review — extracted text stored ({Chars} chars)",
            job.Id, job.ExtractedText.Length);
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
