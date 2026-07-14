using System.Text.Json.Serialization;

namespace AzRadar.Shared.Models;

public class LlmAnalysis
{
    [JsonPropertyName("changeType")]
    public string ChangeType { get; set; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = string.Empty;

    [JsonPropertyName("affectedServices")]
    public List<string> AffectedServices { get; set; } = [];

    [JsonPropertyName("affectedResourceTypes")]
    public List<string> AffectedResourceTypes { get; set; } = [];

    [JsonPropertyName("actionRequired")]
    public string ActionRequired { get; set; } = string.Empty;

    [JsonPropertyName("deadline")]
    public string? Deadline { get; set; }

    [JsonPropertyName("effortEstimate")]
    public string EffortEstimate { get; set; } = string.Empty;

    [JsonPropertyName("migrationPath")]
    public string MigrationPath { get; set; } = string.Empty;

    [JsonPropertyName("microsoftDocLinks")]
    public List<string> MicrosoftDocLinks { get; set; } = [];

    [JsonPropertyName("aiConfidence")]
    public double AiConfidence { get; set; }

    [JsonPropertyName("briefSummary")]
    public string BriefSummary { get; set; } = string.Empty;

    /// <summary>
    /// Whether an Azure platform team must pay attention to this change
    /// (retirement, deprecation, breaking change, security, or major new capability).
    /// </summary>
    [JsonPropertyName("requiresAttention")]
    public bool RequiresAttention { get; set; }

    /// <summary>Short explanation of why this does (or does not) require platform-team attention.</summary>
    [JsonPropertyName("attentionJustification")]
    public string AttentionJustification { get; set; } = string.Empty;
}

public static class ChangeTypes
{
    public const string Retirement = "retirement";
    public const string Deprecation = "deprecation";
    public const string BreakingChange = "breaking-change";
    public const string SecurityAdvisory = "security-advisory";
    public const string NewFeature = "new-feature";
    public const string MigrationRequired = "migration-required";
    public const string Preview = "preview";
    public const string GeneralAvailability = "general-availability";
    public const string Update = "update";
}

public static class SeverityLevels
{
    public const string Critical = "critical";
    public const string High = "high";
    public const string Medium = "medium";
    public const string Low = "low";
    public const string Informational = "informational";
}
