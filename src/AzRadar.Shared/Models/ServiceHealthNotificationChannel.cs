using System.Text.Json.Serialization;

namespace AzRadar.Shared.Models;

public class ServiceHealthNotificationChannel
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = ServiceHealthChannelTypes.TeamsWorkflow;

    [JsonPropertyName("secretUri")]
    public string SecretUri { get; set; } = string.Empty;

    [JsonPropertyName("subscribedEventTypes")]
    public List<string> SubscribedEventTypes { get; set; } = [];

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class ServiceHealthChannelTypes
{
    public const string TeamsWorkflow = "teams-workflow";
}

public static class ServiceHealthEventTypes
{
    public const string ServiceIssue = "ServiceIssue";
    public const string PlannedMaintenance = "PlannedMaintenance";
    public const string HealthAdvisory = "HealthAdvisory";
    public const string SecurityAdvisory = "SecurityAdvisory";

    public static readonly IReadOnlySet<string> Supported = new HashSet<string>(
        [ServiceIssue, PlannedMaintenance, HealthAdvisory, SecurityAdvisory],
        StringComparer.OrdinalIgnoreCase);
}
