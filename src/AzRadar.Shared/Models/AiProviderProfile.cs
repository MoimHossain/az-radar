namespace AzRadar.Shared.Models;

public sealed class AiProviderProfile
{
    public const string Foundry = "foundry";
    public const string Copilot = "github-copilot";
    public const string LegacyId = "ai-profile-foundry-default";
    public string Id { get; set; } = LegacyId;
    public string Provider { get; set; } = Foundry;
    public string CopilotModel { get; set; } = "gpt-5.4";
    public string? PatVersion { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AiSettingsRequest
{
    public string Provider { get; set; } = AiProviderProfile.Foundry;
    public string CopilotModel { get; set; } = "gpt-5.4";
    public string? Pat { get; set; }
    public string Prompt { get; set; } = "";
    public string? ETag { get; set; }
}

public record AiProfileState(AiProviderProfile Profile, string? ETag);
public record AiSettingsView(string Provider, string CopilotModel, bool PatConfigured,
    string Endpoint, string Deployment, string ProfileId, string? ETag);
public record AiTestResult(string Provider, string Model, string Reply, long LatencyMs);

public sealed class AiProviderException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
