using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AzRadar.Shared.Configuration;
using AzRadar.Shared.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AzRadar.Shared.Services;

/// <summary>
/// Minimal GitHub REST client for the Change Radar crawler. Uses an optional read-only PAT
/// (passed per call) to raise the rate limit; works unauthenticated otherwise.
/// </summary>
public class GitHubClient : IGitHubClient
{
    private readonly HttpClient _http;
    private readonly GitHubSettings _settings;
    private readonly ILogger<GitHubClient> _logger;

    public GitHubClient(
        IOptions<GitHubSettings> settings,
        ILogger<GitHubClient> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        _http = new HttpClient
        {
            BaseAddress = new Uri(_settings.ApiBaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(_settings.RequestTimeoutSeconds),
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("az-radar-change-radar");
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public async Task<IReadOnlyList<GitHubCommitRef>> ListCommitsAsync(
        string owner, string repo, string? branch, string? path,
        DateTimeOffset since, string? token, int maxCommits,
        CancellationToken cancellationToken = default)
    {
        var commits = new List<GitHubCommitRef>();
        const int perPage = 100;
        var page = 1;

        while (commits.Count < maxCommits)
        {
            var url = $"repos/{owner}/{repo}/commits?per_page={perPage}&page={page}" +
                      $"&since={Uri.EscapeDataString(since.UtcDateTime.ToString("o"))}";
            if (!string.IsNullOrWhiteSpace(branch))
                url += $"&sha={Uri.EscapeDataString(branch)}";
            if (!string.IsNullOrWhiteSpace(path))
                url += $"&path={Uri.EscapeDataString(path)}";

            using var doc = await SendAsync(url, token, cancellationToken);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
                break;

            var count = 0;
            foreach (var item in root.EnumerateArray())
            {
                count++;
                var sha = item.GetProperty("sha").GetString() ?? "";
                var commit = item.GetProperty("commit");
                var message = commit.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
                var date = commit.GetProperty("author").GetProperty("date").GetDateTimeOffset();
                var htmlUrl = item.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
                commits.Add(new GitHubCommitRef(sha, message, date, htmlUrl));
                if (commits.Count >= maxCommits) break;
            }

            if (count < perPage) break; // last page
            page++;
        }

        return commits;
    }

    public async Task<GitHubCommitDetail?> GetCommitAsync(
        string owner, string repo, string sha, string? token,
        CancellationToken cancellationToken = default)
    {
        var url = $"repos/{owner}/{repo}/commits/{sha}";
        using var doc = await SendAsync(url, token, cancellationToken);
        var root = doc.RootElement;

        var commit = root.GetProperty("commit");
        var message = commit.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
        var date = commit.GetProperty("author").GetProperty("date").GetDateTimeOffset();
        var htmlUrl = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";

        var files = new List<GitHubChangedFile>();
        if (root.TryGetProperty("files", out var filesArr) && filesArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in filesArr.EnumerateArray())
            {
                files.Add(new GitHubChangedFile(
                    Filename: f.TryGetProperty("filename", out var fn) ? fn.GetString() ?? "" : "",
                    Status: f.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "",
                    Additions: f.TryGetProperty("additions", out var a) ? a.GetInt32() : 0,
                    Deletions: f.TryGetProperty("deletions", out var d) ? d.GetInt32() : 0,
                    Patch: f.TryGetProperty("patch", out var p) ? p.GetString() : null,
                    BlobUrl: f.TryGetProperty("blob_url", out var b) ? b.GetString() ?? "" : ""));
            }
        }

        return new GitHubCommitDetail(sha, message, date, htmlUrl, files);
    }

    private async Task<JsonDocument> SendAsync(string url, string? token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _http.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == (HttpStatusCode)429)
        {
            var remaining = GetHeader(response, "X-RateLimit-Remaining");
            if (remaining == "0" || response.StatusCode == (HttpStatusCode)429)
            {
                DateTimeOffset? resetAt = null;
                if (long.TryParse(GetHeader(response, "X-RateLimit-Reset"), out var epoch))
                    resetAt = DateTimeOffset.FromUnixTimeSeconds(epoch);
                throw new GitHubRateLimitException(
                    $"GitHub API rate limit exceeded (resets at {resetAt?.ToString("u") ?? "unknown"}). " +
                    (string.IsNullOrEmpty(token) ? "No PAT configured (60 req/hr cap)." : "Consider a higher-scope PAT."),
                    resetAt);
            }
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            var snippet = body.Length > 300 ? body[..300] : body;
            throw new HttpRequestException(
                $"GitHub API {(int)response.StatusCode} for {url}: {snippet}");
        }

        var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    private static string? GetHeader(HttpResponseMessage response, string name)
        => response.Headers.TryGetValues(name, out var vals) ? vals.FirstOrDefault() : null;
}
