using System.Text.Json.Serialization;

namespace AzRadar.Shared.Models;

public class ServiceHealthNotificationChannel
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = ServiceHealthChannelTypes.TeamsBot;

    [JsonPropertyName("tenantId")]
    public string TenantId { get; set; } = string.Empty;

    [JsonPropertyName("teamId")]
    public string TeamId { get; set; } = string.Empty;

    [JsonPropertyName("teamName")]
    public string TeamName { get; set; } = string.Empty;

    [JsonPropertyName("channelId")]
    public string ChannelId { get; set; } = string.Empty;

    [JsonPropertyName("channelName")]
    public string ChannelName { get; set; } = string.Empty;

    [JsonPropertyName("conversationReferenceId")]
    public string ConversationReferenceId { get; set; } = string.Empty;

    [JsonPropertyName("registrationStatus")]
    public string RegistrationStatus { get; set; } = ServiceHealthChannelRegistrationStatuses.Pending;

    [JsonPropertyName("subscribedEventTypes")]
    public List<string> SubscribedEventTypes { get; set; } = [];

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("lastRegisteredAt")]
    public DateTimeOffset? LastRegisteredAt { get; set; }
}

public static class ServiceHealthChannelTypes
{
    public const string TeamsBot = "teams-bot";
}

public static class ServiceHealthChannelRegistrationStatuses
{
    public const string Pending = "pending";
    public const string Registered = "registered";
    public const string Uninstalled = "uninstalled";
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
