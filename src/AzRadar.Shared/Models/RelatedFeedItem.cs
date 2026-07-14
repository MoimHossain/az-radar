using System.Text.Json.Serialization;

namespace AzRadar.Shared.Models;

/// <summary>
/// A lightweight reference to an Azure Updates feed item that is related to a doc insight,
/// used to cross-link GitHub-detected changes with already-tracked Azure Update announcements.
/// </summary>
public class RelatedFeedItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("link")]
    public string Link { get; set; } = string.Empty;

    [JsonPropertyName("publishDate")]
    public DateTimeOffset PublishDate { get; set; }
}
