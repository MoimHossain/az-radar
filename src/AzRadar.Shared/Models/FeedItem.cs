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

    [JsonPropertyName("llmAnalysis")]
    public LlmAnalysis? LlmAnalysis { get; set; }

    [JsonPropertyName("firstSeenAt")]
    public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("crawlJobId")]
    public string CrawlJobId { get; set; } = string.Empty;
}
