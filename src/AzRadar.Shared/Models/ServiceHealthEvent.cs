using System.Text.Json.Serialization;

namespace AzRadar.Shared.Models;

public class ServiceHealthEvent
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("eventDataId")]
    public string EventDataId { get; set; } = string.Empty;

    [JsonPropertyName("trackingId")]
    public string TrackingId { get; set; } = string.Empty;

    [JsonPropertyName("correlationId")]
    public string CorrelationId { get; set; } = string.Empty;

    [JsonPropertyName("subscriptionId")]
    public string SubscriptionId { get; set; } = string.Empty;

    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = ServiceHealthEventTypes.HealthAdvisory;

    [JsonPropertyName("incidentType")]
    public string IncidentType { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("level")]
    public string Level { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("service")]
    public string Service { get; set; } = string.Empty;

    [JsonPropertyName("region")]
    public string Region { get; set; } = string.Empty;

    [JsonPropertyName("operationName")]
    public string OperationName { get; set; } = string.Empty;

    [JsonPropertyName("eventTimestamp")]
    public DateTimeOffset EventTimestamp { get; set; }

    [JsonPropertyName("receivedAt")]
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("isSynthetic")]
    public bool IsSynthetic { get; set; }

    [JsonPropertyName("contentHash")]
    public string ContentHash { get; set; } = string.Empty;

    [JsonPropertyName("rawPayload")]
    public string RawPayload { get; set; } = string.Empty;

    [JsonPropertyName("llmAnalysis")]
    public LlmAnalysis? LlmAnalysis { get; set; }

    [JsonPropertyName("routingStatus")]
    public string RoutingStatus { get; set; } = ServiceHealthRoutingStatuses.Pending;

    [JsonPropertyName("matchingChannelIds")]
    public List<string> MatchingChannelIds { get; set; } = [];
}

public static class ServiceHealthRoutingStatuses
{
    public const string Pending = "pending";
    public const string ReadyForDispatch = "ready-for-dispatch";
    public const string NoRoute = "no-route";
}

public class ServiceHealthDeliveryIntent
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("eventId")]
    public string EventId { get; set; } = string.Empty;

    [JsonPropertyName("channelId")]
    public string ChannelId { get; set; } = string.Empty;

    [JsonPropertyName("channelDisplayName")]
    public string ChannelDisplayName { get; set; } = string.Empty;

    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = ServiceHealthDeliveryIntentStatuses.Pending;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class ServiceHealthDeliveryIntentStatuses
{
    public const string Pending = "pending";
}

public class ServiceHealthEventCheckpoint
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("partitionId")]
    public string PartitionId { get; set; } = string.Empty;

    [JsonPropertyName("offset")]
    public string Offset { get; set; } = string.Empty;

    [JsonPropertyName("sequenceNumber")]
    public long SequenceNumber { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class ServiceHealthQuarantineRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("partitionId")]
    public string PartitionId { get; set; } = string.Empty;

    [JsonPropertyName("offset")]
    public string Offset { get; set; } = string.Empty;

    [JsonPropertyName("sequenceNumber")]
    public long SequenceNumber { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("payloadHash")]
    public string PayloadHash { get; set; } = string.Empty;

    [JsonPropertyName("rawPayload")]
    public string RawPayload { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
