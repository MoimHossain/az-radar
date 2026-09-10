using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AzRadar.Shared.Models;

namespace AzRadar.Shared.Services;

public static class ServiceHealthEventNormalizer
{
    public static IReadOnlyList<ServiceHealthEvent> Normalize(
        BinaryData body,
        DateTimeOffset enqueuedTime)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var records = root.ValueKind switch
        {
            JsonValueKind.Array => root.EnumerateArray().Select(item => item.Clone()).ToList(),
            JsonValueKind.Object when root.TryGetProperty("records", out var recordsElement) &&
                                      recordsElement.ValueKind == JsonValueKind.Array =>
                recordsElement.EnumerateArray().Select(item => item.Clone()).ToList(),
            JsonValueKind.Object => [root.Clone()],
            _ => throw new JsonException("Service Health payload must be a JSON object or array.")
        };

        return records.Select(record => NormalizeRecord(record, enqueuedTime)).ToList();
    }

    public static string ComputeHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static ServiceHealthEvent NormalizeRecord(JsonElement record, DateTimeOffset enqueuedTime)
    {
        var category = ReadString(record, "category");
        if (!string.Equals(category, "ServiceHealth", StringComparison.OrdinalIgnoreCase))
            throw new JsonException($"Expected Activity Log category ServiceHealth, received '{category}'.");

        var properties = ReadProperties(record);
        var rawPayload = record.GetRawText();
        var contentHash = ComputeHash(rawPayload);
        var eventDataId = FirstNonEmpty(
            ReadString(record, "eventDataId"),
            ReadString(record, "id"),
            contentHash);
        var resourceId = ReadString(record, "resourceId");
        var subscriptionId = FirstNonEmpty(
            ReadString(record, "subscriptionId"),
            ExtractSubscriptionId(resourceId));
        var operationName = ReadString(record, "operationName");
        var incidentType = ReadString(properties, "incidentType");
        var trackingId = FirstNonEmpty(
            ReadString(properties, "trackingId"),
            ReadString(record, "correlationId"),
            eventDataId);
        var eventType = ClassifyEventType(incidentType, operationName, properties);

        return new ServiceHealthEvent
        {
            Id = ComputeHash($"{subscriptionId}|{eventDataId}|{contentHash}"),
            EventDataId = eventDataId,
            TrackingId = trackingId,
            CorrelationId = ReadString(record, "correlationId"),
            SubscriptionId = subscriptionId,
            EventType = eventType,
            IncidentType = incidentType,
            Status = FirstNonEmpty(ReadString(record, "resultType"), ReadString(record, "status")),
            Level = ReadString(record, "level"),
            Title = FirstNonEmpty(
                ReadString(properties, "title"),
                ReadString(properties, "communication"),
                "Azure Service Health notification"),
            Summary = FirstNonEmpty(
                ReadString(properties, "summary"),
                ReadString(properties, "description"),
                ReadString(properties, "communication")),
            Service = ReadString(properties, "service"),
            Region = FirstNonEmpty(
                ReadString(properties, "region"),
                ReadString(properties, "impactedRegion")),
            OperationName = operationName,
            EventTimestamp = ReadDateTime(record, "eventTimestamp") ??
                             ReadDateTime(record, "time") ??
                             enqueuedTime,
            ReceivedAt = DateTimeOffset.UtcNow,
            IsSynthetic = ReadBoolean(properties, "synthetic") ||
                          ReadBoolean(record, "synthetic"),
            ContentHash = contentHash,
            RawPayload = rawPayload
        };
    }

    private static JsonElement ReadProperties(JsonElement record)
    {
        if (!record.TryGetProperty("properties", out var properties))
            return default;
        if (properties.ValueKind == JsonValueKind.Object)
            return properties;
        if (properties.ValueKind == JsonValueKind.String)
        {
            var value = properties.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                using var document = JsonDocument.Parse(value);
                return document.RootElement.Clone();
            }
        }
        return default;
    }

    private static string ClassifyEventType(
        string incidentType,
        string operationName,
        JsonElement properties)
    {
        var signal = $"{incidentType} {operationName} {ReadString(properties, "eventType")}";
        if (signal.Contains("security", StringComparison.OrdinalIgnoreCase))
            return ServiceHealthEventTypes.SecurityAdvisory;
        if (signal.Contains("maintenance", StringComparison.OrdinalIgnoreCase))
            return ServiceHealthEventTypes.PlannedMaintenance;
        if (signal.Contains("incident", StringComparison.OrdinalIgnoreCase) ||
            signal.Contains("serviceissue", StringComparison.OrdinalIgnoreCase))
            return ServiceHealthEventTypes.ServiceIssue;
        return ServiceHealthEventTypes.HealthAdvisory;
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var value))
            return string.Empty;

        if (value.ValueKind == JsonValueKind.String)
            return value.GetString() ?? string.Empty;
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty("value", out var nestedValue))
                return nestedValue.GetString() ?? string.Empty;
            if (value.TryGetProperty("localizedValue", out var localizedValue))
                return localizedValue.GetString() ?? string.Empty;
        }
        return value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? string.Empty
            : value.ToString();
    }

    private static bool ReadBoolean(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => bool.TryParse(value.GetString(), out var result) && result,
            _ => false
        };

    private static DateTimeOffset? ReadDateTime(JsonElement element, string propertyName)
    {
        var value = ReadString(element, propertyName);
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string ExtractSubscriptionId(string resourceId)
    {
        var segments = resourceId.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (string.Equals(segments[i], "subscriptions", StringComparison.OrdinalIgnoreCase))
                return segments[i + 1];
        }
        return string.Empty;
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}
