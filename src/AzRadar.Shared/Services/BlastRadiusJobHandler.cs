using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Models;
using Microsoft.Extensions.Logging;

namespace AzRadar.Shared.Services;

public class BlastRadiusJobHandler : IJobHandler
{
    private readonly IResourceGraphClient _argClient;
    private readonly ILlmAnalyzer _llmAnalyzer;
    private readonly ICosmosDbService _cosmosDb;
    private readonly ILogger<BlastRadiusJobHandler> _logger;

    private const int MaxTopResources = 20;
    private const int MaxLlmAttempts = 3;

    public string JobType => CrawlJobTypes.BlastRadiusScan;

    public BlastRadiusJobHandler(
        IResourceGraphClient argClient,
        ILlmAnalyzer llmAnalyzer,
        ICosmosDbService cosmosDb,
        ILogger<BlastRadiusJobHandler> logger)
    {
        _argClient = argClient;
        _llmAnalyzer = llmAnalyzer;
        _cosmosDb = cosmosDb;
        _logger = logger;
    }

    public async Task HandleAsync(CrawlJob job, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting Blast Radius scan job {JobId}", job.Id);

        var config = await _cosmosDb.GetAppConfigAsync(AppConfigKeys.BlastRadiusUamiClientId, cancellationToken);
        if (config == null || string.IsNullOrEmpty(config.Value))
        {
            job.Result = new CrawlJobResult();
            job.Error = "Blast Radius UAMI is not configured. Go to Settings → Blast Radius Config.";
            return;
        }

        var uamiClientId = config.Value;

        // Collect retirements from both sources, excluding >90 days overdue
        var retirementItems = await CollectRelevantRetirementsAsync(cancellationToken);
        _logger.LogInformation("Found {Count} relevant retirement items to scan", retirementItems.Count);

        int newResults = 0;
        int skipped = 0;
        int totalChecked = 0;

        foreach (var item in retirementItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            totalChecked++;

            // Ask LLM to generate a targeted ARG query
            var argQuery = await _llmAnalyzer.GenerateResourceGraphQueryAsync(
                item.Title, item.Description,
                item.Analysis.AffectedServices,
                item.Analysis.AffectedResourceTypes,
                item.Analysis.ActionRequired,
                cancellationToken);

            if (argQuery == null)
            {
                _logger.LogInformation("LLM skipped (not queryable): {Title}", item.Title);
                skipped++;
                continue;
            }

            // Try running the query with up to 3 LLM attempts
            ResourceGraphQueryResult? queryResult = null;
            string? usedQuery = null;
            string? lastError = null;

            for (int attempt = 1; attempt <= MaxLlmAttempts; attempt++)
            {
                try
                {
                    _logger.LogInformation("ARG query attempt {Attempt} for: {Title}", attempt, item.Title);
                    queryResult = await _argClient.RunQueryAsync(argQuery, uamiClientId, cancellationToken);
                    usedQuery = argQuery;
                    break;
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                    _logger.LogWarning("ARG attempt {Attempt} failed: {Error}", attempt, ex.Message);

                    if (attempt < MaxLlmAttempts)
                    {
                        // Ask LLM to fix the query based on the error
                        argQuery = await RetryQueryGenerationAsync(
                            item, argQuery, ex.Message, cancellationToken);
                        if (argQuery == null) break;
                    }
                }
            }

            if (queryResult == null || usedQuery == null)
            {
                _logger.LogWarning("All ARG attempts failed for: {Title}. Last error: {Error}",
                    item.Title, lastError);
                skipped++;
                continue;
            }

            if (queryResult.TotalCount == 0)
            {
                _logger.LogInformation("No resources found for: {Title}", item.Title);
                continue;
            }

            // Store the summary
            var summaryId = BlastRadiusSummary.GenerateId(item.Id, item.Title);
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
                ResourceType = queryResult.Resources.FirstOrDefault()?.Type ?? "",
                MatchConfidence = "high",
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
                    }).ToList(),
                ScanJobId = job.Id,
                ScannedAt = DateTimeOffset.UtcNow,
                SourceDescription = item.Description,
                SourceLink = item.Link,
                ActionRequired = item.Analysis.ActionRequired,
                ArgQuery = usedQuery,
            };

            await _cosmosDb.UpsertBlastRadiusSummaryAsync(summary, cancellationToken);
            newResults++;

            // Update progress
            job.Result = new CrawlJobResult
            {
                NewItems = newResults,
                TotalChecked = totalChecked,
                SkippedItems = skipped
            };
            var updated = await _cosmosDb.UpdateCrawlJobAsync(job, cancellationToken);
            job.ETag = updated.ETag;
        }

        job.Result = new CrawlJobResult
        {
            NewItems = newResults,
            TotalChecked = totalChecked,
            SkippedItems = skipped
        };

        _logger.LogInformation(
            "Blast Radius scan complete: {Results} impacted, {Skipped} skipped, {Total} checked",
            newResults, skipped, totalChecked);
    }

    private async Task<List<RetirementItem>> CollectRelevantRetirementsAsync(CancellationToken ct)
    {
        var feedItems = await _cosmosDb.GetFeedItemsAsync(limit: 500, cancellationToken: ct);
        var docInsights = await _cosmosDb.GetDocInsightsAsync(limit: 500, cancellationToken: ct);
        var items = new List<RetirementItem>();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-90);

        foreach (var fi in feedItems)
        {
            if (fi.LlmAnalysis == null || !IsRetirementType(fi.LlmAnalysis.ChangeType)) continue;
            if (IsOverdueBeyondCutoff(fi.LlmAnalysis.Deadline, cutoff)) continue;
            items.Add(new RetirementItem(fi.Id, fi.Title, fi.Summary, fi.Link, "azure-updates", fi.LlmAnalysis));
        }

        foreach (var di in docInsights)
        {
            if (di.LlmAnalysis == null || !IsRetirementType(di.LlmAnalysis.ChangeType)) continue;
            if (IsOverdueBeyondCutoff(di.LlmAnalysis.Deadline, cutoff)) continue;
            items.Add(new RetirementItem(di.Id, di.Title, di.Snippet, di.DocUrl, "ms-learn", di.LlmAnalysis));
        }

        return items;
    }

    private async Task<string?> RetryQueryGenerationAsync(
        RetirementItem item, string failedQuery, string error, CancellationToken ct)
    {
        _logger.LogInformation("Asking LLM to fix query for: {Title}", item.Title);

        var retryPrompt = $"""
            The following Azure Resource Graph KQL query failed with an error.
            Fix the query and return only the corrected KQL. If it cannot be fixed, return "SKIP".
            
            Failed query:
            {failedQuery}
            
            Error:
            {error}
            
            Original context:
            Title: {item.Title}
            Affected Services: {string.Join(", ", item.Analysis.AffectedServices)}
            Affected Resource Types: {string.Join(", ", item.Analysis.AffectedResourceTypes)}
            """;

        return await _llmAnalyzer.GenerateResourceGraphQueryAsync(
            item.Title, retryPrompt,
            item.Analysis.AffectedServices,
            item.Analysis.AffectedResourceTypes,
            item.Analysis.ActionRequired, ct);
    }

    private static bool IsRetirementType(string changeType) =>
        changeType is "retirement" or "deprecation" or "breaking-change";

    private static bool IsOverdueBeyondCutoff(string? deadline, DateTimeOffset cutoff)
    {
        if (string.IsNullOrEmpty(deadline)) return false;
        return DateTimeOffset.TryParse(deadline, out var dl) && dl < cutoff;
    }

    private record RetirementItem(
        string Id, string Title, string Description, string Link,
        string Source, LlmAnalysis Analysis);
}
