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
}
