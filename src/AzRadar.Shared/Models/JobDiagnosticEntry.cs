using System.Text.Json.Serialization;

namespace AzRadar.Shared.Models;

/// <summary>
/// Diagnostic log entry for a crawl job — captures each step, LLM call, and ARG query attempt.
/// </summary>
public class JobDiagnosticEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("jobId")]
    public string JobId { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("step")]
    public string Step { get; set; } = string.Empty;

    [JsonPropertyName("itemTitle")]
    public string ItemTitle { get; set; } = string.Empty;

    [JsonPropertyName("level")]
    public string Level { get; set; } = "info";

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("llmQuery")]
    public string? LlmQuery { get; set; }

    [JsonPropertyName("argError")]
    public string? ArgError { get; set; }

    [JsonPropertyName("attempt")]
    public int? Attempt { get; set; }

    [JsonPropertyName("resultCount")]
    public int? ResultCount { get; set; }

    [JsonPropertyName("durationMs")]
    public long? DurationMs { get; set; }
}

public static class DiagnosticLevel
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Error = "error";
    public const string Success = "success";
}
