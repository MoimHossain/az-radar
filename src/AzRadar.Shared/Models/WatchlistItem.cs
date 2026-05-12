using System.Text.Json.Serialization;

namespace AzRadar.Shared.Models;

public class WatchlistItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("serviceName")]
    public string ServiceName { get; set; } = string.Empty;

    [JsonPropertyName("aliases")]
    public List<string> Aliases { get; set; } = [];

    [JsonPropertyName("searchTerms")]
    public List<string> SearchTerms { get; set; } = [];

    [JsonPropertyName("resourceProvider")]
    public string ResourceProvider { get; set; } = string.Empty;

    [JsonPropertyName("addedAt")]
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}
