namespace AzRadar.Shared.Configuration;

public class ServiceHealthEventHubSettings
{
    public const string SectionName = "ServiceHealthEventHub";

    public string FullyQualifiedNamespace { get; set; } = string.Empty;
    public string EventHubName { get; set; } = "service-health";
    public string ConsumerGroup { get; set; } = "azradar-live";
    public string ManagedIdentityClientId { get; set; } = string.Empty;
    public bool EnableIngress { get; set; }
    public bool EnableTestPublisher { get; set; }
    public int MaximumPayloadBytes { get; set; } = 1024 * 1024;
}
