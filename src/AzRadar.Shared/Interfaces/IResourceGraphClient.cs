namespace AzRadar.Shared.Interfaces;

/// <summary>
/// Client for querying Azure Resource Graph.
/// </summary>
public interface IResourceGraphClient
{
    /// <summary>
    /// Query Azure Resource Graph for resources of a specific type.
    /// Returns up to maxResults matching resources.
    /// </summary>
    Task<ResourceGraphQueryResult> QueryResourcesByTypeAsync(
        string resourceType,
        string uamiClientId,
        int maxResults = 200,
        CancellationToken cancellationToken = default);
}

public class ResourceGraphQueryResult
{
    public List<ResourceGraphResource> Resources { get; set; } = [];
    public int TotalCount { get; set; }
    public bool Truncated { get; set; }
}

public class ResourceGraphResource
{
    public string SubscriptionId { get; set; } = string.Empty;
    public string ResourceGroup { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public Dictionary<string, string> Tags { get; set; } = new();
}
