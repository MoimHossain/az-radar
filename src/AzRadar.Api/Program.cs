using System.Text.Json;
using AzRadar.Shared;
using AzRadar.Shared.Configuration;
using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Models;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Services.Configure<CosmosDbSettings>(builder.Configuration.GetSection(CosmosDbSettings.SectionName));
builder.Services.Configure<OpenAiSettings>(builder.Configuration.GetSection(OpenAiSettings.SectionName));
builder.Services.Configure<GitHubSettings>(builder.Configuration.GetSection(GitHubSettings.SectionName));

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

// --- LLM connectivity check ---
// Performs a minimal chat completion so a deployment can verify the app can
// reach the configured Azure OpenAI endpoint (e.g. via a private endpoint)
// and authenticate with its managed identity. Returns 503 on failure.
app.MapGet("/api/health/llm", async (OpenAI.Chat.ChatClient chat, IOptions<OpenAiSettings> openAiOptions) =>
{
    var settings = openAiOptions.Value;
    var sw = System.Diagnostics.Stopwatch.StartNew();
    try
    {
        var messages = new List<OpenAI.Chat.ChatMessage>
        {
            new OpenAI.Chat.UserChatMessage("Reply with the single word: OK")
        };
        var response = await chat.CompleteChatAsync(messages);
        sw.Stop();
        var reply = response.Value.Content.Count > 0 ? response.Value.Content[0].Text : string.Empty;
        return Results.Ok(new
        {
            status = "healthy",
            llm = "reachable",
            endpoint = settings.Endpoint,
            deployment = settings.DeploymentName,
            reply,
            latencyMs = sw.ElapsedMilliseconds,
            timestamp = DateTimeOffset.UtcNow
        });
    }
    catch (Exception ex)
    {
        sw.Stop();
        return Results.Json(new
        {
            status = "unhealthy",
            llm = "unreachable",
            endpoint = settings.Endpoint,
            deployment = settings.DeploymentName,
            error = ex.Message,
            latencyMs = sw.ElapsedMilliseconds,
            timestamp = DateTimeOffset.UtcNow
        }, statusCode: 503);
    }
});

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
    var blastRadius = await db.GetBlastRadiusSummariesAsync(200);

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

        // Blast radius
        blastRadiusTotalResources = blastRadius.Sum(b => b.TotalResources),
        blastRadiusItemsScanned = blastRadius.Select(b => b.SourceItemId).Distinct().Count(),
        blastRadiusSubscriptions = blastRadius
            .SelectMany(b => b.SubscriptionBreakdown.Keys)
            .Distinct().Count(),
        blastRadiusLastScan = blastRadius
            .OrderByDescending(b => b.ScannedAt)
            .FirstOrDefault()?.ScannedAt,
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

// --- Repository watchlist endpoints (GitHub Change Radar) ---
app.MapGet("/api/repo-watchlist", async (ICosmosDbService db) =>
{
    var items = await db.GetRepoWatchlistAsync();
    return Results.Ok(items);
});

app.MapPost("/api/repo-watchlist", async (CreateRepoWatchRequest request, ICosmosDbService db) =>
{
    if (!TryParseGitHubRepo(request.RepoUrl, out var owner, out var repo))
        return Results.BadRequest(new { error = "Invalid GitHub repository URL. Expected https://github.com/{owner}/{repo}." });

    var item = new RepoWatchItem
    {
        RepoUrl = $"https://github.com/{owner}/{repo}",
        Owner = owner,
        Repo = repo,
        Branch = string.IsNullOrWhiteSpace(request.Branch) ? null : request.Branch!.Trim(),
        PathFilters = (request.PathFilters ?? [])
            .Select(p => p.Trim().Trim('/'))
            .Where(p => p.Length > 0)
            .ToList(),
        Label = string.IsNullOrWhiteSpace(request.Label) ? $"{owner}/{repo}" : request.Label!.Trim(),
        CutoffDate = request.CutoffDate ?? DateTimeOffset.UtcNow.AddDays(-30),
        Enabled = request.Enabled ?? true,
    };
    var created = await db.CreateRepoWatchAsync(item);
    return Results.Created($"/api/repo-watchlist/{created.Id}", created);
});

app.MapDelete("/api/repo-watchlist/{id}", async (string id, ICosmosDbService db) =>
{
    var deleted = await db.DeleteRepoWatchAsync(id);
    return deleted ? Results.NoContent() : Results.NotFound();
});

app.MapPatch("/api/repo-watchlist/{id}", async (string id, UpdateRepoWatchRequest request, ICosmosDbService db) =>
{
    var item = await db.GetRepoWatchAsync(id);
    if (item is null) return Results.NotFound();

    if (request.Enabled.HasValue) item.Enabled = request.Enabled.Value;
    if (request.CutoffDate.HasValue) item.CutoffDate = request.CutoffDate.Value;
    if (request.PathFilters != null)
        item.PathFilters = request.PathFilters.Select(p => p.Trim().Trim('/')).Where(p => p.Length > 0).ToList();
    if (request.Label != null) item.Label = request.Label.Trim();

    var updated = await db.UpdateRepoWatchAsync(item);
    return Results.Ok(updated);
});

// --- DocInsight endpoints ---
app.MapGet("/api/doc-insights", async (ICosmosDbService db, string? serviceName, int? limit, string? source) =>
{
    var items = await db.GetDocInsightsAsync(serviceName, limit ?? 50, source);
    return Results.Ok(items);
});

app.MapGet("/api/doc-insights/{id}", async (string id, ICosmosDbService db) =>
{
    var item = await db.GetDocInsightAsync(id);
    return item is null ? Results.NotFound() : Results.Ok(item);
});

// --- Calendar endpoint ---
app.MapGet("/api/calendar", async (ICosmosDbService db) =>
{
    var feedItems = await db.GetFeedItemsAsync(limit: 500);
    var docInsights = await db.GetDocInsightsAsync(limit: 500);
    var cutoff = DateTimeOffset.UtcNow.AddDays(-90);

    var calendarItems = new List<object>();

    foreach (var fi in feedItems)
    {
        if (fi.LlmAnalysis == null) continue;
        var deadline = fi.LlmAnalysis.Deadline;
        if (string.IsNullOrEmpty(deadline)) continue;
        if (DateTimeOffset.TryParse(deadline, out var dl) && dl < cutoff) continue;

        calendarItems.Add(new
        {
            id = fi.Id,
            title = fi.Title,
            link = fi.Link,
            deadline,
            changeType = fi.LlmAnalysis.ChangeType,
            severity = fi.LlmAnalysis.Severity,
            affectedServices = fi.LlmAnalysis.AffectedServices,
            actionRequired = fi.LlmAnalysis.ActionRequired,
            source = "azure-updates",
            briefSummary = fi.LlmAnalysis.BriefSummary,
        });
    }

    foreach (var di in docInsights)
    {
        if (di.LlmAnalysis == null) continue;
        var deadline = di.LlmAnalysis.Deadline;
        if (string.IsNullOrEmpty(deadline)) continue;
        if (DateTimeOffset.TryParse(deadline, out var dl) && dl < cutoff) continue;

        calendarItems.Add(new
        {
            id = di.Id,
            title = di.Title,
            link = di.DocUrl,
            deadline,
            changeType = di.LlmAnalysis.ChangeType,
            severity = di.LlmAnalysis.Severity,
            affectedServices = di.LlmAnalysis.AffectedServices,
            actionRequired = di.LlmAnalysis.ActionRequired,
            source = "ms-learn",
            briefSummary = di.LlmAnalysis.BriefSummary,
        });
    }

    return Results.Ok(calendarItems.OrderBy(i => ((dynamic)i).deadline));
});

// --- AppConfig endpoints ---
app.MapGet("/api/config/{key}", async (string key, ICosmosDbService db) =>
{
    var config = await db.GetAppConfigAsync(key);
    if (config is null) return Results.NotFound();

    // Secrets (e.g. the GitHub PAT) are write-only: never return the raw value.
    if (IsSecretConfigKey(key))
    {
        var v = config.Value ?? string.Empty;
        var masked = v.Length == 0 ? "" : $"••••{(v.Length >= 4 ? v[^4..] : v)}";
        return Results.Ok(new AppConfig
        {
            Id = config.Id,
            Value = masked,
            Description = config.Description,
            UpdatedAt = config.UpdatedAt,
        });
    }
    return Results.Ok(config);
});

// --- Diagnostics endpoint ---
app.MapGet("/api/crawl-jobs/{id}/diagnostics", async (string id, ICosmosDbService db) =>
{
    var entries = await db.GetDiagnosticsForJobAsync(id);
    return Results.Ok(entries);
});

app.MapPut("/api/config/{key}", async (string key, UpdateConfigRequest request, ICosmosDbService db) =>
{
    var config = new AppConfig
    {
        Id = key,
        Value = request.Value,
        Description = request.Description ?? "",
    };
    await db.UpsertAppConfigAsync(config);
    return Results.Ok(config);
});

// --- BlastRadius endpoints ---
app.MapGet("/api/blast-radius", async (ICosmosDbService db, int? limit) =>
{
    var items = await db.GetBlastRadiusSummariesAsync(limit ?? 100);
    return Results.Ok(items);
});

app.MapGet("/api/blast-radius/{id}", async (string id, ICosmosDbService db) =>
{
    var item = await db.GetBlastRadiusSummaryAsync(id);
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
public record CreateRepoWatchRequest(
    string RepoUrl,
    string? Branch = null,
    List<string>? PathFilters = null,
    string? Label = null,
    DateTimeOffset? CutoffDate = null,
    bool? Enabled = null);
public record UpdateRepoWatchRequest(
    bool? Enabled = null,
    DateTimeOffset? CutoffDate = null,
    List<string>? PathFilters = null,
    string? Label = null);
public record UpdateConfigRequest(string Value, string? Description = null);

public partial class Program
{
    /// <summary>Config keys whose values must never be returned in clear text by the API.</summary>
    private static bool IsSecretConfigKey(string key)
        => string.Equals(key, AppConfigKeys.GitHubPat, StringComparison.OrdinalIgnoreCase);

    /// <summary>Parses https://github.com/{owner}/{repo}[/...] (or git@/owner/repo) into owner + repo.</summary>
    private static bool TryParseGitHubRepo(string? url, out string owner, out string repo)
    {
        owner = ""; repo = "";
        if (string.IsNullOrWhiteSpace(url)) return false;
        var s = url.Trim();
        s = s.Replace("git@github.com:", "https://github.com/", StringComparison.OrdinalIgnoreCase);
        if (!s.Contains("github.com", StringComparison.OrdinalIgnoreCase)) return false;

        var afterHost = s[(s.IndexOf("github.com", StringComparison.OrdinalIgnoreCase) + "github.com".Length)..]
            .TrimStart('/', ':');
        var parts = afterHost.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;
        owner = parts[0];
        repo = parts[1].Replace(".git", "", StringComparison.OrdinalIgnoreCase);
        return owner.Length > 0 && repo.Length > 0;
    }
}
