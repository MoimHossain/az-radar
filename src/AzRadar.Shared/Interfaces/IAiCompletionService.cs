using AzRadar.Shared.Models;

namespace AzRadar.Shared.Interfaces;

public interface IAiCompletionService
{
    Task<string> CompleteAsync(string system, string prompt, bool json, CancellationToken ct = default);
    Task<AiTestResult> TestAsync(AiSettingsRequest request, CancellationToken ct = default);
}

public interface IAiProfileStore
{
    Task<AiProfileState> GetActiveAsync(CancellationToken ct = default);
    Task<AiProviderProfile> GetAsync(string id, CancellationToken ct = default);
    Task SaveAsync(AiProviderProfile profile, string? etag, CancellationToken ct = default);
}

public interface ICopilotCredentialStore
{
    bool Configured { get; }
    Task<string> SaveAsync(string pat, CancellationToken ct = default);
    Task<string> GetAsync(string version, CancellationToken ct = default);
}

public interface ICopilotCompletion
{
    Task<string> CompleteAsync(string pat, string model, string system, string prompt, CancellationToken ct);
}
