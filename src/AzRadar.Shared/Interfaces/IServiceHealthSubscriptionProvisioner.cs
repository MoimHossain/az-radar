using AzRadar.Shared.Models;

namespace AzRadar.Shared.Interfaces;

public interface IServiceHealthSubscriptionProvisioner
{
    Task<ServiceHealthProvisioningResult> ProvisionAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default);

    Task<ServiceHealthProvisioningResult> VerifyAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default);
}

public record ServiceHealthProvisioningResult(
    bool Succeeded,
    string Status,
    string DisplayName,
    string TenantId,
    string? ErrorCode = null,
    string? ErrorMessage = null);
