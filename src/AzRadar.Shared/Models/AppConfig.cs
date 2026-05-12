using System.Text.Json.Serialization;

namespace AzRadar.Shared.Models;

/// <summary>
/// Generic key-value app configuration stored in Cosmos DB.
/// </summary>
public class AppConfig
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class AppConfigKeys
{
    /// <summary>
    /// UAMI Client ID for Azure Resource Graph access (blast radius scans).
    /// </summary>
    public const string BlastRadiusUamiClientId = "blast-radius-uami-client-id";
}
