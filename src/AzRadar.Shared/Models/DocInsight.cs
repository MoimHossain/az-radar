using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace AzRadar.Shared.Models;

public class DocInsight
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = "ms-learn";

    [JsonPropertyName("serviceName")]
    public string ServiceName { get; set; } = string.Empty;

    [JsonPropertyName("docUrl")]
    public string DocUrl { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("snippet")]
    public string Snippet { get; set; } = string.Empty;

    [JsonPropertyName("contentHash")]
    public string ContentHash { get; set; } = string.Empty;

    [JsonPropertyName("llmAnalysis")]
    public LlmAnalysis? LlmAnalysis { get; set; }

    [JsonPropertyName("firstSeenAt")]
    public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("lastAnalyzedAt")]
    public DateTimeOffset LastAnalyzedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("crawlJobId")]
    public string CrawlJobId { get; set; } = string.Empty;

    // --- GitHub Change Radar fields (source = "github") ---

    [JsonPropertyName("commitSha")]
    public string? CommitSha { get; set; }

    /// <summary>Real authored date of the commit that produced this change.</summary>
    [JsonPropertyName("commitDate")]
    public DateTimeOffset? CommitDate { get; set; }

    /// <summary>added | modified | removed | renamed</summary>
    [JsonPropertyName("changeKind")]
    public string? ChangeKind { get; set; }

    [JsonPropertyName("repoUrl")]
    public string? RepoUrl { get; set; }

    [JsonPropertyName("filePath")]
    public string? FilePath { get; set; }

    /// <summary>Related Azure Updates findings cross-referenced at analysis time.</summary>
    [JsonPropertyName("relatedFeedItems")]
    public List<RelatedFeedItem> RelatedFeedItems { get; set; } = [];

    /// <summary>
    /// Generates a deterministic ID from source + normalized doc URL.
    /// </summary>
    public static string GenerateId(string docUrl)
    {
        var input = $"ms-learn:{docUrl.Trim().ToLowerInvariant()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant()[..32];
    }

    /// <summary>
    /// Deterministic "seen" ID for a GitHub file change at a specific commit.
    /// Makes cursor overlap and re-runs idempotent (same commit+file =&gt; same id).
    /// </summary>
    public static string GenerateGitHubId(string owner, string repo, string commitSha, string filePath)
    {
        var input = $"github:{owner}/{repo}:{commitSha}:{filePath}".ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant()[..32];
    }

    /// <summary>
    /// Generates a hash of the document content for change detection.
    /// </summary>
    public static string HashContent(string content)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant()[..32];
    }
}
