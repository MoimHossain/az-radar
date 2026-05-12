namespace AzRadar.Shared.Interfaces;

/// <summary>
/// Client for the Microsoft Release Communications MCP Server.
/// Provides structured access to Azure Updates with filtering support.
/// </summary>
public interface IMrcMcpClient
{
    /// <summary>
    /// Get recent Azure Updates with optional search text.
    /// </summary>
    Task<IReadOnlyList<AzureUpdateItem>> GetRecentAzureUpdatesAsync(
        int top = 50,
        string? search = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a specific Azure Update by its ID with full details.
    /// </summary>
    Task<AzureUpdateItem?> GetAzureUpdateByIdAsync(
        string id,
        CancellationToken cancellationToken = default);
}

public record AzureUpdateItem
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public List<string> Tags { get; init; } = [];
    public List<string> Products { get; init; } = [];
    public List<string> ProductCategories { get; init; } = [];
    public string Created { get; init; } = string.Empty;
    public string Modified { get; init; } = string.Empty;
    public string? GeneralAvailabilityDate { get; init; }
}
