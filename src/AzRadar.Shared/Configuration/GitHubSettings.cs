namespace AzRadar.Shared.Configuration;

public class GitHubSettings
{
    public const string SectionName = "GitHub";

    /// <summary>GitHub REST API base URL.</summary>
    public string ApiBaseUrl { get; set; } = "https://api.github.com";

    /// <summary>Max commits expanded per repo per run (cost/time bound).</summary>
    public int MaxCommitsPerRepo { get; set; } = 100;

    /// <summary>Max changed files analyzed per commit.</summary>
    public int MaxFilesPerCommit { get; set; } = 50;

    /// <summary>Max changed files analyzed per run across all repos.</summary>
    public int MaxFilesPerRun { get; set; } = 200;

    public int RequestTimeoutSeconds { get; set; } = 100;

    /// <summary>Diff/content characters sent to the LLM (truncated beyond this).</summary>
    public int DiffMaxChars { get; set; } = 8000;
}
