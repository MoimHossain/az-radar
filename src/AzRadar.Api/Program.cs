using System.Text.Json;
using AzRadar.Shared;
using AzRadar.Shared.Configuration;
using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Models;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Services.Configure<CosmosDbSettings>(builder.Configuration.GetSection(CosmosDbSettings.SectionName));
builder.Services.Configure<OpenAiSettings>(builder.Configuration.GetSection(OpenAiSettings.SectionName));

// Register shared services
builder.Services.AddAzRadarSharedServices();

// CORS for local dev
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

var app = builder.Build();

// Initialize Cosmos DB
var cosmosDb = app.Services.GetRequiredService<ICosmosDbService>();
await cosmosDb.InitializeAsync();

app.UseCors();

// Serve static frontend files from /wwwroot
var wwwrootPath = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
if (Directory.Exists(wwwrootPath))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

// --- Health check ---
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", timestamp = DateTimeOffset.UtcNow }));

// --- CrawlJob endpoints ---
app.MapPost("/api/crawl-jobs", async (CreateCrawlJobRequest request, ICosmosDbService db) =>
{
    var job = new CrawlJob
    {
        JobType = request.JobType,
        Status = CrawlJobStatus.Pending
    };
    var created = await db.CreateCrawlJobAsync(job);
    return Results.Created($"/api/crawl-jobs/{created.Id}", created);
});

app.MapGet("/api/crawl-jobs", async (ICosmosDbService db, int? limit) =>
{
    var jobs = await db.GetCrawlJobsAsync(limit ?? 50);
    return Results.Ok(jobs);
});

app.MapGet("/api/crawl-jobs/{id}", async (string id, ICosmosDbService db) =>
{
    var job = await db.GetCrawlJobAsync(id);
    return job is null ? Results.NotFound() : Results.Ok(job);
});

app.MapDelete("/api/crawl-jobs/{id}", async (string id, ICosmosDbService db) =>
{
    var deleted = await db.DeleteCrawlJobAsync(id);
    return deleted ? Results.NoContent() : Results.NotFound();
});

// --- FeedItem endpoints ---
app.MapGet("/api/feed-items", async (ICosmosDbService db, string? source, int? limit) =>
{
    var items = await db.GetFeedItemsAsync(source, limit ?? 50);
    return Results.Ok(items);
});

app.MapGet("/api/feed-items/{id}", async (string id, ICosmosDbService db) =>
{
    var item = await db.GetFeedItemAsync(id);
    return item is null ? Results.NotFound() : Results.Ok(item);
});

// --- Dashboard stats ---
app.MapGet("/api/dashboard/stats", async (ICosmosDbService db) =>
{
    var jobs = await db.GetCrawlJobsAsync(100);
    var feedItems = await db.GetFeedItemsAsync(limit: 200);
    var docInsights = await db.GetDocInsightsAsync(limit: 200);

    var stats = new
    {
        totalJobs = jobs.Count,
        pendingJobs = jobs.Count(j => j.Status == CrawlJobStatus.Pending),
        completedJobs = jobs.Count(j => j.Status == CrawlJobStatus.Completed),
        failedJobs = jobs.Count(j => j.Status == CrawlJobStatus.Failed),
        totalFeedItems = feedItems.Count,
        criticalItems = feedItems.Count(f => f.LlmAnalysis?.Severity == SeverityLevels.Critical),
        highItems = feedItems.Count(f => f.LlmAnalysis?.Severity == SeverityLevels.High),
        totalDocInsights = docInsights.Count,
        latestCrawl = jobs.OrderByDescending(j => j.CreatedAt).FirstOrDefault()?.CreatedAt,
    };

    return Results.Ok(stats);
});

// --- Watchlist endpoints ---
app.MapGet("/api/watchlist", async (ICosmosDbService db) =>
{
    var items = await db.GetWatchlistAsync();
    return Results.Ok(items);
});

app.MapPost("/api/watchlist", async (CreateWatchlistRequest request, ICosmosDbService db) =>
{
    var item = new WatchlistItem
    {
        ServiceName = request.ServiceName,
        Aliases = request.Aliases ?? [],
        SearchTerms = request.SearchTerms ?? [],
        ResourceProvider = request.ResourceProvider ?? string.Empty,
    };
    var created = await db.CreateWatchlistItemAsync(item);
    return Results.Created($"/api/watchlist/{created.Id}", created);
});

app.MapDelete("/api/watchlist/{id}", async (string id, ICosmosDbService db) =>
{
    var deleted = await db.DeleteWatchlistItemAsync(id);
    return deleted ? Results.NoContent() : Results.NotFound();
});

// --- DocInsight endpoints ---
app.MapGet("/api/doc-insights", async (ICosmosDbService db, string? serviceName, int? limit) =>
{
    var items = await db.GetDocInsightsAsync(serviceName, limit ?? 50);
    return Results.Ok(items);
});

app.MapGet("/api/doc-insights/{id}", async (string id, ICosmosDbService db) =>
{
    var item = await db.GetDocInsightAsync(id);
    return item is null ? Results.NotFound() : Results.Ok(item);
});

// SPA fallback: serve index.html for any non-API, non-file route
if (Directory.Exists(wwwrootPath))
{
    app.MapFallbackToFile("index.html");
}

app.Run();

// --- Request DTOs ---
public record CreateCrawlJobRequest(string JobType);
public record CreateWatchlistRequest(
    string ServiceName,
    List<string>? Aliases = null,
    List<string>? SearchTerms = null,
    string? ResourceProvider = null);
