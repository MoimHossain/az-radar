using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Models;
using Microsoft.Extensions.Logging;

namespace AzRadar.Shared.Services;

public class BlastRadiusJobHandler : IJobHandler
{
    private readonly IResourceGraphClient _argClient;
    private readonly ICosmosDbService _cosmosDb;
    private readonly ILogger<BlastRadiusJobHandler> _logger;

    private const int MaxResourcesPerType = 200;
    private const int MaxTopResources = 20;

    public string JobType => CrawlJobTypes.BlastRadiusScan;

    public BlastRadiusJobHandler(
        IResourceGraphClient argClient,
        ICosmosDbService cosmosDb,
        ILogger<BlastRadiusJobHandler> logger)
    {
        _argClient = argClient;
        _cosmosDb = cosmosDb;
        _logger = logger;
    }

    public async Task HandleAsync(CrawlJob job, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting Blast Radius scan job {JobId}", job.Id);

        // Get the configured UAMI for Resource Graph access
        var config = await _cosmosDb.GetAppConfigAsync(AppConfigKeys.BlastRadiusUamiClientId, cancellationToken);
        if (config == null || string.IsNullOrEmpty(config.Value))
        {
            job.Result = new CrawlJobResult();
            job.Error = "Blast Radius UAMI is not configured. Go to Settings → Blast Radius Config.";
            _logger.LogWarning("No UAMI configured for blast radius scan");
            return;
        }

        var uamiClientId = config.Value;
        _logger.LogInformation("Using UAMI {ClientId} for Resource Graph queries", uamiClientId);

        // Collect all retirements/deprecations from feed-items and doc-insights
        var feedItems = await _cosmosDb.GetFeedItemsAsync(limit: 500, cancellationToken: cancellationToken);
        var docInsights = await _cosmosDb.GetDocInsightsAsync(limit: 500, cancellationToken: cancellationToken);

        var retirementItems = new List<(string Id, string Title, string Source, LlmAnalysis Analysis)>();

        foreach (var fi in feedItems)
        {
            if (fi.LlmAnalysis == null) continue;
            if (!IsRetirementType(fi.LlmAnalysis.ChangeType)) continue;
            retirementItems.Add((fi.Id, fi.Title, "azure-updates", fi.LlmAnalysis));
        }

        foreach (var di in docInsights)
        {
            if (di.LlmAnalysis == null) continue;
            if (!IsRetirementType(di.LlmAnalysis.ChangeType)) continue;
            retirementItems.Add((di.Id, di.Title, "ms-learn", di.LlmAnalysis));
        }

        _logger.LogInformation("Found {Count} retirement/deprecation items to scan", retirementItems.Count);

        // Collect all unique resource types across all retirements
        var typeToItems = new Dictionary<string, List<(string Id, string Title, string Source, LlmAnalysis Analysis)>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var item in retirementItems)
        {
            var resourceTypes = ServiceToResourceTypeMap.ResolveAll(
                item.Analysis.AffectedResourceTypes,
                item.Analysis.AffectedServices);

            foreach (var rt in resourceTypes)
            {
                if (!typeToItems.ContainsKey(rt))
                    typeToItems[rt] = [];
                typeToItems[rt].Add(item);
            }
        }

        _logger.LogInformation("Resolved {Count} unique resource types to query", typeToItems.Count);

        int newResults = 0;
        int totalChecked = 0;

        // Batch query by resource type (not per-retirement)
        foreach (var (resourceType, items) in typeToItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            totalChecked++;

            _logger.LogInformation("Querying ARG for {ResourceType} ({Count} retirements)",
                resourceType, items.Count);

            var queryResult = await _argClient.QueryResourcesByTypeAsync(
                resourceType, uamiClientId, MaxResourcesPerType, cancellationToken);

            if (queryResult.TotalCount == 0)
            {
                _logger.LogInformation("No resources found for {ResourceType}", resourceType);
                continue;
            }

            // Create a summary for each retirement linked to this resource type
            foreach (var item in items)
            {
                var summaryId = BlastRadiusSummary.GenerateId(item.Id, resourceType);

                var regionBreakdown = queryResult.Resources
                    .GroupBy(r => r.Location)
                    .ToDictionary(g => g.Key, g => g.Count());

                var subBreakdown = queryResult.Resources
                    .GroupBy(r => r.SubscriptionId)
                    .ToDictionary(g => g.Key, g => g.Count());

                var summary = new BlastRadiusSummary
                {
                    Id = summaryId,
                    SourceItemId = item.Id,
                    SourceTitle = item.Title,
                    SourceType = item.Source,
                    ChangeType = item.Analysis.ChangeType,
                    Severity = item.Analysis.Severity,
                    Deadline = item.Analysis.Deadline,
                    ResourceType = resourceType,
                    MatchConfidence = item.Analysis.AffectedResourceTypes.Count > 0 ? "high" : "potential",
                    TotalResources = queryResult.TotalCount,
                    SubscriptionCount = subBreakdown.Count,
                    RegionBreakdown = regionBreakdown,
                    SubscriptionBreakdown = subBreakdown,
                    TopResources = queryResult.Resources
                        .Take(MaxTopResources)
                        .Select(r => new AffectedResource
                        {
                            SubscriptionId = r.SubscriptionId,
                            ResourceGroup = r.ResourceGroup,
                            Name = r.Name,
                            Location = r.Location,
                            Sku = r.Sku,
                            Tags = r.Tags,
                        })
                        .ToList(),
                    ScanJobId = job.Id,
                    ScannedAt = DateTimeOffset.UtcNow,
                };

                await _cosmosDb.UpsertBlastRadiusSummaryAsync(summary, cancellationToken);
                newResults++;
            }

            // Update job progress
            job.Result = new CrawlJobResult
            {
                NewItems = newResults,
                TotalChecked = totalChecked,
                SkippedItems = 0
            };
            var updated = await _cosmosDb.UpdateCrawlJobAsync(job, cancellationToken);
            job.ETag = updated.ETag;
        }

        job.Result = new CrawlJobResult
        {
            NewItems = newResults,
            TotalChecked = totalChecked,
            SkippedItems = typeToItems.Count - totalChecked
        };

        _logger.LogInformation(
            "Blast Radius scan complete: {Results} summaries across {Types} resource types",
            newResults, totalChecked);
    }

    private static bool IsRetirementType(string changeType) =>
        changeType is "retirement" or "deprecation" or "breaking-change";
}
