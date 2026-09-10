using System.Text.Json.Serialization;

namespace AzRadar.Shared.Models;

public class ServiceHealthSubscription
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("tenantId")]
    public string TenantId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = ServiceHealthSubscriptionStatus.Registering;

    [JsonPropertyName("diagnosticSettingName")]
    public string DiagnosticSettingName { get; set; } = string.Empty;

    [JsonPropertyName("eventHubAuthorizationRuleId")]
    public string EventHubAuthorizationRuleId { get; set; } = string.Empty;

    [JsonPropertyName("eventHubName")]
    public string EventHubName { get; set; } = string.Empty;

    [JsonPropertyName("provisioningIdentityClientId")]
    public string ProvisioningIdentityClientId { get; set; } = string.Empty;

    [JsonPropertyName("lastVerifiedAt")]
    public DateTimeOffset? LastVerifiedAt { get; set; }

    [JsonPropertyName("lastProvisioningAttemptAt")]
    public DateTimeOffset? LastProvisioningAttemptAt { get; set; }

    [JsonPropertyName("lastErrorCode")]
    public string? LastErrorCode { get; set; }

    [JsonPropertyName("lastErrorMessage")]
    public string? LastErrorMessage { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class ServiceHealthSubscriptionStatus
{
    public const string Registering = "registering";
    public const string Active = "active";
    public const string PermissionRequired = "permission-required";
    public const string ConfigurationFailed = "configuration-failed";
    public const string Disabled = "disabled";
}
