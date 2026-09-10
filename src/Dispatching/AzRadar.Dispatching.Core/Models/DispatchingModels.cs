using System.Text.Json.Serialization;

namespace AzRadar.Dispatching.Core.Models;

public sealed class TeamsDeliveryEnvelope
{
    [JsonPropertyName("deliveryIntentId")]
    public string DeliveryIntentId { get; set; } = string.Empty;

    [JsonPropertyName("eventId")]
    public string EventId { get; set; } = string.Empty;

    [JsonPropertyName("channelId")]
    public string ChannelId { get; set; } = string.Empty;

    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TeamsConversationReferenceDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

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

    [JsonPropertyName("conversationJson")]
    public string ConversationJson { get; set; } = string.Empty;

    [JsonPropertyName("active")]
    public bool Active { get; set; } = true;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TeamsDeliveryAttempt
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("deliveryIntentId")]
    public string DeliveryIntentId { get; set; } = string.Empty;

    [JsonPropertyName("attemptNumber")]
    public int AttemptNumber { get; set; }

    [JsonPropertyName("startedAt")]
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("completedAt")]
    public DateTimeOffset? CompletedAt { get; set; }

    [JsonPropertyName("succeeded")]
    public bool Succeeded { get; set; }

    [JsonPropertyName("teamsActivityId")]
    public string? TeamsActivityId { get; set; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
}
