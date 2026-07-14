using System.Diagnostics;
using AzRadar.Shared.Configuration;
using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AzRadar.Shared.Services;

/// <summary>
/// GitHub Change Radar: scans watched GitHub repositories for new/changed documentation and
/// source files since the last run (bounded by a per-repo cutoff), analyzes each diff with the
/// LLM to judge platform-team relevance, cross-references Azure Updates findings, and stores the
/// results as DocInsights (source = "github"). Fully incremental and idempotent.
/// </summary>
public class GitHubCrawlJobHandler : IJobHandler
{
    private readonly IGitHubClient _github;
    private readonly ILlmAnalyzer _llmAnalyzer;
    private readonly ICosmosDbService _cosmosDb;
    private readonly GitHubSettings _settings;
    private readonly ILogger<GitHubCrawlJobHandler> _logger;

    private static readonly string[] RelevantExtensions = [".md", ".mdx"];

    // Front-matter / trivial metadata keys that, on their own, do not warrant an alert.
    private static readonly string[] MetadataPrefixes =
        ["ms.date", "author", "ms.author", "ms.reviewer", "ms.topic", "ms.custom", "ms.service", "ms.subservice", "titleSuffix", "manager", "ms.collection"];

    public string JobType => CrawlJobTypes.GitHubCrawl;

    public GitHubCrawlJobHandler(
        IGitHubClient github,
        ILlmAnalyzer llmAnalyzer,
        ICosmosDbService cosmosDb,
        IOptions<GitHubSettings> settings,
        ILogger<GitHubCrawlJobHandler> logger)
    {
        _github = github;
        _llmAnalyzer = llmAnalyzer;
        _cosmosDb = cosmosDb;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task HandleAsync(CrawlJob job, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting GitHub Change Radar job {JobId}", job.Id);

        var repos = (await _cosmosDb.GetRepoWatchlistAsync(cancellationToken))
            .Where(r => r.Enabled)
            .ToList();

        if (repos.Count == 0)
        {
            await DiagAsync(job.Id, "init", "", DiagnosticLevel.Warning,
                "No enabled repositories in the Repository Watchlist — nothing to scan.", cancellationToken);
            job.Result = new CrawlJobResult();
            return;
        }

        // Read optional read-only PAT (write-only config, stored in Cosmos app-config).
        var patConfig = await _cosmosDb.GetAppConfigAsync(AppConfigKeys.GitHubPat, cancellationToken);
        var token = patConfig?.Value;
        if (string.IsNullOrWhiteSpace(token))
        {
            await DiagAsync(job.Id, "init", "", DiagnosticLevel.Warning,
                "No GitHub PAT configured — running unauthenticated (60 requests/hour cap).", cancellationToken);
        }

        // Load Azure Updates findings once for cross-referencing.
        var feedItems = await _cosmosDb.GetFeedItemsAsync(CrawlJobTypes.AzureUpdates, 500, cancellationToken);

        int newItems = 0, skipped = 0, totalChecked = 0, filesThisRun = 0;

        foreach (var repo in repos)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (filesThisRun >= _settings.MaxFilesPerRun)
            {
                await DiagAsync(job.Id, "cap", repo.Label, DiagnosticLevel.Warning,
                    $"Reached per-run file cap ({_settings.MaxFilesPerRun}); remaining repos will be scanned next run.", cancellationToken);
                break;
            }

            var since = repo.LastScannedCommitDate ?? repo.CutoffDate;
            await DiagAsync(job.Id, "scan-repo", repo.Label, DiagnosticLevel.Info,
                $"Scanning {repo.Owner}/{repo.Repo} (branch={repo.Branch ?? "default"}, paths={FormatPaths(repo.PathFilters)}) since {since:u}.", cancellationToken);

            try
            {
                var (repoNew, repoSkipped, repoChecked, repoFiles, newestSha, newestDate) =
                    await ScanRepoAsync(job, repo, since, token, feedItems,
                        filesThisRun, cancellationToken);

                newItems += repoNew;
                skipped += repoSkipped;
                totalChecked += repoChecked;
                filesThisRun += repoFiles;

                // Advance cursor only on success.
                if (newestSha != null)
                {
                    repo.LastScannedCommitSha = newestSha;
                    repo.LastScannedCommitDate = newestDate;
                }
                repo.LastScanAt = DateTimeOffset.UtcNow;
                repo.LastScanStatus = "ok";
                repo.LastScanError = null;
                await _cosmosDb.UpdateRepoWatchAsync(repo, cancellationToken);

                await DiagAsync(job.Id, "scan-repo-done", repo.Label, DiagnosticLevel.Success,
                    $"{repo.Label}: {repoNew} new, {repoSkipped} skipped, {repoChecked} checked.", cancellationToken);
            }
            catch (GitHubRateLimitException ex)
            {
                repo.LastScanAt = DateTimeOffset.UtcNow;
                repo.LastScanStatus = "rate-limited";
                repo.LastScanError = ex.Message;
                await _cosmosDb.UpdateRepoWatchAsync(repo, cancellationToken);
                await DiagAsync(job.Id, "error", repo.Label, DiagnosticLevel.Error, ex.Message, cancellationToken);
            }
            catch (Exception ex)
            {
                repo.LastScanAt = DateTimeOffset.UtcNow;
                repo.LastScanStatus = "error";
                repo.LastScanError = ex.Message;
                await _cosmosDb.UpdateRepoWatchAsync(repo, cancellationToken);
                await DiagAsync(job.Id, "error", repo.Label, DiagnosticLevel.Error,
                    $"Scan failed: {ex.Message}", cancellationToken);
                _logger.LogError(ex, "GitHub scan failed for {Repo}", repo.RepoUrl);
            }

            job.Result = new CrawlJobResult { NewItems = newItems, SkippedItems = skipped, TotalChecked = totalChecked };
            var updated = await _cosmosDb.UpdateCrawlJobAsync(job, cancellationToken);
            job.ETag = updated.ETag;
        }

        job.Result = new CrawlJobResult { NewItems = newItems, SkippedItems = skipped, TotalChecked = totalChecked };
        _logger.LogInformation(
            "GitHub Change Radar complete: {New} new, {Skipped} skipped, {Total} checked",
            newItems, skipped, totalChecked);
    }

    private async Task<(int repoNew, int repoSkipped, int repoChecked, int repoFiles, string? newestSha, DateTimeOffset? newestDate)>
        ScanRepoAsync(
            CrawlJob job, RepoWatchItem repo, DateTimeOffset since, string? token,
            IReadOnlyList<FeedItem> feedItems, int filesAlreadyThisRun, CancellationToken ct)
    {
        // Collect commits across all path filters (dedup by SHA).
        List<string?> paths = repo.PathFilters.Count > 0
            ? repo.PathFilters.Cast<string?>().ToList()
            : [null];
        var commitMap = new Dictionary<string, GitHubCommitRef>();
        foreach (var path in paths)
        {
            var commits = await _github.ListCommitsAsync(
                repo.Owner, repo.Repo, repo.Branch, path, since, token,
                _settings.MaxCommitsPerRepo, ct);
            foreach (var c in commits)
                commitMap[c.Sha] = c;
        }

        // Process oldest-first so that, if we hit a cap, the cursor advances only past fully
        // processed commits and newer ones are picked up on the next run (no skips).
        var ordered = commitMap.Values
            .Where(c => c.AuthorDate > since || repo.LastScannedCommitDate == null)
            .OrderBy(c => c.AuthorDate)
            .Take(_settings.MaxCommitsPerRepo)
            .ToList();

        await DiagAsync(job.Id, "commits", repo.Label, DiagnosticLevel.Info,
            $"Found {ordered.Count} new commit(s) to inspect.", ct, resultCount: ordered.Count);

        int repoNew = 0, repoSkipped = 0, repoChecked = 0, repoFiles = 0;
        string? newestSha = null;
        DateTimeOffset? newestDate = null;

        foreach (var commitRef in ordered)
        {
            ct.ThrowIfCancellationRequested();

            if (filesAlreadyThisRun + repoFiles >= _settings.MaxFilesPerRun)
                break; // stop at commit boundary; cursor stays at last fully-processed commit

            var detail = await _github.GetCommitAsync(repo.Owner, repo.Repo, commitRef.Sha, token, ct);
            if (detail == null) continue;

            var relevantFiles = detail.Files
                .Where(f => IsRelevantFile(f.Filename, repo.PathFilters))
                .ToList();

            foreach (var file in relevantFiles)
            {
                repoChecked++;

                if (IsNoiseChange(file))
                {
                    repoSkipped++;
                    continue;
                }

                var docId = DocInsight.GenerateGitHubId(repo.Owner, repo.Repo, detail.Sha, file.Filename);
                var existing = await _cosmosDb.GetDocInsightAsync(docId, ct);
                if (existing != null)
                {
                    repoSkipped++;
                    continue; // already seen this exact commit+file
                }

                var diff = TruncateDiff(file);
                var sw = Stopwatch.StartNew();
                var change = new RepoChangeContext(
                    RepoLabel: repo.Label,
                    Owner: repo.Owner,
                    Repo: repo.Repo,
                    FilePath: file.Filename,
                    ChangeKind: file.Status,
                    CommitMessage: FirstLine(detail.Message),
                    CommitDate: detail.AuthorDate,
                    Diff: diff,
                    BlobUrl: file.BlobUrl);

                var analysis = await _llmAnalyzer.AnalyzeDocChangeAsync(change, ct);
                sw.Stop();

                var related = FindRelatedFeedItems(analysis, feedItems);

                var insight = new DocInsight
                {
                    Id = docId,
                    Source = "github",
                    ServiceName = analysis.AffectedServices.FirstOrDefault() ?? repo.Label,
                    DocUrl = file.BlobUrl,
                    Title = $"{FirstLine(detail.Message)} — {file.Filename}",
                    Snippet = analysis.BriefSummary,
                    ContentHash = DocInsight.HashContent(diff),
                    LlmAnalysis = analysis,
                    CrawlJobId = job.Id,
                    FirstSeenAt = DateTimeOffset.UtcNow,
                    LastAnalyzedAt = DateTimeOffset.UtcNow,
                    CommitSha = detail.Sha,
                    CommitDate = detail.AuthorDate,
                    ChangeKind = file.Status,
                    RepoUrl = repo.RepoUrl,
                    FilePath = file.Filename,
                    RelatedFeedItems = related,
                };

                await _cosmosDb.UpsertDocInsightAsync(insight, ct);
                repoNew++;
                repoFiles++;

                await DiagAsync(job.Id, "analyzed", insight.Title, analysis.RequiresAttention ? DiagnosticLevel.Success : DiagnosticLevel.Info,
                    $"{(analysis.RequiresAttention ? "ATTENTION" : "noted")}: {analysis.ChangeType}/{analysis.Severity}" +
                    (related.Count > 0 ? $" — {related.Count} related Azure Update(s)" : ""),
                    ct, durationMs: sw.ElapsedMilliseconds);
            }

            // Commit fully processed → safe to advance cursor to it.
            newestSha = detail.Sha;
            newestDate = detail.AuthorDate;
        }

        return (repoNew, repoSkipped, repoChecked, repoFiles, newestSha, newestDate);
    }

    private static bool IsRelevantFile(string filename, List<string> pathFilters)
    {
        if (!RelevantExtensions.Any(ext => filename.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            return false;
        if (pathFilters.Count == 0) return true;
        return pathFilters.Any(p =>
            filename.StartsWith(p.Trim().TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase) ||
            filename.Equals(p.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// True when a change carries no plausible lifecycle/breaking signal: no diff available, or the
    /// only added/removed lines are front-matter metadata or whitespace.
    /// </summary>
    private static bool IsNoiseChange(GitHubChangedFile file)
    {
        // Deletions are a potential retirement signal — never treat as noise.
        if (string.Equals(file.Status, "removed", StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.IsNullOrWhiteSpace(file.Patch))
            return true; // nothing to analyze

        var changedLines = file.Patch
            .Split('\n')
            .Where(l => (l.StartsWith('+') || l.StartsWith('-')) &&
                        !l.StartsWith("+++") && !l.StartsWith("---"))
            .Select(l => l[1..].Trim())
            .Where(l => l.Length > 0)
            .ToList();

        if (changedLines.Count == 0) return true; // whitespace-only

        // If every changed line is a metadata front-matter key, it's noise.
        return changedLines.All(IsMetadataLine);
    }

    private static bool IsMetadataLine(string line)
    {
        foreach (var prefix in MetadataPrefixes)
        {
            if (line.StartsWith(prefix + ":", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith(prefix + " :", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static List<RelatedFeedItem> FindRelatedFeedItems(
        LlmAnalysis analysis, IReadOnlyList<FeedItem> feedItems)
    {
        if (analysis.AffectedServices.Count == 0) return [];
        var services = analysis.AffectedServices
            .Select(s => s.Trim().ToLowerInvariant())
            .Where(s => s.Length > 0)
            .ToHashSet();
        if (services.Count == 0) return [];

        return feedItems
            .Where(f => f.LlmAnalysis != null &&
                        f.LlmAnalysis.AffectedServices.Any(s => services.Contains(s.Trim().ToLowerInvariant())))
            .OrderByDescending(f => f.PublishDate)
            .Take(5)
            .Select(f => new RelatedFeedItem
            {
                Id = f.Id,
                Title = f.Title,
                Link = f.Link,
                PublishDate = f.PublishDate,
            })
            .ToList();
    }

    private string TruncateDiff(GitHubChangedFile file)
    {
        var text = file.Patch;
        if (string.IsNullOrEmpty(text))
            text = $"(no diff available; file {file.Status}, +{file.Additions}/-{file.Deletions})";
        return text.Length <= _settings.DiffMaxChars ? text : text[.._settings.DiffMaxChars];
    }

    private static string FirstLine(string s)
    {
        var idx = s.IndexOf('\n');
        var line = idx >= 0 ? s[..idx] : s;
        return line.Trim();
    }

    private static string FormatPaths(List<string> paths)
        => paths.Count == 0 ? "(whole repo)" : string.Join(", ", paths);

    private async Task DiagAsync(
        string jobId, string step, string itemTitle, string level, string message,
        CancellationToken ct, int? resultCount = null, long? durationMs = null)
    {
        try
        {
            await _cosmosDb.StoreDiagnosticAsync(new JobDiagnosticEntry
            {
                JobId = jobId,
                Step = step,
                ItemTitle = itemTitle,
                Level = level,
                Message = message,
                ResultCount = resultCount,
                DurationMs = durationMs,
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to store diagnostic for job {JobId}", jobId);
        }
    }
}
