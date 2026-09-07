using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Models;
using Microsoft.Extensions.Logging;

namespace AzRadar.Shared.Services;

public class AzureUpdatesJobHandler : IJobHandler
{
    private readonly IAzureUpdatesSource _source;
    private readonly ILlmAnalyzer _llmAnalyzer;
    private readonly ICosmosDbService _cosmosDb;
    private readonly ILogger<AzureUpdatesJobHandler> _logger;

    public string JobType => CrawlJobTypes.AzureUpdates;

    public AzureUpdatesJobHandler(
        IAzureUpdatesSource source,
        ILlmAnalyzer llmAnalyzer,
        ICosmosDbService cosmosDb,
        ILogger<AzureUpdatesJobHandler> logger)
    {
        _source = source;
        _llmAnalyzer = llmAnalyzer;
        _cosmosDb = cosmosDb;
        _logger = logger;
    }

    public async Task HandleAsync(CrawlJob job, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting Azure Updates catalog and RSS crawl job {JobId}", job.Id);

        var snapshot = await _source.GetUpdatesAsync(cancellationToken);
        var updates = snapshot.Items;

        await _cosmosDb.StoreDiagnosticAsync(new JobDiagnosticEntry
        {
            JobId = job.Id,
            Step = "azure-updates-coverage",
            Message = $"Enumerated {snapshot.CatalogCount} catalog posts and {snapshot.RssCount} RSS entries; " +
                      $"{updates.Count} unique updates to check. No lookback or 50-item cutoff. " +
                      $"LLM analysis: {(job.SkipLlmAnalysis ? "disabled for this backfill" : "enabled")}.",
            ResultCount = updates.Count,
        }, cancellationToken);

        int newItems = 0;
        int updatedItems = 0;
        int skipped = 0;
        job.Result = new CrawlJobResult();

        foreach (var update in updates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var id = GenerateDedupId(update.Id);
            var contentHash = GenerateContentHash(update);

            var existing = await _cosmosDb.GetFeedItemAsync(id, cancellationToken);
            if (existing?.SourceContentHash == contentHash &&
                (existing.LlmAnalysis?.AiConfidence > 0 || existing.LlmAnalysisSkipped))
            {
                skipped++;
                if ((newItems + updatedItems + skipped) % 50 == 0)
                    await SaveProgressAsync();
                continue;
            }

            var feedItem = new FeedItem
            {
                Id = id,
                Source = CrawlJobTypes.AzureUpdates,
                Title = update.Title,
                Link = update.Link ?? $"https://azure.microsoft.com/updates?id={Uri.EscapeDataString(update.Id)}",
                PublishDate = DateTimeOffset.TryParse(update.Created, out var created)
                    ? created
                    : DateTimeOffset.Parse(update.Modified, System.Globalization.CultureInfo.InvariantCulture),
                Summary = update.Description,
                Categories = [.. update.Tags, .. update.ProductCategories],
                RawContent = update.Description,
                CrawlJobId = job.Id,
                FirstSeenAt = existing?.FirstSeenAt ?? DateTimeOffset.UtcNow,
                SourceContentHash = contentHash,
                LlmAnalysisSkipped = job.SkipLlmAnalysis,
                SourceModifiedAt = DateTimeOffset.Parse(update.Modified, System.Globalization.CultureInfo.InvariantCulture),
                ETag = existing?.ETag
            };

            if (!job.SkipLlmAnalysis)
            {
                _logger.LogInformation("Analyzing: {Title}", update.Title);
                var analysis = await _llmAnalyzer.AnalyzeFeedItemAsync(feedItem, cancellationToken);

                if (analysis.AffectedServices.Count == 0 && update.Products.Count > 0)
                    analysis.AffectedServices = update.Products;

                if (!string.IsNullOrWhiteSpace(analysis.SuggestedTitle))
                    feedItem.Title = analysis.SuggestedTitle;

                feedItem.LlmAnalysis = analysis;
            }

            if (existing != null)
            {
                if (!await _cosmosDb.TryReplaceFeedItemAsync(feedItem, cancellationToken))
                    throw new InvalidOperationException($"Azure update {update.Id} changed concurrently; retry the crawl.");
                updatedItems++;
            }
            else if (await _cosmosDb.TryStoreFeedItemAsync(feedItem, cancellationToken))
                newItems++;
            else
                skipped++;

            if (!job.SkipLlmAnalysis || (newItems + updatedItems + skipped) % 50 == 0)
                await SaveProgressAsync();
        }

        job.Result = new CrawlJobResult
        {
            NewItems = newItems,
            UpdatedItems = updatedItems,
            TotalChecked = updates.Count,
            SkippedItems = skipped
        };

        _logger.LogInformation(
            "Azure Updates crawl complete: {New} new, {Updated} updated, {Skipped} skipped, {Total} total",
            newItems, updatedItems, skipped, updates.Count);

        async Task SaveProgressAsync()
        {
            job.Result = new CrawlJobResult
            {
                NewItems = newItems,
                UpdatedItems = updatedItems,
                TotalChecked = newItems + updatedItems + skipped,
                SkippedItems = skipped
            };
            var updated = await _cosmosDb.UpdateCrawlJobAsync(job, cancellationToken);
            job.ETag = updated.ETag;
        }
    }

    internal static string GenerateContentHash(AzureUpdateItem update) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(update))).ToLowerInvariant();

    internal static string GenerateDedupId(string updateId)
    {
        var input = $"azure-updates:{updateId.Trim().ToLowerInvariant()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant()[..32];
    }
}
