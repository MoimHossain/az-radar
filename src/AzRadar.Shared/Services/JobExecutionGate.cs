using System.Net;
using AzRadar.Shared.Configuration;
using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace AzRadar.Shared.Services;

// No expiry: an unresponsive worker must not be mistaken for a stopped worker.
public sealed class JobExecutionGate(CosmosClient client, IOptions<CosmosDbSettings> settings, ICosmosDbService jobs)
{
    private Container Container => client.GetContainer(settings.Value.DatabaseName, settings.Value.SchedulesContainer);

    public async Task<ExecutionGateState> GetStateAsync(CancellationToken ct)
    {
        try
        {
            var held = await Container.ReadItemAsync<ExecutionLock>("gate", new PartitionKey("execution"), cancellationToken: ct);
            var job = await jobs.GetCrawlJobAsync(held.Resource.JobId, ct);
            var heartbeat = job?.LastHeartbeatAt ?? job?.StartedAt ?? job?.CreatedAt;
            return new(held.Resource.JobId, job is null ||
                (job.Status is not (CrawlJobStatus.Completed or CrawlJobStatus.Failed) &&
                 (!heartbeat.HasValue || DateTimeOffset.UtcNow - heartbeat.Value > TimeSpan.FromMinutes(5))), heartbeat);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return new(null, false, null); }
    }

    public record ExecutionGateState(string? JobId, bool Stalled, DateTimeOffset? LastHeartbeat);

    public async Task<bool> TryAcquireAsync(CrawlJob job, CancellationToken ct)
    {
        try
        {
            await Container.CreateItemAsync(new ExecutionLock { JobId = job.Id, Attempt = job.AttemptCount },
                new PartitionKey("execution"), cancellationToken: ct);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            var held = await Container.ReadItemAsync<ExecutionLock>("gate", new PartitionKey("execution"), cancellationToken: ct);
            var owner = await jobs.GetCrawlJobAsync(held.Resource.JobId, ct);
            if (owner?.Status is CrawlJobStatus.Completed or CrawlJobStatus.Failed)
            {
                try
                {
                    await Container.DeleteItemAsync<ExecutionLock>("gate", new PartitionKey("execution"),
                        new ItemRequestOptions { IfMatchEtag = held.ETag }, ct);
                }
                catch (CosmosException race) when (race.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.NotFound) { }
            }
            return false;
        }
    }

    public async Task ReleaseAsync(CrawlJob job, CancellationToken ct)
    {
        try
        {
            var held = await Container.ReadItemAsync<ExecutionLock>("gate", new PartitionKey("execution"), cancellationToken: ct);
            if (held.Resource.JobId != job.Id || held.Resource.Attempt != job.AttemptCount) return;
            await Container.DeleteItemAsync<ExecutionLock>("gate", new PartitionKey("execution"),
                new ItemRequestOptions { IfMatchEtag = held.ETag }, ct);
        }
        catch (CosmosException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.PreconditionFailed) { }
    }

    public class ExecutionLock
    {
        public string Id { get; set; } = "gate";
        public string ScheduleId { get; set; } = "execution";
        public string Kind { get; set; } = "execution-lock";
        public string JobId { get; set; } = "";
        public int Attempt { get; set; }
    }
}
