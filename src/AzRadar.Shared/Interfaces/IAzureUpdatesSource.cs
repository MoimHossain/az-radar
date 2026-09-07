namespace AzRadar.Shared.Interfaces;

/// <summary>
/// Enumerates the Azure Updates catalog and RSS feed without a recency or item limit.
/// Source failures must propagate so incomplete coverage is never reported as success.
/// </summary>
public interface IAzureUpdatesSource
{
    Task<AzureUpdatesSnapshot> GetUpdatesAsync(CancellationToken cancellationToken = default);
}

public record AzureUpdatesSnapshot(
    IReadOnlyList<AzureUpdateItem> Items,
    int CatalogCount,
    int RssCount);
