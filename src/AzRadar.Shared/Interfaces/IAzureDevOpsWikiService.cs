using AzRadar.Shared.Models;

namespace AzRadar.Shared.Interfaces;

public interface IAzureDevOpsWikiService
{
    Task StorePatAsync(
        string secretName,
        string personalAccessToken,
        CancellationToken cancellationToken = default);

    Task DeletePatAsync(
        string secretName,
        CancellationToken cancellationToken = default);

    Task<AzureDevOpsWikiPageSnapshot> GetPageAsync(
        ServiceHealthNotificationChannel target,
        CancellationToken cancellationToken = default);

    Task<AzureDevOpsWikiUpdateResult> UpdatePageAsync(
        ServiceHealthNotificationChannel target,
        string content,
        string eTag,
        CancellationToken cancellationToken = default);
}
