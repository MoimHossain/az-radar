using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Models;
using Microsoft.Extensions.Logging;

namespace AzRadar.Shared.Services;

public sealed class JobHeartbeatRunner(ICosmosDbService db, ILogger<JobHeartbeatRunner> logger)
{
    public Task RunAsync(CrawlJob job, Func<CancellationToken, Task> work, CancellationToken cancellationToken) =>
        RunAsync(job, work, TimeSpan.FromSeconds(30), cancellationToken);

    internal async Task RunAsync(CrawlJob job, Func<CancellationToken, Task> work,
        TimeSpan interval, CancellationToken cancellationToken)
    {
        await db.HeartbeatCrawlJobAsync(job.Id, job.AttemptCount, DateTimeOffset.UtcNow, cancellationToken);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeat = PumpAsync();
        Task? execution = null;
        try
        {
            execution = work(lifetime.Token);
            var completed = await Task.WhenAny(execution, heartbeat);
            if (completed == heartbeat)
            {
                // A failed heartbeat must not leave untracked work executing indefinitely.
                logger.LogError("Heartbeat stopped for job {JobId}; cancelling its execution", job.Id);
                await lifetime.CancelAsync();
                try
                {
                    await execution;
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
                {
                    // Preserve the heartbeat failure below.
                }
                await heartbeat;
            }
            await execution;
        }
        finally
        {
            await lifetime.CancelAsync();
            try
            {
                await heartbeat;
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
            {
                // Normal heartbeat shutdown after the handler finishes or is cancelled.
            }
        }

        async Task PumpAsync()
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(lifetime.Token))
                await db.HeartbeatCrawlJobAsync(job.Id, job.AttemptCount, DateTimeOffset.UtcNow, lifetime.Token);
        }
    }
}
