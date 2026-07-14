using AzRadar.Shared.Models;

namespace AzRadar.Shared.Interfaces;

public interface ICosmosDbService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    // CrawlJob operations
    Task<CrawlJob> CreateCrawlJobAsync(CrawlJob job, CancellationToken cancellationToken = default);
    Task<CrawlJob?> GetCrawlJobAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CrawlJob>> GetCrawlJobsAsync(int limit = 50, CancellationToken cancellationToken = default);
    Task<CrawlJob> UpdateCrawlJobAsync(CrawlJob job, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to claim a job by atomically setting status to "processing" using ETag.
    /// Returns true if claim succeeded, false if another processor already claimed it.
    /// </summary>
    Task<bool> TryClaimJobAsync(CrawlJob job, CancellationToken cancellationToken = default);

    Task<bool> DeleteCrawlJobAsync(string id, CancellationToken cancellationToken = default);

    // FeedItem operations
    Task<FeedItem?> GetFeedItemAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FeedItem>> GetFeedItemsAsync(
        string? source = null,
        int limit = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a feed item only if it doesn't already exist (dedup by id).
    /// Returns true if the item was new and stored, false if it already existed.
    /// </summary>
    Task<bool> TryStoreFeedItemAsync(FeedItem item, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the most recent feed item date for a given source, used to determine
    /// how far back to look on subsequent crawls.
    /// </summary>
    Task<DateTimeOffset?> GetLatestFeedItemDateAsync(
        string source,
        CancellationToken cancellationToken = default);

    // Watchlist operations
    Task<WatchlistItem> CreateWatchlistItemAsync(WatchlistItem item, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WatchlistItem>> GetWatchlistAsync(CancellationToken cancellationToken = default);
    Task<bool> DeleteWatchlistItemAsync(string id, CancellationToken cancellationToken = default);

    // Repository watchlist operations (GitHub Change Radar)
    Task<RepoWatchItem> CreateRepoWatchAsync(RepoWatchItem item, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RepoWatchItem>> GetRepoWatchlistAsync(CancellationToken cancellationToken = default);
    Task<RepoWatchItem?> GetRepoWatchAsync(string id, CancellationToken cancellationToken = default);
    Task<RepoWatchItem> UpdateRepoWatchAsync(RepoWatchItem item, CancellationToken cancellationToken = default);
    Task<bool> DeleteRepoWatchAsync(string id, CancellationToken cancellationToken = default);

    // DocInsight operations
    Task<DocInsight?> GetDocInsightAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DocInsight>> GetDocInsightsAsync(
        string? serviceName = null,
        int limit = 50,
        string? source = null,
        CancellationToken cancellationToken = default);
    Task<bool> UpsertDocInsightAsync(DocInsight insight, CancellationToken cancellationToken = default);

    // AppConfig operations
    Task<AppConfig?> GetAppConfigAsync(string key, CancellationToken cancellationToken = default);
    Task UpsertAppConfigAsync(AppConfig config, CancellationToken cancellationToken = default);

    // BlastRadius operations
    Task UpsertBlastRadiusSummaryAsync(BlastRadiusSummary summary, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BlastRadiusSummary>> GetBlastRadiusSummariesAsync(
        int limit = 100,
        CancellationToken cancellationToken = default);
    Task<BlastRadiusSummary?> GetBlastRadiusSummaryAsync(string id, CancellationToken cancellationToken = default);

    // Diagnostics operations
    Task StoreDiagnosticAsync(JobDiagnosticEntry entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<JobDiagnosticEntry>> GetDiagnosticsForJobAsync(
        string jobId, CancellationToken cancellationToken = default);
}
