using AzRadar.Shared.Models;

namespace AzRadar.Shared.Interfaces;

public interface IScheduleStore
{
    Task InitializeAsync(CancellationToken ct = default);
    Task<JobSchedule?> GetAsync(string id, CancellationToken ct = default);
    Task CreateAsync(JobSchedule schedule, CancellationToken ct = default);
    Task<bool> SaveAsync(JobSchedule schedule, ScheduleRun? run = null, bool createRun = false, CancellationToken ct = default);
    Task<ScheduleRun?> GetRunAsync(string scheduleId, string runId, CancellationToken ct = default);
    Task<SchedulePage<JobSchedule>> ListAsync(string? continuation, CancellationToken ct = default);
    Task<SchedulePage<ScheduleRun>> RunsAsync(string scheduleId, string? continuation, CancellationToken ct = default);
    IAsyncEnumerable<JobSchedule> DueAsync(DateTimeOffset now, CancellationToken ct = default);
    Task<int> SaveBarriersAsync(string jobId, IReadOnlyList<AzRadar.Shared.Services.JobReadBarrier> barriers, CancellationToken ct);
    Task ApplyBarriersAsync(string jobId, int pageCount, CancellationToken ct);
}
