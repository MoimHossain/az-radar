using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Services;

namespace AzRadar.JobHost;

public sealed class ScheduleWorker(
    IScheduleStore store,
    ScheduleCoordinator coordinator,
    TimeProvider clock,
    ILogger<ScheduleWorker> logger) : BackgroundService
{
    public DateTimeOffset? LastSuccessfulPass { get; private set; }
    public string? LastError { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                var errors = 0;
                await foreach (var schedule in store.DueAsync(clock.GetUtcNow(), stoppingToken))
                {
                    try { await coordinator.ReconcileAsync(schedule.ScheduleId, stoppingToken); }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                    catch (Exception ex)
                    {
                        errors++;
                        LastError = ex.Message;
                        logger.LogError(ex, "Schedule {ScheduleId} reconciliation failed", schedule.ScheduleId);
                    }
                }
                if (errors == 0)
                {
                    LastSuccessfulPass = clock.GetUtcNow();
                    LastError = null;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                LastError = ex.Message;
                logger.LogError(ex, "Scheduling pass failed; retrying on the next pass");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
