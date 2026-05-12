using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Models;
using Microsoft.Extensions.Logging;

namespace AzRadar.Shared.Services;

public class MsLearnIntelligenceJobHandler : IJobHandler
{
    private readonly IMcpDocsClient _mcpClient;
    private readonly ILlmAnalyzer _llmAnalyzer;
    private readonly ICosmosDbService _cosmosDb;
    private readonly ILogger<MsLearnIntelligenceJobHandler> _logger;

    private const int MaxFetchesPerService = 8;

    public string JobType => CrawlJobTypes.MsLearnIntelligence;

    public MsLearnIntelligenceJobHandler(
        IMcpDocsClient mcpClient,
        ILlmAnalyzer llmAnalyzer,
        ICosmosDbService cosmosDb,
        ILogger<MsLearnIntelligenceJobHandler> logger)
    {
        _mcpClient = mcpClient;
        _llmAnalyzer = llmAnalyzer;
        _cosmosDb = cosmosDb;
        _logger = logger;
    }

    public async Task HandleAsync(CrawlJob job, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting MS Learn Intelligence job {JobId}", job.Id);

        var watchlist = await _cosmosDb.GetWatchlistAsync(cancellationToken);
        if (watchlist.Count == 0)
        {
            _logger.LogWarning("No services in watchlist, nothing to do");
            job.Result = new CrawlJobResult { NewItems = 0, TotalChecked = 0, SkippedItems = 0 };
            return;
        }

        _logger.LogInformation("Processing {Count} watched services", watchlist.Count);

        int newItems = 0;
        int skipped = 0;
        int totalChecked = 0;

        foreach (var service in watchlist)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation("Searching MS Learn for: {Service}", service.ServiceName);

            // Targeted search: lifecycle changes
            var targetedQuery = $"{service.ServiceName} retirement OR deprecation OR breaking change OR migration OR end of support";
            var targetedResults = await _mcpClient.SearchDocsAsync(targetedQuery, cancellationToken);
            _logger.LogInformation("Targeted search returned {Count} results (URLs: {Urls})",
                targetedResults.Count,
                targetedResults.Count(r => !string.IsNullOrEmpty(r.Url)));

            // Broad search: general updates
            var broadQuery = $"{service.ServiceName} updates changes announcements";
            var broadResults = await _mcpClient.SearchDocsAsync(broadQuery, cancellationToken);
            _logger.LogInformation("Broad search returned {Count} results", broadResults.Count);

            // Merge and dedup by title (MCP results may not have URLs)
            var allResults = targetedResults
                .Concat(broadResults)
                .DistinctBy(r => !string.IsNullOrEmpty(r.Url) ? NormalizeUrl(r.Url) : r.Title.ToLowerInvariant())
                .Where(r => !string.IsNullOrEmpty(r.Title) || !string.IsNullOrEmpty(r.FullContent))
                .Take(MaxFetchesPerService)
                .ToList();

            _logger.LogInformation(
                "Found {Count} unique docs for {Service}",
                allResults.Count, service.ServiceName);

            foreach (var searchResult in allResults)
            {
                cancellationToken.ThrowIfCancellationRequested();
                totalChecked++;

                // Use title as part of the dedup key since MCP may not return URLs
                var docKey = !string.IsNullOrEmpty(searchResult.Url)
                    ? searchResult.Url
                    : $"{service.ServiceName}:{searchResult.Title}";
                var docId = DocInsight.GenerateId(docKey);

                // Use inline content from search (MCP returns full content)
                var docContent = !string.IsNullOrEmpty(searchResult.FullContent)
                    ? searchResult.FullContent
                    : searchResult.Snippet;

                if (string.IsNullOrEmpty(docContent))
                {
                    _logger.LogDebug("Empty content for {Title}, skipping", searchResult.Title);
                    skipped++;
                    continue;
                }

                var contentHash = DocInsight.HashContent(docContent);

                // Check if we've already analyzed this exact version
                var existing = await _cosmosDb.GetDocInsightAsync(docId, cancellationToken);
                if (existing != null && existing.ContentHash == contentHash)
                {
                    _logger.LogDebug("Doc unchanged: {Title}", searchResult.Title);
                    skipped++;
                    continue;
                }

                // Analyze with LLM — create a synthetic FeedItem for the analyzer
                var feedItem = new FeedItem
                {
                    Id = docId,
                    Source = "ms-learn",
                    Title = searchResult.Title,
                    Link = !string.IsNullOrEmpty(searchResult.Url)
                        ? searchResult.Url
                        : $"https://learn.microsoft.com/search/?terms={Uri.EscapeDataString(service.ServiceName)}",
                    PublishDate = DateTimeOffset.UtcNow,
                    Summary = searchResult.Snippet,
                    RawContent = TruncateContent(docContent, 8000),
                };

                _logger.LogInformation("Analyzing doc: {Title}", searchResult.Title);
                var analysis = await _llmAnalyzer.AnalyzeFeedItemAsync(feedItem, cancellationToken);

                var insight = new DocInsight
                {
                    Id = docId,
                    Source = "ms-learn",
                    ServiceName = service.ServiceName,
                    DocUrl = feedItem.Link,
                    Title = searchResult.Title,
                    Snippet = searchResult.Snippet,
                    ContentHash = contentHash,
                    LlmAnalysis = analysis,
                    CrawlJobId = job.Id,
                    LastAnalyzedAt = DateTimeOffset.UtcNow,
                    FirstSeenAt = existing?.FirstSeenAt ?? DateTimeOffset.UtcNow,
                };

                await _cosmosDb.UpsertDocInsightAsync(insight, cancellationToken);
                newItems++;

                // Update job progress incrementally
                job.Result = new CrawlJobResult
                {
                    NewItems = newItems,
                    TotalChecked = totalChecked,
                    SkippedItems = skipped
                };
                var updatedJob = await _cosmosDb.UpdateCrawlJobAsync(job, cancellationToken);
                job.ETag = updatedJob.ETag;

                _logger.LogInformation(
                    "Stored insight: {Title} (type={ChangeType}, severity={Severity})",
                    insight.Title, analysis.ChangeType, analysis.Severity);
            }
        }

        job.Result = new CrawlJobResult
        {
            NewItems = newItems,
            TotalChecked = totalChecked,
            SkippedItems = skipped
        };

        _logger.LogInformation(
            "MS Learn Intelligence complete: {New} new/updated, {Skipped} unchanged, {Total} total",
            newItems, skipped, totalChecked);
    }

    private static string NormalizeUrl(string url)
    {
        return url.Trim().ToLowerInvariant()
            .TrimEnd('/')
            .Replace("?view=", "?v="); // normalize common query params
    }

    private static string TruncateContent(string content, int maxChars)
    {
        return content.Length <= maxChars ? content : content[..maxChars];
    }
}
