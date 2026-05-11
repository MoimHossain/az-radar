using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Models;
using Microsoft.Extensions.Logging;

namespace AzRadar.Shared.Services;

public class AzureUpdatesJobHandler : IJobHandler
{
    private readonly IFeedReader _feedReader;
    private readonly ILlmAnalyzer _llmAnalyzer;
    private readonly ICosmosDbService _cosmosDb;
    private readonly ILogger<AzureUpdatesJobHandler> _logger;

    private static readonly TimeSpan DefaultLookback = TimeSpan.FromDays(7);

    public string JobType => CrawlJobTypes.AzureUpdates;

    public AzureUpdatesJobHandler(
        IFeedReader feedReader,
        ILlmAnalyzer llmAnalyzer,
        ICosmosDbService cosmosDb,
        ILogger<AzureUpdatesJobHandler> logger)
    {
        _feedReader = feedReader;
        _llmAnalyzer = llmAnalyzer;
        _cosmosDb = cosmosDb;
        _logger = logger;
    }

    public async Task HandleAsync(CrawlJob job, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting Azure Updates crawl job {JobId}", job.Id);

        // Determine how far back to look
        var since = await DetermineSinceDateAsync(cancellationToken);
        _logger.LogInformation("Reading feed items since {Since}", since.ToString("o"));

        // Read the RSS feed
        var feedItems = await _feedReader.ReadFeedAsync(since, cancellationToken);
        _logger.LogInformation("Found {Count} feed items from RSS", feedItems.Count);

        int newItems = 0;
        int skipped = 0;

        foreach (var item in feedItems)
        {
            cancellationToken.ThrowIfCancellationRequested();

            item.CrawlJobId = job.Id;

            // Check if already exists (dedup)
            var existing = await _cosmosDb.GetFeedItemAsync(item.Id, cancellationToken);
            if (existing != null)
            {
                skipped++;
                _logger.LogDebug("Skipping already-seen item: {Title}", item.Title);
                continue;
            }

            // Analyze with LLM before storing
            _logger.LogInformation("Analyzing new item: {Title}", item.Title);
            var analysis = await _llmAnalyzer.AnalyzeFeedItemAsync(item, cancellationToken);
            item.LlmAnalysis = analysis;

            // Store the complete item (with analysis)
            await _cosmosDb.TryStoreFeedItemAsync(item, cancellationToken);
            newItems++;
        }

        // Update job result
        job.Result = new CrawlJobResult
        {
            NewItems = newItems,
            TotalChecked = feedItems.Count,
            SkippedItems = skipped
        };

        _logger.LogInformation(
            "Azure Updates crawl complete: {New} new, {Skipped} skipped, {Total} total",
            newItems, skipped, feedItems.Count);
    }

    private async Task<DateTimeOffset> DetermineSinceDateAsync(CancellationToken ct)
    {
        var latestDate = await _cosmosDb.GetLatestFeedItemDateAsync(
            CrawlJobTypes.AzureUpdates, ct);

        if (latestDate.HasValue)
        {
            _logger.LogInformation("Found existing items, using latest date: {Date}", latestDate.Value);
            return latestDate.Value;
        }

        // First run — only look back 7 days
        var since = DateTimeOffset.UtcNow.Subtract(DefaultLookback);
        _logger.LogInformation("First run, defaulting to last {Days} days", DefaultLookback.TotalDays);
        return since;
    }
}
