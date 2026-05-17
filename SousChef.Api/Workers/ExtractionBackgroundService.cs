using Microsoft.EntityFrameworkCore;
using SousChef.Infrastructure.Data;

namespace SousChef.Api.Workers;

public class ExtractionBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExtractionBackgroundService> _logger;
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(10);

    public ExtractionBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<ExtractionBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
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

        var job = await db.ExtractionJobs
            .Where(j => j.Status == ExtractionJobStatus.Pending)
            .OrderBy(j => j.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (job is null) return;

        job.Status = ExtractionJobStatus.Processing;
        job.Attempts++;
        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Job {JobId} claimed for processing (attempt {Attempt}). File: {Filename}",
            job.Id, job.Attempts, job.OriginalFilename);

        // Phase 3 will add extraction logic here
    }
}
