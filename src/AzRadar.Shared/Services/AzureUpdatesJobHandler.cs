using System.Security.Cryptography;
using System.Text;
using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Models;
using Microsoft.Extensions.Logging;

namespace AzRadar.Shared.Services;

public class AzureUpdatesJobHandler : IJobHandler
{
    private readonly IMrcMcpClient _mrcClient;
    private readonly ILlmAnalyzer _llmAnalyzer;
    private readonly ICosmosDbService _cosmosDb;
    private readonly ILogger<AzureUpdatesJobHandler> _logger;

    public string JobType => CrawlJobTypes.AzureUpdates;

    public AzureUpdatesJobHandler(
        IMrcMcpClient mrcClient,
        ILlmAnalyzer llmAnalyzer,
        ICosmosDbService cosmosDb,
        ILogger<AzureUpdatesJobHandler> logger)
    {
        _mrcClient = mrcClient;
        _llmAnalyzer = llmAnalyzer;
        _cosmosDb = cosmosDb;
        _logger = logger;
    }

    public async Task HandleAsync(CrawlJob job, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting Azure Updates crawl job {JobId} via MRC MCP", job.Id);

        // Fetch latest updates from MRC MCP (up to 50)
        var updates = await _mrcClient.GetRecentAzureUpdatesAsync(
            top: 50, cancellationToken: cancellationToken);

        _logger.LogInformation("MRC returned {Count} Azure updates", updates.Count);

        int newItems = 0;
        int skipped = 0;

        foreach (var update in updates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var id = GenerateDedupId(update.Id);

            // Check if already exists (dedup)
            var existing = await _cosmosDb.GetFeedItemAsync(id, cancellationToken);
            if (existing != null)
            {
                skipped++;
                continue;
            }

            var feedItem = new FeedItem
            {
                Id = id,
                Source = CrawlJobTypes.AzureUpdates,
                Title = update.Title,
                Link = $"https://azure.microsoft.com/updates?id={update.Id}",
                PublishDate = DateTimeOffset.TryParse(update.Modified, out var modified)
                    ? modified : DateTimeOffset.UtcNow,
                Summary = update.Description,
                Categories = [.. update.Tags, .. update.ProductCategories],
                RawContent = update.Description,
                CrawlJobId = job.Id,
            };

            // Analyze with LLM
            _logger.LogInformation("Analyzing: {Title}", update.Title);
            var analysis = await _llmAnalyzer.AnalyzeFeedItemAsync(feedItem, cancellationToken);

            // Enrich with structured data from MRC (products, tags are already parsed)
            if (analysis.AffectedServices.Count == 0 && update.Products.Count > 0)
                analysis.AffectedServices = update.Products;

            feedItem.LlmAnalysis = analysis;

            await _cosmosDb.TryStoreFeedItemAsync(feedItem, cancellationToken);
            newItems++;

            // Update job progress incrementally
            job.Result = new CrawlJobResult
            {
                NewItems = newItems,
                TotalChecked = newItems + skipped,
                SkippedItems = skipped
            };
            var updated = await _cosmosDb.UpdateCrawlJobAsync(job, cancellationToken);
            job.ETag = updated.ETag;
        }

        job.Result = new CrawlJobResult
        {
            NewItems = newItems,
            TotalChecked = updates.Count,
            SkippedItems = skipped
        };

        _logger.LogInformation(
            "Azure Updates crawl complete: {New} new, {Skipped} skipped, {Total} total",
            newItems, skipped, updates.Count);
    }

    internal static string GenerateDedupId(string updateId)
    {
        var input = $"azure-updates:{updateId.Trim().ToLowerInvariant()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant()[..32];
    }
}
