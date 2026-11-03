using System.Text.RegularExpressions;
using AzRadar.Shared.Configuration;
using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Models;
using Microsoft.Extensions.Options;

namespace AzRadar.Shared.Services;

public sealed partial class AiSettingsService(IAiProfileStore profiles, ICopilotCredentialStore credentials,
    IAiCompletionService completions, IOptions<OpenAiSettings> foundry)
{
    public static string? Validate(AiSettingsRequest request, bool test)
    {
        if (request.Provider is not (AiProviderProfile.Foundry or AiProviderProfile.Copilot))
            return "Choose Foundry Endpoint or GitHub Copilot.";
        if (request.Provider == AiProviderProfile.Copilot &&
            !ModelPattern().IsMatch(request.CopilotModel ?? ""))
            return "Enter a valid explicit Copilot model ID (1-100 letters, digits, dots, underscores or hyphens).";
        if (!string.IsNullOrEmpty(request.Pat) &&
            (!request.Pat.StartsWith("github_pat_", StringComparison.Ordinal) ||
             request.Pat.Length > 512 || request.Pat.Any(char.IsWhiteSpace)))
            return "Use a fine-grained github_pat_ token, without whitespace. Classic PATs are not supported.";
        if (test && (string.IsNullOrWhiteSpace(request.Prompt) || request.Prompt.Length > 4000))
            return "Enter a test prompt of 1-4000 characters.";
        return null;
    }

    public async Task<AiSettingsView> GetAsync(CancellationToken ct = default)
    {
        var state = await profiles.GetActiveAsync(ct);
        return View(state);
    }

    private AiSettingsView View(AiProfileState state) => new(state.Profile.Provider,
        state.Profile.CopilotModel, state.Profile.PatVersion is not null, foundry.Value.Endpoint,
        foundry.Value.DeploymentName, state.Profile.Id, state.ETag);

    public async Task<AiSettingsView> SaveAsync(AiSettingsRequest request, CancellationToken ct = default)
    {
        if (Validate(request, false) is { } error) throw new AiProviderException("invalid_settings", error);
        var current = await profiles.GetActiveAsync(ct);
        if (request.ETag != current.ETag)
            throw new AiProviderException("configuration_changed", "AI settings changed. Refresh and retry.");
        if (request.Provider == AiProviderProfile.Copilot && !credentials.Configured)
            throw new AiProviderException("vault_not_configured", "The AI credential Key Vault is not configured.");

        // Verify the exact candidate before publishing it; never persist test-only PATs.
        await completions.TestAsync(new AiSettingsRequest
        {
            Provider = request.Provider,
            CopilotModel = request.CopilotModel,
            Pat = request.Pat,
            Prompt = "Reply with the single word OK."
        }, ct);
        var version = current.Profile.PatVersion;
        if (request.Provider == AiProviderProfile.Copilot && !string.IsNullOrEmpty(request.Pat))
            version = await credentials.SaveAsync(request.Pat, ct);
        var profile = new AiProviderProfile
        {
            Id = $"ai-profile-{Guid.NewGuid():N}",
            Provider = request.Provider,
            CopilotModel = request.CopilotModel,
            PatVersion = version
        };
        await profiles.SaveAsync(profile, current.ETag, ct);
        return await GetAsync(ct);
    }

    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9._-]{0,99}$")]
    private static partial Regex ModelPattern();
}
