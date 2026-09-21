namespace AzRadar.Shared.Configuration;

public sealed class AzureDevOpsWikiSettings
{
    public const string SectionName = "AzureDevOpsWiki";

    public string KeyVaultUri { get; set; } = string.Empty;
    public string ManagedIdentityClientId { get; set; } = string.Empty;
    public string AzureDevOpsTokenScope { get; set; } =
        "499b84ac-1321-427f-aa17-267ca6975798/.default";
    public int RequestTimeoutSeconds { get; set; } = 30;
    public int MaximumPageCharacters { get; set; } = 1_500_000;
}
