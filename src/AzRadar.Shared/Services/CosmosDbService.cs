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
    private Container? _docInsightsContainer;
    private Container? _appConfigContainer;
    private Container? _blastRadiusContainer;

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
        _docInsightsContainer = await CreateContainerIfNotExistsAsync(
            _settings.DocInsightsContainer, "/id", cancellationToken);
        _appConfigContainer = await CreateContainerIfNotExistsAsync(
            _settings.AppConfigContainer, "/id", cancellationToken);
        _blastRadiusContainer = await CreateContainerIfNotExistsAsync(
            _settings.BlastRadiusContainer, "/id", cancellationToken);

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

    private Container DocInsights => _docInsightsContainer
        ?? throw new InvalidOperationException("Call InitializeAsync first");

    private Container AppConfigDb => _appConfigContainer
        ?? throw new InvalidOperationException("Call InitializeAsync first");

    private Container BlastRadius => _blastRadiusContainer
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
        var response = await CrawlJobs.ReplaceItemAsync(
            job, job.Id, new PartitionKey(job.Id),
            cancellationToken: cancellationToken);
        var updated = response.Resource;
        updated.ETag = response.ETag;
        return updated;
    }

    public async Task<bool> TryClaimJobAsync(CrawlJob job, CancellationToken cancellationToken = default)
    {
        try
        {
            job.Status = CrawlJobStatus.Processing;
            job.StartedAt = DateTimeOffset.UtcNow;
            job.AttemptCount++;

            var options = new ItemRequestOptions();
            if (!string.IsNullOrEmpty(job.ETag))
            {
                options.IfMatchEtag = job.ETag;
            }

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
            return response.Resource;
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
            results.AddRange(response);
        }
        return results;
    }

    public async Task<bool> TryStoreFeedItemAsync(FeedItem item, CancellationToken cancellationToken = default)
    {
        try
        {
            await FeedItems.CreateItemAsync(
                item, new PartitionKey(item.Id), cancellationToken: cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            _logger.LogDebug("Feed item {Id} already exists, skipping", item.Id);
            return false;
        }
    }

    public async Task<DateTimeOffset?> GetLatestFeedItemDateAsync(
        string source, CancellationToken cancellationToken = default)
    {
        var query = FeedItems.GetItemQueryIterator<DateTimeOffset?>(
            new QueryDefinition(
                "SELECT VALUE MAX(c.publishDate) FROM c WHERE c.source = @source")
                .WithParameter("@source", source));

        if (query.HasMoreResults)
        {
            var response = await query.ReadNextAsync(cancellationToken);
            return response.FirstOrDefault();
        }
        return null;
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
        string? serviceName = null, int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var queryText = serviceName != null
            ? "SELECT TOP @limit * FROM c WHERE c.serviceName = @serviceName ORDER BY c.lastAnalyzedAt DESC"
            : "SELECT TOP @limit * FROM c ORDER BY c.lastAnalyzedAt DESC";

        var queryDef = new QueryDefinition(queryText).WithParameter("@limit", limit);
        if (serviceName != null)
            queryDef = queryDef.WithParameter("@serviceName", serviceName);

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
}
