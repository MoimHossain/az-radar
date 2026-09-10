namespace AzRadar.Shared.Configuration;

public class ServiceHealthProvisioningSettings
{
    public const string SectionName = "ServiceHealthProvisioning";

    public string ManagedIdentityClientId { get; set; } = string.Empty;
    public string DiagnosticSettingName { get; set; } = "az-radar-service-health";
    public string EventHubAuthorizationRuleId { get; set; } = string.Empty;
    public string EventHubName { get; set; } = "service-health";
}
