namespace AzRadar.Shared.Interfaces;

/// <summary>
/// Client for the Microsoft Learn MCP Server.
/// Uses the MCP protocol to search and fetch documentation.
/// </summary>
public interface IMcpDocsClient
{
    /// <summary>
    /// Search Microsoft Learn documentation for a query.
    /// Returns a list of (title, url, snippet) tuples.
    /// </summary>
    Task<IReadOnlyList<McpSearchResult>> SearchDocsAsync(
        string query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetch the full content of a Microsoft Learn page.
    /// </summary>
    Task<string> FetchDocAsync(
        string url,
        CancellationToken cancellationToken = default);
}

public record McpSearchResult(string Title, string Url, string Snippet)
{
    /// <summary>
    /// Full document content returned by MCP search (may be large).
    /// </summary>
    public string FullContent { get; init; } = string.Empty;
}
