using System.Text.Json.Serialization;

namespace AzRadar.Shared.Models;

public class FeedItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("link")]
    public string Link { get; set; } = string.Empty;

    [JsonPropertyName("publishDate")]
    public DateTimeOffset PublishDate { get; set; }

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("categories")]
    public List<string> Categories { get; set; } = [];

    [JsonPropertyName("rawContent")]
    public string RawContent { get; set; } = string.Empty;

    [JsonPropertyName("rawContentGzip")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RawContentGzip { get; set; }

    [JsonPropertyName("llmAnalysis")]
    public LlmAnalysis? LlmAnalysis { get; set; }

    [JsonPropertyName("firstSeenAt")]
    public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("crawlJobId")]
    public string CrawlJobId { get; set; } = string.Empty;

    [JsonPropertyName("sourceContentHash")]
    public string? SourceContentHash { get; set; }

    [JsonPropertyName("llmAnalysisSkipped")]
    public bool LlmAnalysisSkipped { get; set; }

    [JsonPropertyName("sourceModifiedAt")]
    public DateTimeOffset? SourceModifiedAt { get; set; }

    /// <summary>Azure Updates catalog products, kept so watchlist decisions can be audited.</summary>
    [JsonPropertyName("products")]
    public List<string> Products { get; set; } = [];

    /// <summary>Normalized lifecycle deadline (yyyy-MM-dd) used for calendar range queries.</summary>
    [JsonPropertyName("lifecycleDeadline")]
    public string? LifecycleDeadline { get; set; }

    /// <summary>llm | extracted</summary>
    [JsonPropertyName("deadlineSource")]
    public string? DeadlineSource { get; set; }

    [JsonPropertyName("deadlineResolverVersion")]
    public int? DeadlineResolverVersion { get; set; }

    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }

    internal FeedItem CopyForStorage() => (FeedItem)MemberwiseClone();
}
