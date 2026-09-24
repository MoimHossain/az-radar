using AzRadar.Dispatching.Core;
using AzRadar.Shared.Models;
using AzRadar.Shared.Services;

namespace AzRadar.Dispatching.Worker;

public sealed class AzureDevOpsWikiReconciliationWorker : BackgroundService
{
    private readonly DispatchingRepository _repository;
    private readonly ILogger<AzureDevOpsWikiReconciliationWorker> _logger;

    public AzureDevOpsWikiReconciliationWorker(
        DispatchingRepository repository,
        ILogger<AzureDevOpsWikiReconciliationWorker> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var targets = await _repository.GetWikiTargetsAsync(stoppingToken);
                var day = DateTimeOffset.UtcNow.ToString("yyyyMMdd");
                foreach (var target in targets.Where(target =>
                             target.RegistrationStatus == ServiceHealthChannelRegistrationStatuses.Registered &&
                             target.IncludedRegions.Count > 0 &&
                             (target.LastSucceededAt == null ||
                              target.LastSucceededAt < DateTimeOffset.UtcNow.AddHours(-24))))
                {
                    await _repository.TryCreateWikiRefreshIntentAsync(
                        new ServiceHealthDeliveryIntent
                        {
                            Id = ServiceHealthEventNormalizer.ComputeHash($"wiki-daily|{target.Id}|{day}"),
                            EventId = string.Empty,
                            ChannelId = target.Id,
                            ChannelDisplayName = target.DisplayName,
                            TargetType = ServiceHealthChannelTypes.AzureDevOpsWiki,
                            EventType = "DailyReconciliation"
                        },
                        stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to schedule Azure DevOps Wiki reconciliation");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}
