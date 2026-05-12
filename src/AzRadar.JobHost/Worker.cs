using AzRadar.Shared.Configuration;
using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace AzRadar.JobHost;

/// <summary>
/// Watches the crawl-jobs container via Cosmos DB Change Feed
/// and dispatches new pending jobs to the appropriate handler.
/// </summary>
public class ChangeFeedWorker : BackgroundService
{
    private readonly ILogger<ChangeFeedWorker> _logger;
    private readonly CosmosClient _cosmosClient;
    private readonly CosmosDbSettings _settings;
    private readonly ICosmosDbService _cosmosDb;
    private readonly IEnumerable<IJobHandler> _handlers;
    private ChangeFeedProcessor? _processor;

    public ChangeFeedWorker(
        ILogger<ChangeFeedWorker> logger,
        CosmosClient cosmosClient,
        IOptions<CosmosDbSettings> settings,
        ICosmosDbService cosmosDb,
        IEnumerable<IJobHandler> handlers)
    {
        _logger = logger;
        _cosmosClient = cosmosClient;
        _settings = settings.Value;
        _cosmosDb = cosmosDb;
        _handlers = handlers;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting Change Feed processor");

        var db = _cosmosClient.GetDatabase(_settings.DatabaseName);
        var monitoredContainer = db.GetContainer(_settings.CrawlJobsContainer);
        var leaseContainer = db.GetContainer(_settings.LeasesContainer);

        _processor = monitoredContainer
            .GetChangeFeedProcessorBuilder<CrawlJob>(
                processorName: "az-radar-job-processor",
                onChangesDelegate: HandleChangesAsync)
            .WithInstanceName(Environment.MachineName)
            .WithLeaseContainer(leaseContainer)
            .WithStartTime(DateTime.UtcNow.AddMinutes(-5))
            .Build();

        await _processor.StartAsync();
        _logger.LogInformation("Change Feed processor started");

        // Keep alive until cancellation
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Shutting down Change Feed processor");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor != null)
        {
            await _processor.StopAsync();
            _logger.LogInformation("Change Feed processor stopped");
        }
        await base.StopAsync(cancellationToken);
    }

    private async Task HandleChangesAsync(
        ChangeFeedProcessorContext context,
        IReadOnlyCollection<CrawlJob> changes,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Change Feed: received {Count} changes from lease {LeaseToken}",
            changes.Count, context.LeaseToken);

        foreach (var job in changes)
        {
            // Only process pending jobs (ignore status updates)
            if (job.Status != CrawlJobStatus.Pending)
            {
                _logger.LogDebug("Skipping job {Id} with status {Status}", job.Id, job.Status);
                continue;
            }

            await ProcessJobAsync(job, cancellationToken);
        }
    }

    private async Task ProcessJobAsync(CrawlJob job, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing job {Id} of type {Type}", job.Id, job.JobType);

        // Find the handler for this job type
        var handler = _handlers.FirstOrDefault(h => h.JobType == job.JobType);
        if (handler == null)
        {
            _logger.LogWarning("No handler registered for job type: {Type}", job.JobType);
            job.Status = CrawlJobStatus.Failed;
            job.Error = $"No handler for job type: {job.JobType}";
            job.CompletedAt = DateTimeOffset.UtcNow;
            await _cosmosDb.UpdateCrawlJobAsync(job, cancellationToken);
            return;
        }

        // Try to claim the job (ETag-based optimistic concurrency)
        var claimed = await _cosmosDb.TryClaimJobAsync(job, cancellationToken);
        if (!claimed)
        {
            _logger.LogInformation("Job {Id} already claimed by another worker", job.Id);
            return;
        }

        try
        {
            await handler.HandleAsync(job, cancellationToken);

            job.Status = CrawlJobStatus.Completed;
            job.CompletedAt = DateTimeOffset.UtcNow;
            var updated = await _cosmosDb.UpdateCrawlJobAsync(job, cancellationToken);
            job.ETag = updated.ETag;

            _logger.LogInformation(
                "Job {Id} completed: {New} new items, {Skipped} skipped",
                job.Id, job.Result?.NewItems ?? 0, job.Result?.SkippedItems ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {Id} failed", job.Id);

            job.Status = CrawlJobStatus.Failed;
            job.Error = ex.Message;
            job.CompletedAt = DateTimeOffset.UtcNow;
            await _cosmosDb.UpdateCrawlJobAsync(job, cancellationToken);
        }
    }
}
