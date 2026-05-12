using System.Text.Json;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.ResourceGraph;
using Azure.ResourceManager.ResourceGraph.Models;
using AzRadar.Shared.Interfaces;
using Microsoft.Extensions.Logging;

namespace AzRadar.Shared.Services;

public class ResourceGraphClient : IResourceGraphClient
{
    private readonly ILogger<ResourceGraphClient> _logger;

    public ResourceGraphClient(ILogger<ResourceGraphClient> logger)
    {
        _logger = logger;
    }

    public async Task<ResourceGraphQueryResult> QueryResourcesByTypeAsync(
        string resourceType, string uamiClientId, int maxResults = 200,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("ARG query: type={ResourceType}, uami={Uami}", resourceType, uamiClientId);

        try
        {
            var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ManagedIdentityClientId = uamiClientId
            });

            var armClient = new ArmClient(credential);
            var tenant = armClient.GetTenants().First();

            var query = $@"
                resources
                | where type =~ '{resourceType}'
                | project subscriptionId, resourceGroup, name, type, location,
                          sku = coalesce(sku.name, ''),
                          tags
                | take {maxResults}";

            var queryContent = new ResourceQueryContent(query);

            var response = await tenant.GetResourcesAsync(queryContent, cancellationToken);
            var result = response.Value;

            var resources = new List<ResourceGraphResource>();

            var dataJson = result.Data.ToObjectFromJson<JsonElement>();
            if (dataJson.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in dataJson.EnumerateArray())
                {
                    var resource = new ResourceGraphResource
                    {
                        SubscriptionId = row.TryGetProperty("subscriptionId", out var sub) ? sub.GetString() ?? "" : "",
                        ResourceGroup = row.TryGetProperty("resourceGroup", out var rg) ? rg.GetString() ?? "" : "",
                        Name = row.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                        Type = row.TryGetProperty("type", out var type) ? type.GetString() ?? "" : "",
                        Location = row.TryGetProperty("location", out var loc) ? loc.GetString() ?? "" : "",
                        Sku = row.TryGetProperty("sku", out var sku) ? sku.GetString() ?? "" : "",
                        Tags = ParseTags(row),
                    };
                    resources.Add(resource);
                }
            }

            _logger.LogInformation("ARG returned {Count} resources for {Type}",
                resources.Count, resourceType);

            return new ResourceGraphQueryResult
            {
                Resources = resources,
                TotalCount = (int)result.TotalRecords,
                Truncated = result.ResultTruncated.ToString() == "True",
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ARG query failed for {ResourceType}", resourceType);
            return new ResourceGraphQueryResult();
        }
    }

    private static Dictionary<string, string> ParseTags(JsonElement row)
    {
        var tags = new Dictionary<string, string>();
        if (!row.TryGetProperty("tags", out var tagsEl) || tagsEl.ValueKind != JsonValueKind.Object)
            return tags;

        // Only extract well-known ownership tags
        string[] allowedTags = ["team", "owner", "env", "environment", "costCenter", "application", "department"];
        foreach (var tag in allowedTags)
        {
            if (tagsEl.TryGetProperty(tag, out var val) && val.ValueKind == JsonValueKind.String)
                tags[tag] = val.GetString() ?? "";
        }

        return tags;
    }
}
