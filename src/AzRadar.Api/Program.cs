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
    var feedItems = await db.GetFeedItemsAsync(limit: 500);
    var docInsights = await db.GetDocInsightsAsync(limit: 500);
    var watchlist = await db.GetWatchlistAsync();

    // Combine all items for unified analysis
    var allAnalyses = feedItems
        .Where(f => f.LlmAnalysis != null)
        .Select(f => new { f.Title, f.Link, f.PublishDate, Analysis = f.LlmAnalysis!, Source = "azure-updates" })
        .Concat(docInsights
            .Where(d => d.LlmAnalysis != null)
            .Select(d => new { d.Title, Link = d.DocUrl, PublishDate = d.LastAnalyzedAt, Analysis = d.LlmAnalysis!, Source = "ms-learn" }))
        .ToList();

    // Filter out analyses with deadlines overdue > 90 days
    var relevantAnalyses = allAnalyses.Where(a =>
    {
        if (string.IsNullOrEmpty(a.Analysis.Deadline)) return true;
        if (!DateTimeOffset.TryParse(a.Analysis.Deadline, out var dl)) return true;
        var days = (int)(dl - DateTimeOffset.UtcNow).TotalDays;
        return days > -90;
    }).ToList();

    // Change type distribution (using relevant only)
    var changeTypeBreakdown = relevantAnalyses
        .GroupBy(a => a.Analysis.ChangeType)
        .ToDictionary(g => g.Key, g => g.Count());

    // Severity distribution (using relevant only)
    var severityBreakdown = relevantAnalyses
        .GroupBy(a => a.Analysis.Severity)
        .ToDictionary(g => g.Key, g => g.Count());

    // Upcoming deadlines (sorted by urgency)
    var deadlines = allAnalyses
        .Where(a => !string.IsNullOrEmpty(a.Analysis.Deadline))
        .Select(a => new
        {
            a.Title,
            a.Link,
            a.Analysis.Deadline,
            a.Analysis.Severity,
            a.Analysis.ChangeType,
            a.Analysis.ActionRequired,
            affectedServices = a.Analysis.AffectedServices,
            a.Source,
            daysRemaining = (int?)null as int?,
        })
        .ToList()
        .Select(d =>
        {
            int? days = DateTimeOffset.TryParse(d.Deadline, out var dl)
                ? (int)(dl - DateTimeOffset.UtcNow).TotalDays
                : null;
            return new
            {
                d.Title, d.Link, d.Deadline, d.Severity, d.ChangeType,
                d.ActionRequired, d.affectedServices, d.Source,
                daysRemaining = days
            };
        })
        .Where(d => !d.daysRemaining.HasValue || d.daysRemaining.Value > -90)
        .OrderBy(d => d.daysRemaining ?? int.MaxValue)
        .ToList();

    // Top affected services (using relevant only)
    var topServices = relevantAnalyses
        .SelectMany(a => a.Analysis.AffectedServices.Select(s => new { Service = s, a.Analysis.ChangeType }))
        .GroupBy(x => x.Service)
        .Select(g => new
        {
            service = g.Key,
            total = g.Count(),
            retirements = g.Count(x => x.ChangeType == "retirement" || x.ChangeType == "deprecation"),
        })
        .OrderByDescending(x => x.retirements)
        .ThenByDescending(x => x.total)
        .Take(15)
        .ToList();

    // Source breakdown
    var sourceBreakdown = new
    {
        azureUpdates = feedItems.Count,
        msLearnDocs = docInsights.Count,
    };

    var stats = new
    {
        // Summary counters
        totalItems = relevantAnalyses.Count,
        totalRetirements = relevantAnalyses.Count(a =>
            a.Analysis.ChangeType == "retirement" || a.Analysis.ChangeType == "deprecation"),
        totalGA = relevantAnalyses.Count(a => a.Analysis.ChangeType == "general-availability"),
        totalPreviews = relevantAnalyses.Count(a => a.Analysis.ChangeType == "preview"),
        totalNewFeatures = relevantAnalyses.Count(a => a.Analysis.ChangeType == "new-feature"),
        urgentDeadlines = deadlines.Count(d => d.daysRemaining.HasValue && d.daysRemaining.Value < 90),
        watchedServices = watchlist.Count,

        // Jobs
        totalJobs = jobs.Count,
        completedJobs = jobs.Count(j => j.Status == CrawlJobStatus.Completed),
        latestCrawl = jobs.OrderByDescending(j => j.CreatedAt).FirstOrDefault()?.CreatedAt,

        // Breakdowns
        changeTypeBreakdown,
        severityBreakdown,
        sourceBreakdown,

        // Deadline timeline
        deadlines,

        // Top services
        topAffectedServices = topServices,
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
