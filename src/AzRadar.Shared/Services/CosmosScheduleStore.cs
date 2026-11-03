using System.Net;
using System.Runtime.CompilerServices;
using AzRadar.Shared.Configuration;
using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace AzRadar.Shared.Services;

public sealed class CosmosScheduleStore(CosmosClient client, IOptions<CosmosDbSettings> settings) : IScheduleStore
{
    private Container Container => client.GetContainer(settings.Value.DatabaseName, settings.Value.SchedulesContainer);

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await client.GetDatabase(settings.Value.DatabaseName).CreateContainerIfNotExistsAsync(
            new ContainerProperties(settings.Value.SchedulesContainer, "/scheduleId") { DefaultTimeToLive = -1 },
            cancellationToken: ct);
    }

    public async Task<JobSchedule?> GetAsync(string id, CancellationToken ct = default)
    {
        try
        {
            var response = await Container.ReadItemAsync<JobSchedule>("definition", new PartitionKey(id), cancellationToken: ct);
            response.Resource.ETag = response.ETag;
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return null; }
    }

    public async Task CreateAsync(JobSchedule schedule, CancellationToken ct = default)
    {
        var response = await Container.CreateItemAsync(schedule, new PartitionKey(schedule.ScheduleId), cancellationToken: ct);
        schedule.ETag = response.ETag;
    }

    public async Task<bool> SaveAsync(JobSchedule schedule, ScheduleRun? run = null, bool createRun = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(schedule.ETag)) throw new InvalidOperationException("Schedule ETag is required.");
        var batch = Container.CreateTransactionalBatch(new PartitionKey(schedule.ScheduleId))
            .ReplaceItem(schedule.Id, schedule, new TransactionalBatchItemRequestOptions { IfMatchEtag = schedule.ETag });
        if (run is not null)
        {
            if (createRun) batch.CreateItem(run);
            else batch.ReplaceItem(run.Id, run, new TransactionalBatchItemRequestOptions { IfMatchEtag = run.ETag });
        }
        using var response = await batch.ExecuteAsync(ct);
        if (response.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict) return false;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Schedule transaction failed: {response.StatusCode}: {response.ErrorMessage}");
        schedule.ETag = response.GetOperationResultAtIndex<JobSchedule>(0).ETag;
        return true;
    }

    public async Task<ScheduleRun?> GetRunAsync(string scheduleId, string runId, CancellationToken ct = default)
    {
        try
        {
            var response = await Container.ReadItemAsync<ScheduleRun>(runId, new PartitionKey(scheduleId), cancellationToken: ct);
            response.Resource.ETag = response.ETag;
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return null; }
    }

    public async Task<SchedulePage<JobSchedule>> ListAsync(string? continuation, CancellationToken ct = default)
    {
        using var iterator = Container.GetItemQueryIterator<JobSchedule>(
            new QueryDefinition("SELECT * FROM c WHERE c.kind = 'schedule' AND c.deleted = false"),
            continuation, new QueryRequestOptions { MaxItemCount = 50 });
        var page = await iterator.ReadNextAsync(ct);
        return new(page.ToList(), page.ContinuationToken);
    }

    public async Task<SchedulePage<ScheduleRun>> RunsAsync(string scheduleId, string? continuation, CancellationToken ct = default)
    {
        using var iterator = Container.GetItemQueryIterator<ScheduleRun>(
            new QueryDefinition("SELECT * FROM c WHERE c.kind = 'run' ORDER BY c.createdAt DESC"),
            continuation, new QueryRequestOptions { PartitionKey = new PartitionKey(scheduleId), MaxItemCount = 30 });
        var page = await iterator.ReadNextAsync(ct);
        return new(page.ToList(), page.ContinuationToken);
    }

    public async IAsyncEnumerable<JobSchedule> DueAsync(DateTimeOffset now, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var iterator = Container.GetItemQueryIterator<JobSchedule>(
            new QueryDefinition("SELECT * FROM c WHERE c.kind = 'schedule' AND c.wakeAt <= @now")
                .WithParameter("@now", now.ToUnixTimeMilliseconds()),
            requestOptions: new QueryRequestOptions { MaxItemCount = 100 });
        while (iterator.HasMoreResults)
            foreach (var item in await iterator.ReadNextAsync(ct))
                yield return item;
    }

    public async Task<int> SaveBarriersAsync(string jobId, IReadOnlyList<JobReadBarrier> barriers, CancellationToken ct)
    {
        var pages = barriers.Chunk(100).ToArray();
        for (var i = 0; i < pages.Length; i++)
            await Container.CreateItemAsync(new JobBarrierPage
            {
                Id = $"barrier-{i}", ScheduleId = jobId, Items = pages[i].ToList()
            }, new PartitionKey(jobId), cancellationToken: ct);
        return pages.Length;
    }

    public async Task ApplyBarriersAsync(string jobId, int pageCount, CancellationToken ct)
    {
        if (pageCount < 0 || pageCount > 200) throw new InvalidOperationException("Invalid completion barrier.");
        for (var i = 0; i < pageCount; i++)
        {
            var page = await Container.ReadItemAsync<JobBarrierPage>($"barrier-{i}", new PartitionKey(jobId), cancellationToken: ct);
            foreach (var item in page.Resource.Items)
            {
                using var response = await client.GetContainer(settings.Value.DatabaseName, item.Container)
                    .ReadItemStreamAsync(item.ItemId, new PartitionKey(item.ItemId),
                        new ItemRequestOptions { SessionToken = item.SessionToken }, ct);
                if (response.StatusCode != HttpStatusCode.NotFound) response.EnsureSuccessStatusCode();
            }
        }
    }
}
