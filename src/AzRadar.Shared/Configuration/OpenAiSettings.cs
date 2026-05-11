namespace AzRadar.Shared.Configuration;

public class OpenAiSettings
{
    public const string SectionName = "OpenAi";

    public string Endpoint { get; set; } = string.Empty;
    public string DeploymentName { get; set; } = "gpt-4o";

    /// <summary>
    /// Client ID of the User-Assigned Managed Identity used to authenticate with Azure OpenAI.
    /// </summary>
    public string ManagedIdentityClientId { get; set; } = string.Empty;
}
