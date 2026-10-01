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
        var watchlist = await _cosmosDb.GetWatchlistAsync(cancellationToken);

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
        int discarded = 0;
        int auditedDiscards = 0;
        job.Result = new CrawlJobResult();

        foreach (var update in updates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var id = GenerateDedupId(update.Id);
            var contentHash = GenerateContentHash(update);

            var existing = await _cosmosDb.GetFeedItemAsync(id, cancellationToken);
            if (!job.SkipLlmAnalysis && watchlist.Count == 0)
            {
                if (existing != null)
                    await _cosmosDb.DeleteFeedItemAsync(id, cancellationToken);
                discarded++;
                skipped++;
                if ((newItems + updatedItems + skipped) % 50 == 0)
                    await SaveProgressAsync();
                continue;
            }

            if (existing?.SourceContentHash == contentHash &&
                (existing.LlmAnalysis?.AiConfidence > 0 || existing.LlmAnalysisSkipped))
            {
                if (!job.SkipLlmAnalysis && !existing.LlmAnalysisSkipped &&
                    FindWatchlistMatch(existing.LlmAnalysis, update.Products) is null)
                {
                    await _cosmosDb.DeleteFeedItemAsync(id, cancellationToken);
                    discarded++;
                    skipped++;
                    _logger.LogInformation(
                        "Deleted Azure update {Id} because it no longer matches the service and region watchlist",
                        update.Id);
                    await AuditDiscardAsync(existing.Title, update, existing.LlmAnalysis, "no longer matches");
                    continue;
                }

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
                Products = [.. update.Products],
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

            if (!job.SkipLlmAnalysis &&
                FindWatchlistMatch(feedItem.LlmAnalysis, update.Products) is null)
            {
                if (existing != null)
                    await _cosmosDb.DeleteFeedItemAsync(id, cancellationToken);
                discarded++;
                skipped++;
                _logger.LogInformation(
                    "Discarded Azure update {Id} because it does not match the service and region watchlist",
                    update.Id);
                await AuditDiscardAsync(feedItem.Title, update, feedItem.LlmAnalysis, "not matched");
                if (!job.SkipLlmAnalysis || (newItems + updatedItems + skipped) % 50 == 0)
                    await SaveProgressAsync();
                continue;
            }

            if (existing != null)
            {
                if (!await _cosmosDb.TryReplaceFeedItemAsync(feedItem, cancellationToken) &&
                    !await RetryReplaceAfterMetadataPatchAsync(feedItem, existing))
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
            SkippedItems = skipped,
            DiscardedItems = discarded
        };

        if (discarded > 0)
        {
            await _cosmosDb.StoreDiagnosticAsync(new JobDiagnosticEntry
            {
                JobId = job.Id,
                Step = "watchlist-discard-summary",
                Message = watchlist.Count == 0
                    ? $"Discarded all {discarded} updates because the watchlist is empty."
                    : $"Discarded {discarded} updates that did not match the service and region watchlist " +
                      $"({auditedDiscards} itemized as 'watchlist-discard' entries, limit {MaxItemizedDiscards}).",
                ResultCount = discarded,
            }, cancellationToken);
        }

        _logger.LogInformation(
            "Azure Updates crawl complete: {New} new, {Updated} updated, {Skipped} skipped, " +
            "{Discarded} discarded by watchlist, {Total} total",
            newItems, updatedItems, skipped, discarded, updates.Count);

        WatchlistItem? FindWatchlistMatch(LlmAnalysis? analysis, IEnumerable<string> sourceProducts)
        {
            var affectedServices = (analysis?.AffectedServices ?? [])
                .Concat(sourceProducts)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            return WatchlistRelevanceMatcher.FindMatch(
                watchlist, affectedServices, analysis?.AffectedRegions);
        }

        async Task AuditDiscardAsync(string title, AzureUpdateItem update, LlmAnalysis? analysis, string reason)
        {
            if (auditedDiscards >= MaxItemizedDiscards)
                return;
            auditedDiscards++;
            var services = string.Join(", ", (analysis?.AffectedServices ?? []).Take(8));
            var products = string.Join(", ", update.Products.Take(8));
            var regions = string.Join(", ", (analysis?.AffectedRegions ?? []).Take(8));
            await _cosmosDb.StoreDiagnosticAsync(new JobDiagnosticEntry
            {
                JobId = job.Id,
                Step = "watchlist-discard",
                ItemTitle = title,
                Message = $"Discarded ({reason}). Update id: {update.Id}; change type: {analysis?.ChangeType ?? "n/a"}; " +
                          $"deadline: {analysis?.Deadline ?? "none"}; services: [{services}]; products: [{products}]; " +
                          $"regions: [{regions}].",
            }, cancellationToken);
        }

        async Task<bool> RetryReplaceAfterMetadataPatchAsync(FeedItem feedItem, FeedItem existing)
        {
            // The lifecycle-deadline backfill patches metadata only; the source content is unchanged
            // when the hash still matches the version this crawl read, so a single retry is safe.
            var current = await _cosmosDb.GetFeedItemAsync(feedItem.Id, cancellationToken);
            if (current is null || current.SourceContentHash != existing.SourceContentHash ||
                current.CrawlJobId != existing.CrawlJobId)
                return false;
            feedItem.ETag = current.ETag;
            return await _cosmosDb.TryReplaceFeedItemAsync(feedItem, cancellationToken);
        }

        async Task SaveProgressAsync()
        {
            job.Result = new CrawlJobResult
            {
                NewItems = newItems,
                UpdatedItems = updatedItems,
                TotalChecked = newItems + updatedItems + skipped,
                SkippedItems = skipped,
                DiscardedItems = discarded
            };
            var updated = await _cosmosDb.UpdateCrawlJobAsync(job, cancellationToken);
            job.ETag = updated.ETag;
        }
    }

    internal const int MaxItemizedDiscards = 100;

    internal static string GenerateContentHash(AzureUpdateItem update) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(update))).ToLowerInvariant();

    internal static string GenerateDedupId(string updateId)
    {
        var input = $"azure-updates:{updateId.Trim().ToLowerInvariant()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant()[..32];
    }
}
