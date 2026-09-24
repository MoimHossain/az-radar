using System.Text.Json.Serialization;

namespace AzRadar.Shared.Models;

public class ServiceHealthNotificationChannel
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = ServiceHealthChannelTypes.TeamsBot;

    [JsonPropertyName("tenantId")]
    public string TenantId { get; set; } = string.Empty;

    [JsonPropertyName("teamId")]
    public string TeamId { get; set; } = string.Empty;

    [JsonPropertyName("teamName")]
    public string TeamName { get; set; } = string.Empty;

    [JsonPropertyName("channelId")]
    public string ChannelId { get; set; } = string.Empty;

    [JsonPropertyName("channelName")]
    public string ChannelName { get; set; } = string.Empty;

    [JsonPropertyName("conversationReferenceId")]
    public string ConversationReferenceId { get; set; } = string.Empty;

    [JsonPropertyName("wikiUri")]
    public string WikiUri { get; set; } = string.Empty;

    [JsonPropertyName("azureDevOpsOrganization")]
    public string AzureDevOpsOrganization { get; set; } = string.Empty;

    [JsonPropertyName("azureDevOpsProject")]
    public string AzureDevOpsProject { get; set; } = string.Empty;

    [JsonPropertyName("azureDevOpsWikiIdentifier")]
    public string AzureDevOpsWikiIdentifier { get; set; } = string.Empty;

    [JsonPropertyName("azureDevOpsPagePath")]
    public string AzureDevOpsPagePath { get; set; } = string.Empty;

    [JsonPropertyName("azureDevOpsPageId")]
    public int? AzureDevOpsPageId { get; set; }

    [JsonPropertyName("authenticationType")]
    public string AuthenticationType { get; set; } = string.Empty;

    [JsonPropertyName("credentialSecretName")]
    public string CredentialSecretName { get; set; } = string.Empty;

    [JsonPropertyName("managedIdentityClientId")]
    public string ManagedIdentityClientId { get; set; } = string.Empty;

    [JsonPropertyName("credentialExpiresAt")]
    public DateTimeOffset? CredentialExpiresAt { get; set; }

    [JsonPropertyName("registrationStatus")]
    public string RegistrationStatus { get; set; } = ServiceHealthChannelRegistrationStatuses.Pending;

    [JsonPropertyName("subscribedEventTypes")]
    public List<string> SubscribedEventTypes { get; set; } = [];

    [JsonPropertyName("includedRegions")]
    public List<string> IncludedRegions { get; set; } = [];

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("lastRegisteredAt")]
    public DateTimeOffset? LastRegisteredAt { get; set; }

    [JsonPropertyName("lastAttemptedAt")]
    public DateTimeOffset? LastAttemptedAt { get; set; }

    [JsonPropertyName("lastSucceededAt")]
    public DateTimeOffset? LastSucceededAt { get; set; }

    [JsonPropertyName("lastRenderedContentHash")]
    public string LastRenderedContentHash { get; set; } = string.Empty;

    [JsonPropertyName("lastExternalVersion")]
    public string LastExternalVersion { get; set; } = string.Empty;

    [JsonPropertyName("lastErrorCode")]
    public string? LastErrorCode { get; set; }

    [JsonPropertyName("lastErrorMessage")]
    public string? LastErrorMessage { get; set; }
}

public static class ServiceHealthChannelTypes
{
    public const string TeamsBot = "teams-bot";
    public const string AzureDevOpsWiki = "azure-devops-wiki";
}

public static class ServiceHealthChannelRegistrationStatuses
{
    public const string Pending = "pending";
    public const string Registered = "registered";
    public const string Uninstalled = "uninstalled";
    public const string Degraded = "degraded";
    public const string Disabled = "disabled";
    public const string PermissionRequired = "permission-required";
    public const string ConfigurationRequired = "configuration-required";
}

public static class AzureDevOpsAuthenticationTypes
{
    public const string PersonalAccessToken = "pat";
    public const string ManagedIdentity = "managed-identity";

    public static readonly IReadOnlySet<string> Supported = new HashSet<string>(
        [PersonalAccessToken, ManagedIdentity],
        StringComparer.OrdinalIgnoreCase);
}

public static class ServiceHealthEventTypes
{
    public const string ServiceIssue = "ServiceIssue";
    public const string PlannedMaintenance = "PlannedMaintenance";
    public const string HealthAdvisory = "HealthAdvisory";
    public const string SecurityAdvisory = "SecurityAdvisory";

    public static readonly IReadOnlySet<string> Supported = new HashSet<string>(
        [ServiceIssue, PlannedMaintenance, HealthAdvisory, SecurityAdvisory],
        StringComparer.OrdinalIgnoreCase);
}
