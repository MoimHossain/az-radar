using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace AzRadar.Shared.Models;

/// <summary>
/// Summary of blast radius for a single retirement/deprecation item.
/// </summary>
public class BlastRadiusSummary
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("sourceItemId")]
    public string SourceItemId { get; set; } = string.Empty;

    [JsonPropertyName("sourceTitle")]
    public string SourceTitle { get; set; } = string.Empty;

    [JsonPropertyName("sourceType")]
    public string SourceType { get; set; } = string.Empty;

    [JsonPropertyName("changeType")]
    public string ChangeType { get; set; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = string.Empty;

    [JsonPropertyName("deadline")]
    public string? Deadline { get; set; }

    [JsonPropertyName("resourceType")]
    public string ResourceType { get; set; } = string.Empty;

    [JsonPropertyName("matchConfidence")]
    public string MatchConfidence { get; set; } = "potential";

    [JsonPropertyName("totalResources")]
    public int TotalResources { get; set; }

    [JsonPropertyName("subscriptionCount")]
    public int SubscriptionCount { get; set; }

    [JsonPropertyName("regionBreakdown")]
    public Dictionary<string, int> RegionBreakdown { get; set; } = new();

    [JsonPropertyName("subscriptionBreakdown")]
    public Dictionary<string, int> SubscriptionBreakdown { get; set; } = new();

    [JsonPropertyName("topResources")]
    public List<AffectedResource> TopResources { get; set; } = [];

    [JsonPropertyName("scanJobId")]
    public string ScanJobId { get; set; } = string.Empty;

    [JsonPropertyName("scannedAt")]
    public DateTimeOffset ScannedAt { get; set; } = DateTimeOffset.UtcNow;

    public static string GenerateId(string sourceItemId, string resourceType)
    {
        var input = $"blast:{sourceItemId}:{resourceType.ToLowerInvariant()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant()[..32];
    }
}

public class AffectedResource
{
    [JsonPropertyName("subscriptionId")]
    public string SubscriptionId { get; set; } = string.Empty;

    [JsonPropertyName("resourceGroup")]
    public string ResourceGroup { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("location")]
    public string Location { get; set; } = string.Empty;

    [JsonPropertyName("sku")]
    public string Sku { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public Dictionary<string, string> Tags { get; set; } = new();
}
