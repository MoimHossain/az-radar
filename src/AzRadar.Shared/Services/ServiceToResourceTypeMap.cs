namespace AzRadar.Shared.Services;

/// <summary>
/// Curated mapping from Azure service display names to ARM resource types.
/// Used as fallback when LLM doesn't extract resource types.
/// </summary>
public static class ServiceToResourceTypeMap
{
    private static readonly Dictionary<string, string[]> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Azure Cache for Redis"] = ["Microsoft.Cache/redis"],
        ["Azure Managed Redis"] = ["Microsoft.Cache/redis"],
        ["Azure Kubernetes Service"] = ["Microsoft.ContainerService/managedClusters"],
        ["AKS"] = ["Microsoft.ContainerService/managedClusters"],
        ["Azure Virtual Machines"] = ["Microsoft.Compute/virtualMachines"],
        ["Virtual Machines"] = ["Microsoft.Compute/virtualMachines"],
        ["Azure App Service"] = ["Microsoft.Web/sites"],
        ["Azure Functions"] = ["Microsoft.Web/sites"],
        ["Azure SQL Database"] = ["Microsoft.Sql/servers/databases", "Microsoft.Sql/servers"],
        ["Azure SQL Managed Instance"] = ["Microsoft.Sql/managedInstances"],
        ["Azure Cosmos DB"] = ["Microsoft.DocumentDB/databaseAccounts"],
        ["Azure Storage"] = ["Microsoft.Storage/storageAccounts"],
        ["Azure Blob Storage"] = ["Microsoft.Storage/storageAccounts"],
        ["Azure Key Vault"] = ["Microsoft.KeyVault/vaults"],
        ["Azure Container Apps"] = ["Microsoft.App/containerApps"],
        ["Azure Container Registry"] = ["Microsoft.ContainerRegistry/registries"],
        ["Azure API Management"] = ["Microsoft.ApiManagement/service"],
        ["Azure Event Hubs"] = ["Microsoft.EventHub/namespaces"],
        ["Azure Service Bus"] = ["Microsoft.ServiceBus/namespaces"],
        ["Azure Front Door"] = ["Microsoft.Cdn/profiles", "Microsoft.Network/frontDoors"],
        ["Azure Application Gateway"] = ["Microsoft.Network/applicationGateways"],
        ["Azure Load Balancer"] = ["Microsoft.Network/loadBalancers"],
        ["Azure Firewall"] = ["Microsoft.Network/azureFirewalls"],
        ["Azure Monitor"] = ["Microsoft.Insights/components"],
        ["Application Insights"] = ["Microsoft.Insights/components"],
        ["Azure Database for PostgreSQL"] = ["Microsoft.DBforPostgreSQL/flexibleServers", "Microsoft.DBforPostgreSQL/servers"],
        ["Azure Database for MySQL"] = ["Microsoft.DBforMySQL/flexibleServers", "Microsoft.DBforMySQL/servers"],
        ["Azure Database for MariaDB"] = ["Microsoft.DBforMariaDB/servers"],
        ["Azure SignalR Service"] = ["Microsoft.SignalRService/SignalR"],
        ["Azure Cognitive Services"] = ["Microsoft.CognitiveServices/accounts"],
        ["Azure AI Services"] = ["Microsoft.CognitiveServices/accounts"],
        ["Azure AI Document Intelligence"] = ["Microsoft.CognitiveServices/accounts"],
        ["Azure AI Speech"] = ["Microsoft.CognitiveServices/accounts"],
        ["Azure Machine Learning"] = ["Microsoft.MachineLearningServices/workspaces"],
        ["Azure Disk Storage"] = ["Microsoft.Compute/disks"],
        ["Azure NetApp Files"] = ["Microsoft.NetApp/netAppAccounts"],
        ["Azure Backup"] = ["Microsoft.RecoveryServices/vaults"],
        ["Azure Site Recovery"] = ["Microsoft.RecoveryServices/vaults"],
        ["Azure Logic Apps"] = ["Microsoft.Logic/workflows"],
        ["Azure Data Factory"] = ["Microsoft.DataFactory/factories"],
        ["Azure Stream Analytics"] = ["Microsoft.StreamAnalytics/streamingjobs"],
        ["Azure HDInsight"] = ["Microsoft.HDInsight/clusters"],
        ["Azure Databricks"] = ["Microsoft.Databricks/workspaces"],
        ["Azure Batch"] = ["Microsoft.Batch/batchAccounts"],
        ["Azure IoT Hub"] = ["Microsoft.Devices/IotHubs"],
        ["Azure Notification Hubs"] = ["Microsoft.NotificationHubs/namespaces"],
        ["Azure VPN Gateway"] = ["Microsoft.Network/virtualNetworkGateways"],
        ["Azure ExpressRoute"] = ["Microsoft.Network/expressRouteCircuits"],
        ["Azure DNS"] = ["Microsoft.Network/dnsZones"],
        ["Azure Private DNS"] = ["Microsoft.Network/privateDnsZones"],
    };

    /// <summary>
    /// Resolve ARM resource types for a service name.
    /// Returns empty array if no mapping found.
    /// </summary>
    public static string[] Resolve(string serviceName)
    {
        if (Map.TryGetValue(serviceName, out var types))
            return types;

        // Try partial match
        foreach (var (key, value) in Map)
        {
            if (serviceName.Contains(key, StringComparison.OrdinalIgnoreCase) ||
                key.Contains(serviceName, StringComparison.OrdinalIgnoreCase))
                return value;
        }

        return [];
    }

    /// <summary>
    /// Resolve resource types from both explicit types and service names.
    /// </summary>
    public static HashSet<string> ResolveAll(
        IEnumerable<string> explicitResourceTypes,
        IEnumerable<string> serviceNames)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rt in explicitResourceTypes)
        {
            if (!string.IsNullOrEmpty(rt))
                result.Add(rt);
        }

        foreach (var sn in serviceNames)
        {
            foreach (var rt in Resolve(sn))
                result.Add(rt);
        }

        return result;
    }
}
