namespace AzRadar.Shared.Configuration;

public class CosmosDbSettings
{
    public const string SectionName = "CosmosDb";

    /// <summary>
    /// Cosmos DB account endpoint (e.g. https://myaccount.documents.azure.com:443/)
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = "az-radar-db";
    public string CrawlJobsContainer { get; set; } = "crawl-jobs";
    public string FeedItemsContainer { get; set; } = "feed-items";
    public string LeasesContainer { get; set; } = "change-feed-leases";
    public string WatchlistContainer { get; set; } = "watchlist";
    public string RepoWatchlistContainer { get; set; } = "repo-watchlist";
    public string DocInsightsContainer { get; set; } = "doc-insights";
    public string AppConfigContainer { get; set; } = "app-config";
    public string BlastRadiusContainer { get; set; } = "blast-radius-results";
    public string DiagnosticsContainer { get; set; } = "job-diagnostics";
    public string ServiceHealthSubscriptionsContainer { get; set; } = "service-health-subscriptions";
    public string ServiceHealthChannelsContainer { get; set; } = "service-health-channels";
    public string ServiceHealthEventsContainer { get; set; } = "service-health-events";
    public string ServiceHealthDeliveryIntentsContainer { get; set; } = "service-health-delivery-intents";
    public string ServiceHealthCheckpointsContainer { get; set; } = "service-health-checkpoints";
    public string ServiceHealthQuarantineContainer { get; set; } = "service-health-quarantine";

    /// <summary>
    /// Client ID of the UAMI used to authenticate with Cosmos DB.
    /// Shared with OpenAI — same identity for both.
    /// </summary>
    public string ManagedIdentityClientId { get; set; } = string.Empty;
}
