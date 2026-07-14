namespace AzRadar.Shared.Models;

/// <summary>
/// Context passed to the LLM to judge whether a single GitHub documentation/source change
/// is relevant to an Azure platform team.
/// </summary>
public record RepoChangeContext(
    string RepoLabel,
    string Owner,
    string Repo,
    string FilePath,
    string ChangeKind,
    string CommitMessage,
    DateTimeOffset CommitDate,
    string Diff,
    string BlobUrl);
