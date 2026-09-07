using System.Threading.Channels;
using AzRadar.Shared.Configuration;
using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Models;
using AzRadar.Shared.Services;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace AzRadar.JobHost;

/// <summary>
/// Watches the crawl-jobs container via Cosmos DB Change Feed and dispatches new pending jobs
/// to the appropriate handler.
/// </summary>
/// <remarks>
/// The Change Feed Processor will not deliver the next batch until the change delegate returns,
/// so running a handler inline inside that delegate makes one slow job block every subsequent
/// job (head-of-line blocking). The delegate queues pending jobs; executors claim them
/// only when ready to start so jobs waiting for capacity are not mistaken for active workers.
/// </remarks>
public class ChangeFeedWorker : BackgroundService
{
    private readonly ILogger<ChangeFeedWorker> _logger;
    private readonly CosmosClient _cosmosClient;
    private readonly CosmosDbSettings _settings;
    private readonly ICosmosDbService _cosmosDb;
    private readonly IEnumerable<IJobHandler> _handlers;
    private readonly int _maxConcurrentJobs;
    private readonly JobHeartbeatRunner _heartbeat;
    private readonly Channel<CrawlJob> _queue =
        Channel.CreateUnbounded<CrawlJob>(new UnboundedChannelOptions { SingleReader = false });
    private ChangeFeedProcessor? _processor;

    public ChangeFeedWorker(
        ILogger<ChangeFeedWorker> logger,
        CosmosClient cosmosClient,
        IOptions<CosmosDbSettings> settings,
        ICosmosDbService cosmosDb,
        IEnumerable<IJobHandler> handlers,
        IConfiguration configuration,
        JobHeartbeatRunner heartbeat)
    {
        _logger = logger;
        _cosmosClient = cosmosClient;
        _settings = settings.Value;
        _cosmosDb = cosmosDb;
        _handlers = handlers;
        _maxConcurrentJobs = Math.Max(1, configuration.GetValue("JobHost:MaxConcurrentJobs", 2));
        _heartbeat = heartbeat;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Starting Change Feed processor with {Count} concurrent job executor(s)",
            _maxConcurrentJobs);

        var executors = Enumerable
            .Range(0, _maxConcurrentJobs)
            .Select(i => Task.Run(() => ExecutorLoopAsync(i, stoppingToken), stoppingToken))
            .ToArray();

        var db = _cosmosClient.GetDatabase(_settings.DatabaseName);
        var monitoredContainer = db.GetContainer(_settings.CrawlJobsContainer);
        var leaseContainer = db.GetContainer(_settings.LeasesContainer);

        _processor = monitoredContainer
            .GetChangeFeedProcessorBuilder<CrawlJob>(
                processorName: "az-radar-job-processor",
                onChangesDelegate: HandleChangesAsync)
            .WithInstanceName(Environment.MachineName)
            .WithLeaseContainer(leaseContainer)
            .WithPollInterval(TimeSpan.FromSeconds(2))
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

        _queue.Writer.TryComplete();
        try
        {
            await Task.WhenAll(executors).WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Job executors did not drain cleanly during shutdown");
        }
    }

    /// <summary>
    /// Drains claimed jobs off the queue and runs their handlers, isolated from the Change Feed
    /// delegate so that a long-running job cannot stall delivery of later jobs.
    /// </summary>
    private async Task ExecutorLoopAsync(int index, CancellationToken stoppingToken)
    {
        _logger.LogInformation("Job executor {Index} started", index);
        try
        {
            await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                await ClaimAndExecuteAsync(job, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job executor {Index} terminated unexpectedly", index);
        }
        _logger.LogInformation("Job executor {Index} stopped", index);
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

    private Task HandleChangesAsync(
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

            cancellationToken.ThrowIfCancellationRequested();
            if (!_queue.Writer.TryWrite(job))
                throw new InvalidOperationException($"Job host is shutting down; could not queue job {job.Id}.");
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Claims only when an executor is available, using a fresh ETag rather than a queued snapshot.
    /// </summary>
    private async Task ClaimAndExecuteAsync(CrawlJob queuedJob, CancellationToken cancellationToken)
    {
        var job = await _cosmosDb.GetCrawlJobAsync(queuedJob.Id, cancellationToken);
        if (job == null || job.Status != CrawlJobStatus.Pending)
        {
            _logger.LogDebug("Queued job {Id} is no longer pending", queuedJob.Id);
            return;
        }

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
        _logger.LogInformation("Attempting to claim job {Id} (ETag: {ETag})", job.Id, job.ETag ?? "null");
        bool claimed;
        try
        {
            claimed = await _cosmosDb.TryClaimJobAsync(job, cancellationToken);
        }
        catch (Exception ex)
        {
            // Swallow so one bad job cannot stall or endlessly redeliver the whole batch.
            _logger.LogError(ex, "Failed to claim job {Id}", job.Id);
            return;
        }

        if (!claimed)
        {
            _logger.LogInformation("Job {Id} already claimed by another worker", job.Id);
            return;
        }

        _logger.LogInformation(
            "Job {Id} ({Type}) claimed, status now {Status}; starting execution",
            job.Id, job.JobType, job.Status);

        await ExecuteJobAsync(job, cancellationToken);
    }

    private async Task ExecuteJobAsync(CrawlJob job, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing job {Id} of type {Type}", job.Id, job.JobType);

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

        try
        {
            await _heartbeat.RunAsync(job, ct => handler.HandleAsync(job, ct), cancellationToken);

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
            try
            {
                await _cosmosDb.UpdateCrawlJobAsync(job, CancellationToken.None);
            }
            catch (Exception updateEx)
            {
                _logger.LogError(updateEx, "Could not persist failure state for job {Id}", job.Id);
            }
        }
    }
}
