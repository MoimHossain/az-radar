using System.Text.Json.Serialization;

namespace AzRadar.Shared.Models;

/// <summary>
/// A GitHub repository the platform team wants to watch for documentation/source changes.
/// The GitHub Change Radar job scans commits under the configured paths and analyzes diffs.
/// </summary>
public class RepoWatchItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Full repo URL as entered by the user, e.g. https://github.com/MicrosoftDocs/azure-aks-docs</summary>
    [JsonPropertyName("repoUrl")]
    public string RepoUrl { get; set; } = string.Empty;

    [JsonPropertyName("owner")]
    public string Owner { get; set; } = string.Empty;

    [JsonPropertyName("repo")]
    public string Repo { get; set; } = string.Empty;

    /// <summary>Branch to watch. Null/empty = repository default branch.</summary>
    [JsonPropertyName("branch")]
    public string? Branch { get; set; }

    /// <summary>Optional sub-paths to watch (e.g. "articles/aks"). Empty = whole repo.</summary>
    [JsonPropertyName("pathFilters")]
    public List<string> PathFilters { get; set; } = [];

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    /// <summary>Commits authored before this instant are ignored (applied on the first scan).</summary>
    [JsonPropertyName("cutoffDate")]
    public DateTimeOffset CutoffDate { get; set; } = DateTimeOffset.UtcNow.AddDays(-30);

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("addedAt")]
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;

    // --- Scan cursor (embedded state) ---

    [JsonPropertyName("lastScannedCommitSha")]
    public string? LastScannedCommitSha { get; set; }

    [JsonPropertyName("lastScannedCommitDate")]
    public DateTimeOffset? LastScannedCommitDate { get; set; }

    [JsonPropertyName("lastScanAt")]
    public DateTimeOffset? LastScanAt { get; set; }

    /// <summary>ok | error | rate-limited</summary>
    [JsonPropertyName("lastScanStatus")]
    public string? LastScanStatus { get; set; }

    [JsonPropertyName("lastScanError")]
    public string? LastScanError { get; set; }

    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}
