using System.Net;
using AzRadar.Shared.Configuration;
using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AzRadar.Shared.Services;

public class CosmosDbService : ICosmosDbService
{
    private readonly CosmosClient _client;
    private readonly CosmosDbSettings _settings;
    private readonly ILogger<CosmosDbService> _logger;
    private Database? _database;
    private Container? _crawlJobsContainer;
    private Container? _feedItemsContainer;
    private Container? _watchlistContainer;
    private Container? _repoWatchlistContainer;
    private Container? _docInsightsContainer;
    private Container? _appConfigContainer;
    private Container? _blastRadiusContainer;
    private Container? _diagnosticsContainer;
    private Container? _serviceHealthSubscriptionsContainer;
    private Container? _serviceHealthChannelsContainer;
    private Container? _serviceHealthEventsContainer;
    private Container? _serviceHealthDeliveryIntentsContainer;
    private Container? _serviceHealthCheckpointsContainer;
    private Container? _serviceHealthQuarantineContainer;

    public CosmosDbService(
        CosmosClient client,
        IOptions<CosmosDbSettings> settings,
        ILogger<CosmosDbService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing Cosmos DB: database={Database}", _settings.DatabaseName);

        var dbResponse = await _client.CreateDatabaseIfNotExistsAsync(
            _settings.DatabaseName, cancellationToken: cancellationToken);
        _database = dbResponse.Database;

        _crawlJobsContainer = await CreateContainerIfNotExistsAsync(
            _settings.CrawlJobsContainer, "/id", cancellationToken);
        _feedItemsContainer = await CreateContainerIfNotExistsAsync(
            _settings.FeedItemsContainer, "/id", cancellationToken);
        // Lease container for Change Feed Processor
        await CreateContainerIfNotExistsAsync(
            _settings.LeasesContainer, "/id", cancellationToken);
        _watchlistContainer = await CreateContainerIfNotExistsAsync(
            _settings.WatchlistContainer, "/id", cancellationToken);
        _repoWatchlistContainer = await CreateContainerIfNotExistsAsync(
            _settings.RepoWatchlistContainer, "/id", cancellationToken);
        _docInsightsContainer = await CreateContainerIfNotExistsAsync(
            _settings.DocInsightsContainer, "/id", cancellationToken);
        _appConfigContainer = await CreateContainerIfNotExistsAsync(
            _settings.AppConfigContainer, "/id", cancellationToken);
        _blastRadiusContainer = await CreateContainerIfNotExistsAsync(
            _settings.BlastRadiusContainer, "/id", cancellationToken);
        _diagnosticsContainer = await CreateContainerIfNotExistsAsync(
            _settings.DiagnosticsContainer, "/jobId", cancellationToken);
        _serviceHealthSubscriptionsContainer = await CreateContainerIfNotExistsAsync(
            _settings.ServiceHealthSubscriptionsContainer, "/id", cancellationToken);
        _serviceHealthChannelsContainer = await CreateContainerIfNotExistsAsync(
            _settings.ServiceHealthChannelsContainer, "/id", cancellationToken);
        _serviceHealthEventsContainer = await CreateContainerIfNotExistsAsync(
            _settings.ServiceHealthEventsContainer, "/id", cancellationToken);
        _serviceHealthDeliveryIntentsContainer = await CreateContainerIfNotExistsAsync(
            _settings.ServiceHealthDeliveryIntentsContainer, "/id", cancellationToken);
        _serviceHealthCheckpointsContainer = await CreateContainerIfNotExistsAsync(
            _settings.ServiceHealthCheckpointsContainer, "/id", cancellationToken);
        _serviceHealthQuarantineContainer = await CreateContainerIfNotExistsAsync(
            _settings.ServiceHealthQuarantineContainer, "/id", cancellationToken);

        _logger.LogInformation("Cosmos DB initialized successfully");
    }

    private async Task<Container> CreateContainerIfNotExistsAsync(
        string containerName, string partitionKeyPath, CancellationToken ct)
    {
        var response = await _database!.CreateContainerIfNotExistsAsync(
            new ContainerProperties(containerName, partitionKeyPath),
            cancellationToken: ct);
        return response.Container;
    }

    private Container CrawlJobs => _crawlJobsContainer
        ?? throw new InvalidOperationException("Call InitializeAsync first");

    private Container FeedItems => _feedItemsContainer
        ?? throw new InvalidOperationException("Call InitializeAsync first");

    private Container Watchlist => _watchlistContainer
        ?? throw new InvalidOperationException("Call InitializeAsync first");

    private Container RepoWatchlist => _repoWatchlistContainer
        ?? throw new InvalidOperationException("Call InitializeAsync first");

    private Container DocInsights => _docInsightsContainer
        ?? throw new InvalidOperationException("Call InitializeAsync first");

    private Container AppConfigDb => _appConfigContainer
        ?? throw new InvalidOperationException("Call InitializeAsync first");

    private Container BlastRadius => _blastRadiusContainer
        ?? throw new InvalidOperationException("Call InitializeAsync first");

    private Container Diagnostics => _diagnosticsContainer
        ?? throw new InvalidOperationException("Call InitializeAsync first");

    private Container ServiceHealthSubscriptions => _serviceHealthSubscriptionsContainer
        ?? throw new InvalidOperationException("Call InitializeAsync first");

    private Container ServiceHealthChannels => _serviceHealthChannelsContainer
        ?? throw new InvalidOperationException("Call InitializeAsync first");

    private Container ServiceHealthEvents => _serviceHealthEventsContainer
        ?? throw new InvalidOperationException("Call InitializeAsync first");

    private Container ServiceHealthDeliveryIntents => _serviceHealthDeliveryIntentsContainer
        ?? throw new InvalidOperationException("Call InitializeAsync first");

    private Container ServiceHealthCheckpoints => _serviceHealthCheckpointsContainer
        ?? throw new InvalidOperationException("Call InitializeAsync first");

    private Container ServiceHealthQuarantine => _serviceHealthQuarantineContainer
        ?? throw new InvalidOperationException("Call InitializeAsync first");

    // --- CrawlJob operations ---

    public async Task<CrawlJob> CreateCrawlJobAsync(CrawlJob job, CancellationToken cancellationToken = default)
    {
        var response = await CrawlJobs.CreateItemAsync(
            job, new PartitionKey(job.Id), cancellationToken: cancellationToken);
        var created = response.Resource;
        created.ETag = response.ETag;
        return created;
    }

    public async Task<CrawlJob?> GetCrawlJobAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await CrawlJobs.ReadItemAsync<CrawlJob>(
                id, new PartitionKey(id), cancellationToken: cancellationToken);
            var job = response.Resource;
            job.ETag = response.ETag;
            return job;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<CrawlJob>> GetCrawlJobsAsync(
        int limit = 50, CancellationToken cancellationToken = default)
    {
        var query = CrawlJobs.GetItemQueryIterator<CrawlJob>(
            new QueryDefinition("SELECT TOP @limit * FROM c ORDER BY c.createdAt DESC")
                .WithParameter("@limit", limit));

        var results = new List<CrawlJob>();
        while (query.HasMoreResults)
        {
            var response = await query.ReadNextAsync(cancellationToken);
            results.AddRange(response);
        }
        return results;
    }

    public async Task<CrawlJob> UpdateCrawlJobAsync(CrawlJob job, CancellationToken cancellationToken = default)
    {
        var progressAt = DateTimeOffset.UtcNow;
        // Heartbeat and progress writers own different fields; neither replaces the document.
        var response = await CrawlJobs.PatchItemAsync<CrawlJob>(
            job.Id, new PartitionKey(job.Id),
            [
                PatchOperation.Set("/status", job.Status),
                PatchOperation.Set("/result", job.Result),
                PatchOperation.Set("/error", job.Error),
                PatchOperation.Set("/completedAt", job.CompletedAt),
                PatchOperation.Set("/lastProgressAt", progressAt)
            ],
            new PatchItemRequestOptions
            {
                FilterPredicate = $"FROM c WHERE c.attemptCount = {job.AttemptCount} AND c.status IN ('pending', 'processing')"
            }, cancellationToken);
        var updated = response.Resource;
        updated.ETag = response.ETag;
        return updated;
    }

    public async Task HeartbeatCrawlJobAsync(string id, int attemptCount, DateTimeOffset timestamp,
        CancellationToken cancellationToken = default)
    {
        await CrawlJobs.PatchItemAsync<CrawlJob>(id, new PartitionKey(id),
            [PatchOperation.Set("/lastHeartbeatAt", timestamp)],
            new PatchItemRequestOptions
            {
                FilterPredicate = $"FROM c WHERE c.status = 'processing' AND c.attemptCount = {attemptCount}"
            }, cancellationToken);
    }

    public async Task<bool> TryClaimJobAsync(CrawlJob job, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(job.ETag))
            throw new InvalidOperationException("Claiming a job requires its current ETag.");
        try
        {
            job.Status = CrawlJobStatus.Processing;
            job.StartedAt = DateTimeOffset.UtcNow;
            job.LastHeartbeatAt = job.StartedAt;
            job.AttemptCount++;

            var options = new ItemRequestOptions { IfMatchEtag = job.ETag };

            var response = await CrawlJobs.ReplaceItemAsync(
                job, job.Id, new PartitionKey(job.Id),
                options, cancellationToken);

            job.ETag = response.ETag;
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            _logger.LogInformation("Job {JobId} already claimed by another processor", job.Id);
            return false;
        }
    }

    public async Task<bool> DeleteCrawlJobAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            await CrawlJobs.DeleteItemAsync<CrawlJob>(id, new PartitionKey(id), cancellationToken: cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    // --- FeedItem operations ---

    public async Task<FeedItem?> GetFeedItemAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await FeedItems.ReadItemAsync<FeedItem>(
                id, new PartitionKey(id), cancellationToken: cancellationToken);
            var item = response.Resource;
            item.ETag = response.ETag;
            return FeedItemContentCodec.Decode(item);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<FeedItem>> GetFeedItemsAsync(
        string? source = null, int limit = 50, CancellationToken cancellationToken = default)
    {
        var queryText = source != null
            ? "SELECT TOP @limit * FROM c WHERE c.source = @source ORDER BY c.publishDate DESC"
            : "SELECT TOP @limit * FROM c ORDER BY c.publishDate DESC";

        var queryDef = new QueryDefinition(queryText).WithParameter("@limit", limit);
        if (source != null)
            queryDef = queryDef.WithParameter("@source", source);

        var query = FeedItems.GetItemQueryIterator<FeedItem>(queryDef);
        var results = new List<FeedItem>();
        while (query.HasMoreResults)
        {
            var response = await query.ReadNextAsync(cancellationToken);
            results.AddRange(response.Select(FeedItemContentCodec.Decode));
        }
        return results;
    }

    public async Task<bool> TryStoreFeedItemAsync(FeedItem item, CancellationToken cancellationToken = default)
    {
        try
        {
            await FeedItems.CreateItemAsync(
                FeedItemContentCodec.Encode(item), new PartitionKey(item.Id), cancellationToken: cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            _logger.LogDebug("Feed item {Id} already exists, skipping", item.Id);
            return false;
        }
    }

    public async Task<bool> TryReplaceFeedItemAsync(FeedItem item, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(item.ETag))
            throw new InvalidOperationException("Replacing a feed item requires its current ETag.");
        try
        {
            await FeedItems.ReplaceItemAsync(FeedItemContentCodec.Encode(item), item.Id, new PartitionKey(item.Id),
                new ItemRequestOptions { IfMatchEtag = item.ETag }, cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            _logger.LogWarning("Feed item {Id} changed concurrently; retry the crawl", item.Id);
            return false;
        }
    }

    // --- Watchlist operations ---

    public async Task<WatchlistItem> CreateWatchlistItemAsync(
        WatchlistItem item, CancellationToken cancellationToken = default)
    {
        var response = await Watchlist.CreateItemAsync(
            item, new PartitionKey(item.Id), cancellationToken: cancellationToken);
        return response.Resource;
    }

    public async Task<IReadOnlyList<WatchlistItem>> GetWatchlistAsync(
        CancellationToken cancellationToken = default)
    {
        var query = Watchlist.GetItemQueryIterator<WatchlistItem>(
            new QueryDefinition("SELECT * FROM c ORDER BY c.serviceName"));
        var results = new List<WatchlistItem>();
        while (query.HasMoreResults)
        {
            var response = await query.ReadNextAsync(cancellationToken);
            results.AddRange(response);
        }
        return results;
    }

    public async Task<bool> DeleteWatchlistItemAsync(
        string id, CancellationToken cancellationToken = default)
    {
        try
        {
            await Watchlist.DeleteItemAsync<WatchlistItem>(
                id, new PartitionKey(id), cancellationToken: cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    // --- Repository watchlist operations (GitHub Change Radar) ---

    public async Task<RepoWatchItem> CreateRepoWatchAsync(
        RepoWatchItem item, CancellationToken cancellationToken = default)
    {
        var response = await RepoWatchlist.CreateItemAsync(
            item, new PartitionKey(item.Id), cancellationToken: cancellationToken);
        var created = response.Resource;
        created.ETag = response.ETag;
        return created;
    }

    public async Task<IReadOnlyList<RepoWatchItem>> GetRepoWatchlistAsync(
        CancellationToken cancellationToken = default)
    {
        var query = RepoWatchlist.GetItemQueryIterator<RepoWatchItem>(
            new QueryDefinition("SELECT * FROM c ORDER BY c.addedAt DESC"));
        var results = new List<RepoWatchItem>();
        while (query.HasMoreResults)
        {
            var response = await query.ReadNextAsync(cancellationToken);
            results.AddRange(response);
        }
        return results;
    }

    public async Task<RepoWatchItem?> GetRepoWatchAsync(
        string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await RepoWatchlist.ReadItemAsync<RepoWatchItem>(
                id, new PartitionKey(id), cancellationToken: cancellationToken);
            var item = response.Resource;
            item.ETag = response.ETag;
            return item;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<RepoWatchItem> UpdateRepoWatchAsync(
        RepoWatchItem item, CancellationToken cancellationToken = default)
    {
        var response = await RepoWatchlist.UpsertItemAsync(
            item, new PartitionKey(item.Id), cancellationToken: cancellationToken);
        var updated = response.Resource;
        updated.ETag = response.ETag;
        return updated;
    }

    public async Task<bool> DeleteRepoWatchAsync(
        string id, CancellationToken cancellationToken = default)
    {
        try
        {
            await RepoWatchlist.DeleteItemAsync<RepoWatchItem>(
                id, new PartitionKey(id), cancellationToken: cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    // --- DocInsight operations ---

    public async Task<DocInsight?> GetDocInsightAsync(
        string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await DocInsights.ReadItemAsync<DocInsight>(
                id, new PartitionKey(id), cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<DocInsight>> GetDocInsightsAsync(
        string? serviceName = null, int limit = 50, string? source = null,
        CancellationToken cancellationToken = default)
    {
        var conditions = new List<string>();
        if (serviceName != null) conditions.Add("c.serviceName = @serviceName");
        if (source != null) conditions.Add("c.source = @source");
        var where = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : "";
        var queryText = $"SELECT TOP @limit * FROM c{where} ORDER BY c.lastAnalyzedAt DESC";

        var queryDef = new QueryDefinition(queryText).WithParameter("@limit", limit);
        if (serviceName != null)
            queryDef = queryDef.WithParameter("@serviceName", serviceName);
        if (source != null)
            queryDef = queryDef.WithParameter("@source", source);

        var query = DocInsights.GetItemQueryIterator<DocInsight>(queryDef);
        var results = new List<DocInsight>();
        while (query.HasMoreResults)
        {
            var response = await query.ReadNextAsync(cancellationToken);
            results.AddRange(response);
        }
        return results;
    }

    public async Task<bool> UpsertDocInsightAsync(
        DocInsight insight, CancellationToken cancellationToken = default)
    {
        await DocInsights.UpsertItemAsync(
            insight, new PartitionKey(insight.Id), cancellationToken: cancellationToken);
        return true;
    }

    // --- AppConfig operations ---

    public async Task<AppConfig?> GetAppConfigAsync(
        string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await AppConfigDb.ReadItemAsync<AppConfig>(
                key, new PartitionKey(key), cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task UpsertAppConfigAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        config.UpdatedAt = DateTimeOffset.UtcNow;
        await AppConfigDb.UpsertItemAsync(
            config, new PartitionKey(config.Id), cancellationToken: cancellationToken);
    }

    // --- BlastRadius operations ---

    public async Task UpsertBlastRadiusSummaryAsync(
        BlastRadiusSummary summary, CancellationToken cancellationToken = default)
    {
        await BlastRadius.UpsertItemAsync(
            summary, new PartitionKey(summary.Id), cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<BlastRadiusSummary>> GetBlastRadiusSummariesAsync(
        int limit = 100, CancellationToken cancellationToken = default)
    {
        var query = BlastRadius.GetItemQueryIterator<BlastRadiusSummary>(
            new QueryDefinition("SELECT TOP @limit * FROM c ORDER BY c.totalResources DESC")
                .WithParameter("@limit", limit));
        var results = new List<BlastRadiusSummary>();
        while (query.HasMoreResults)
        {
            var response = await query.ReadNextAsync(cancellationToken);
            results.AddRange(response);
        }
        return results;
    }

    public async Task<BlastRadiusSummary?> GetBlastRadiusSummaryAsync(
        string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await BlastRadius.ReadItemAsync<BlastRadiusSummary>(
                id, new PartitionKey(id), cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    // --- Diagnostics operations ---

    public async Task StoreDiagnosticAsync(
        JobDiagnosticEntry entry, CancellationToken cancellationToken = default)
    {
        await Diagnostics.CreateItemAsync(
            entry, new PartitionKey(entry.JobId), cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<JobDiagnosticEntry>> GetDiagnosticsForJobAsync(
        string jobId, CancellationToken cancellationToken = default)
    {
        var query = Diagnostics.GetItemQueryIterator<JobDiagnosticEntry>(
            new QueryDefinition("SELECT * FROM c WHERE c.jobId = @jobId ORDER BY c.timestamp")
                .WithParameter("@jobId", jobId));
        var results = new List<JobDiagnosticEntry>();
        while (query.HasMoreResults)
        {
            var response = await query.ReadNextAsync(cancellationToken);
            results.AddRange(response);
        }
        return results;
    }

    // --- Service Health subscription registry ---

    public async Task<ServiceHealthSubscription> UpsertServiceHealthSubscriptionAsync(
        ServiceHealthSubscription subscription, CancellationToken cancellationToken = default)
    {
        subscription.UpdatedAt = DateTimeOffset.UtcNow;
        var response = await ServiceHealthSubscriptions.UpsertItemAsync(
            subscription, new PartitionKey(subscription.Id), cancellationToken: cancellationToken);
        return response.Resource;
    }

    public async Task<ServiceHealthSubscription?> GetServiceHealthSubscriptionAsync(
        string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await ServiceHealthSubscriptions.ReadItemAsync<ServiceHealthSubscription>(
                id, new PartitionKey(id), cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<ServiceHealthSubscription>> GetServiceHealthSubscriptionsAsync(
        CancellationToken cancellationToken = default)
    {
        var query = ServiceHealthSubscriptions.GetItemQueryIterator<ServiceHealthSubscription>(
            new QueryDefinition("SELECT * FROM c ORDER BY c.createdAt DESC"));
        var results = new List<ServiceHealthSubscription>();
        while (query.HasMoreResults)
        {
            var response = await query.ReadNextAsync(cancellationToken);
            results.AddRange(response);
        }
        return results;
    }

    public async Task<bool> DeleteServiceHealthSubscriptionAsync(
        string id, CancellationToken cancellationToken = default)
    {
        try
        {
            await ServiceHealthSubscriptions.DeleteItemAsync<ServiceHealthSubscription>(
                id, new PartitionKey(id), cancellationToken: cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    // --- Service Health notification channels ---

    public async Task<ServiceHealthNotificationChannel> UpsertServiceHealthChannelAsync(
        ServiceHealthNotificationChannel channel, CancellationToken cancellationToken = default)
    {
        channel.UpdatedAt = DateTimeOffset.UtcNow;
        var response = await ServiceHealthChannels.UpsertItemAsync(
            channel, new PartitionKey(channel.Id), cancellationToken: cancellationToken);
        return response.Resource;
    }

    public async Task<IReadOnlyList<ServiceHealthNotificationChannel>> GetServiceHealthChannelsAsync(
        CancellationToken cancellationToken = default)
    {
        var query = ServiceHealthChannels.GetItemQueryIterator<ServiceHealthNotificationChannel>(
            new QueryDefinition("SELECT * FROM c ORDER BY c.createdAt DESC"));
        var results = new List<ServiceHealthNotificationChannel>();
        while (query.HasMoreResults)
        {
            var response = await query.ReadNextAsync(cancellationToken);
            results.AddRange(response);
        }
        return results;
    }

    public async Task<bool> DeleteServiceHealthChannelAsync(
        string id, CancellationToken cancellationToken = default)
    {
        try
        {
            await ServiceHealthChannels.DeleteItemAsync<ServiceHealthNotificationChannel>(
                id, new PartitionKey(id), cancellationToken: cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    // --- Service Health ingestion ---

    public async Task<bool> TryStoreServiceHealthEventAsync(
        ServiceHealthEvent serviceHealthEvent,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await ServiceHealthEvents.CreateItemAsync(
                serviceHealthEvent,
                new PartitionKey(serviceHealthEvent.Id),
                cancellationToken: cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<ServiceHealthEvent>> GetServiceHealthEventsAsync(
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var query = ServiceHealthEvents.GetItemQueryIterator<ServiceHealthEvent>(
            new QueryDefinition("SELECT TOP @limit * FROM c ORDER BY c.receivedAt DESC")
                .WithParameter("@limit", Math.Clamp(limit, 1, 200)));
        var results = new List<ServiceHealthEvent>();
        while (query.HasMoreResults)
        {
            var response = await query.ReadNextAsync(cancellationToken);
            results.AddRange(response);
        }
        return results;
    }

    public async Task<bool> DeleteServiceHealthEventAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var intents = ServiceHealthDeliveryIntents.GetItemQueryIterator<ServiceHealthDeliveryIntent>(
            new QueryDefinition("SELECT * FROM c WHERE c.eventId = @eventId")
                .WithParameter("@eventId", id));
        var relatedIntents = new List<ServiceHealthDeliveryIntent>();
        while (intents.HasMoreResults)
        {
            var response = await intents.ReadNextAsync(cancellationToken);
            relatedIntents.AddRange(response);
        }

        var activeIntent = relatedIntents.FirstOrDefault(intent =>
            intent.Status is ServiceHealthDeliveryIntentStatuses.Queued
                or ServiceHealthDeliveryIntentStatuses.Dispatching
                or ServiceHealthDeliveryIntentStatuses.RetryScheduled);
        if (activeIntent != null)
        {
            throw new InvalidOperationException(
                $"Delivery intent '{activeIntent.Id}' is still active and must finish before the event can be deleted.");
        }

        foreach (var intent in relatedIntents)
        {
            await ServiceHealthDeliveryIntents.DeleteItemAsync<ServiceHealthDeliveryIntent>(
                intent.Id,
                new PartitionKey(intent.Id),
                cancellationToken: cancellationToken);
        }

        try
        {
            await ServiceHealthEvents.DeleteItemAsync<ServiceHealthEvent>(
                id,
                new PartitionKey(id),
                cancellationToken: cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<bool> TryCreateServiceHealthDeliveryIntentAsync(
        ServiceHealthDeliveryIntent intent,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await ServiceHealthDeliveryIntents.CreateItemAsync(
                intent,
                new PartitionKey(intent.Id),
                cancellationToken: cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<ServiceHealthDeliveryIntent>> GetServiceHealthDeliveryIntentsAsync(
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var query = ServiceHealthDeliveryIntents.GetItemQueryIterator<ServiceHealthDeliveryIntent>(
            new QueryDefinition("SELECT TOP @limit * FROM c ORDER BY c.createdAt DESC")
                .WithParameter("@limit", Math.Clamp(limit, 1, 200)));
        var results = new List<ServiceHealthDeliveryIntent>();
        while (query.HasMoreResults)
        {
            var response = await query.ReadNextAsync(cancellationToken);
            results.AddRange(response);
        }
        return results;
    }

    public async Task<bool> DeleteServiceHealthDeliveryIntentAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        ServiceHealthDeliveryIntent intent;
        try
        {
            var response = await ServiceHealthDeliveryIntents.ReadItemAsync<ServiceHealthDeliveryIntent>(
                id,
                new PartitionKey(id),
                cancellationToken: cancellationToken);
            intent = response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        if (intent.Status is ServiceHealthDeliveryIntentStatuses.Queued
            or ServiceHealthDeliveryIntentStatuses.Dispatching
            or ServiceHealthDeliveryIntentStatuses.RetryScheduled)
        {
            throw new InvalidOperationException(
                $"Delivery intent '{id}' is still active and cannot be deleted.");
        }

        await ServiceHealthDeliveryIntents.DeleteItemAsync<ServiceHealthDeliveryIntent>(
            id,
            new PartitionKey(id),
            cancellationToken: cancellationToken);
        return true;
    }

    public async Task<ServiceHealthEventCheckpoint?> GetServiceHealthCheckpointAsync(
        string partitionId,
        CancellationToken cancellationToken = default)
    {
        var id = $"partition-{partitionId}";
        try
        {
            var response = await ServiceHealthCheckpoints.ReadItemAsync<ServiceHealthEventCheckpoint>(
                id,
                new PartitionKey(id),
                cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task UpsertServiceHealthCheckpointAsync(
        ServiceHealthEventCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        checkpoint.Id = $"partition-{checkpoint.PartitionId}";
        checkpoint.UpdatedAt = DateTimeOffset.UtcNow;
        await ServiceHealthCheckpoints.UpsertItemAsync(
            checkpoint,
            new PartitionKey(checkpoint.Id),
            cancellationToken: cancellationToken);
    }

    public async Task StoreServiceHealthQuarantineRecordAsync(
        ServiceHealthQuarantineRecord record,
        CancellationToken cancellationToken = default)
    {
        await ServiceHealthQuarantine.CreateItemAsync(
            record,
            new PartitionKey(record.Id),
            cancellationToken: cancellationToken);
    }
}
