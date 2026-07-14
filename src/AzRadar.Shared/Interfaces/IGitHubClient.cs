namespace AzRadar.Shared.Interfaces;

/// <summary>Lightweight reference to a GitHub commit (from the commits list endpoint).</summary>
public record GitHubCommitRef(string Sha, string Message, DateTimeOffset AuthorDate, string HtmlUrl);

/// <summary>A file changed within a commit, including its unified diff.</summary>
public record GitHubChangedFile(
    string Filename,
    string Status,
    int Additions,
    int Deletions,
    string? Patch,
    string BlobUrl);

/// <summary>Full commit detail including the list of changed files.</summary>
public record GitHubCommitDetail(
    string Sha,
    string Message,
    DateTimeOffset AuthorDate,
    string HtmlUrl,
    IReadOnlyList<GitHubChangedFile> Files);

/// <summary>Thrown when the GitHub API reports the rate limit is exhausted.</summary>
public class GitHubRateLimitException(string message, DateTimeOffset? resetAt) : Exception(message)
{
    public DateTimeOffset? ResetAt { get; } = resetAt;
}

public interface IGitHubClient
{
    /// <summary>
    /// Lists commits on <paramref name="branch"/> touching <paramref name="path"/> that were
    /// authored on/after <paramref name="since"/>, newest first, up to <paramref name="maxCommits"/>.
    /// </summary>
    Task<IReadOnlyList<GitHubCommitRef>> ListCommitsAsync(
        string owner,
        string repo,
        string? branch,
        string? path,
        DateTimeOffset since,
        string? token,
        int maxCommits,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches a single commit with its changed files and diffs.</summary>
    Task<GitHubCommitDetail?> GetCommitAsync(
        string owner,
        string repo,
        string sha,
        string? token,
        CancellationToken cancellationToken = default);
}
