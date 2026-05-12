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
    /// Generates a hash of the document content for change detection.
    /// </summary>
    public static string HashContent(string content)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant()[..32];
    }
}
