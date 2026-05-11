using System.Text.Json.Serialization;

namespace AzRadar.Shared.Models;

public class CrawlJob
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("jobType")]
    public string JobType { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = CrawlJobStatus.Pending;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("startedAt")]
    public DateTimeOffset? StartedAt { get; set; }

    [JsonPropertyName("completedAt")]
    public DateTimeOffset? CompletedAt { get; set; }

    [JsonPropertyName("result")]
    public CrawlJobResult? Result { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("attemptCount")]
    public int AttemptCount { get; set; }

    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}

public class CrawlJobResult
{
    [JsonPropertyName("newItems")]
    public int NewItems { get; set; }

    [JsonPropertyName("totalChecked")]
    public int TotalChecked { get; set; }

    [JsonPropertyName("skippedItems")]
    public int SkippedItems { get; set; }
}

public static class CrawlJobStatus
{
    public const string Pending = "pending";
    public const string Processing = "processing";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

public static class CrawlJobTypes
{
    public const string AzureUpdates = "azure-updates";
}
