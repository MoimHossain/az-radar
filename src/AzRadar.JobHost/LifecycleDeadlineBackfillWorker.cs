using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Models;

namespace AzRadar.JobHost;

/// <summary>
/// Stamps the normalized lifecycle deadline on documents analyzed before the resolver existed
/// (or by an older resolver version) so calendar range queries see the full catalog.
/// </summary>
public sealed class LifecycleDeadlineBackfillWorker(
    ICosmosDbService cosmosDb,
    ILogger<LifecycleDeadlineBackfillWorker> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CrawlPollInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxCrawlWait = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);

            var waitedSince = DateTimeOffset.UtcNow;
            while (await IsCrawlRunningAsync(stoppingToken) && DateTimeOffset.UtcNow - waitedSince < MaxCrawlWait)
            {
                logger.LogInformation("Lifecycle deadline backfill deferred while a crawl job is processing");
                await Task.Delay(CrawlPollInterval, stoppingToken);
            }

            var result = await cosmosDb.BackfillLifecycleDeadlinesAsync(stoppingToken);
            logger.LogInformation(
                "Lifecycle deadline backfill complete: feed items {FeedStamped}/{FeedScanned} stamped " +
                "({FeedDated} dated), doc insights {DocsStamped}/{DocsScanned} stamped ({DocsDated} dated)",
                result.FeedItemsStamped, result.FeedItemsScanned, result.FeedItemsWithDeadline,
                result.DocInsightsStamped, result.DocInsightsScanned, result.DocInsightsWithDeadline);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // Unstamped documents are still resolved in memory by the calendar queries.
            logger.LogError(ex, "Lifecycle deadline backfill failed; it will be retried on next start");
        }
    }

    private async Task<bool> IsCrawlRunningAsync(CancellationToken cancellationToken)
    {
        var jobs = await cosmosDb.GetCrawlJobsAsync(20, cancellationToken);
        return jobs.Any(job => job.Status == CrawlJobStatus.Processing);
    }
}
