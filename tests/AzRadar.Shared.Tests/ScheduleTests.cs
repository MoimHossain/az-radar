using System.Runtime.CompilerServices;
using System.Text.Json;
using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Models;
using AzRadar.Shared.Services;
using Moq;

namespace AzRadar.Shared.Tests;

public class ScheduleTests
{
    [Theory]
    [InlineData("2026-03-29", "02:30", "2026-03-29T01:00:00Z")]
    [InlineData("2026-10-25", "02:30", "2026-10-25T00:30:00Z")]
    public void Recurrence_resolves_dst_gap_and_fold_once(string date, string time, string expected)
    {
        var request = Request();
        request.LocalDate = date;
        request.LocalTime = time;
        request.TimeZone = "Europe/Amsterdam";
        Assert.Equal(DateTimeOffset.Parse(expected), ScheduleRecurrence.Next(request, DateTimeOffset.Parse(date).AddDays(-1)));
        Assert.Null(ScheduleRecurrence.Next(request, DateTimeOffset.Parse(expected)));
    }

    [Fact]
    public void Daily_is_calendar_based_and_does_not_repeat_fall_back_hour()
    {
        var request = Request();
        request.Frequency = "daily";
        request.LocalDate = "2026-10-25";
        request.LocalTime = "02:30";
        request.TimeZone = "Europe/Amsterdam";
        Assert.Equal(DateTimeOffset.Parse("2026-10-26T01:30:00Z"),
            ScheduleRecurrence.Next(request, DateTimeOffset.Parse("2026-10-25T00:31:00Z")));
    }

    [Fact]
    public void Weekly_honors_start_date_weekdays_and_non_hour_offset()
    {
        var request = Request();
        request.Frequency = "weekly";
        request.TimeZone = "Asia/Kathmandu";
        request.Weekdays = [1];
        request.LocalDate = "2026-11-03";
        Assert.Equal(DateTimeOffset.Parse("2026-11-08T18:15:00Z"),
            ScheduleRecurrence.Next(request, DateTimeOffset.Parse("2026-11-02T00:00:00Z")));
    }

    [Theory]
    [InlineData("sometimes", "UTC", "azure-updates")]
    [InlineData("daily", "No/SuchZone", "azure-updates")]
    [InlineData("daily", "UTC", "arbitrary-code")]
    public void Invalid_recurrence_and_job_types_are_rejected(string frequency, string zone, string type)
    {
        var request = Request();
        request.Frequency = frequency;
        request.TimeZone = zone;
        request.Primary.JobType = type;
        Assert.NotNull(ScheduleRecurrence.Validate(request));
    }

    [Fact]
    public async Task Admission_is_durable_before_dispatch_and_repeated_checks_do_not_duplicate()
    {
        var f = new Fixture();
        await f.Tick();
        Assert.Single(f.Store.Runs);
        Assert.Empty(f.Jobs);
        await f.Tick();
        await f.Tick();
        Assert.Single(f.Jobs);
        Assert.Single(f.Store.Runs);
        Assert.NotNull(f.Store.Schedule.ActiveRunId);
    }

    [Theory]
    [InlineData("completed", "succeeded", true)]
    [InlineData("completed", "partial", false)]
    [InlineData("completed", null, false)]
    [InlineData("failed", "failed", false)]
    public async Task Only_explicit_primary_success_admits_post_job(string status, string? outcome, bool postExpected)
    {
        var f = new Fixture();
        await f.Tick();
        await f.Tick();
        var primary = Assert.Single(f.Jobs).Value;
        primary.Status = status;
        primary.Outcome = outcome;
        primary.BarrierPages = outcome == "succeeded" ? 0 : null;
        await f.Tick();
        await f.Tick();
        Assert.Equal(postExpected ? 2 : 1, f.Jobs.Count);
        Assert.Equal(postExpected ? "post-running" : "failed", Assert.Single(f.Store.Runs).Value.Status);
    }

    [Fact]
    public async Task Lost_job_create_acknowledgement_is_recovered_using_same_id()
    {
        var f = new Fixture();
        await f.Tick();
        f.FailNextCreateAcknowledgement = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => f.Tick());
        await f.Tick();
        Assert.Single(f.Jobs);
        Assert.Equal("primary-running", Assert.Single(f.Store.Runs).Value.Status);
    }

    [Fact]
    public async Task Missing_acknowledged_job_blocks_chain_instead_of_recreating_it()
    {
        var f = new Fixture();
        await f.Tick();
        await f.Tick();
        f.Jobs.Clear();
        await f.Tick();
        Assert.Empty(f.Jobs);
        Assert.Equal("attention-required", Assert.Single(f.Store.Runs).Value.Status);
        Assert.NotNull(f.Store.Schedule.ActiveRunId);
    }

    [Fact]
    public async Task Stale_primary_never_triggers_post_and_operator_failure_unblocks_schedule()
    {
        var f = new Fixture();
        await f.Tick();
        await f.Tick();
        var job = Assert.Single(f.Jobs).Value;
        job.Status = "processing";
        job.LastHeartbeatAt = f.Clock.Now.AddMinutes(-6);
        await f.Tick();
        Assert.Equal("attention-required", Assert.Single(f.Store.Runs).Value.Status);
        Assert.Single(f.Jobs);
        job.Status = "failed";
        job.Outcome = "interrupted";
        await f.Tick();
        Assert.Equal("failed", Assert.Single(f.Store.Runs).Value.Status);
        Assert.Null(f.Store.Schedule.ActiveRunId);
    }

    [Fact]
    public async Task Paused_or_deleted_schedule_finishes_admitted_chain()
    {
        var f = new Fixture();
        await f.Tick();
        f.Store.Schedule.Enabled = false;
        f.Store.Schedule.Deleted = true;
        await f.Tick();
        Assert.Single(f.Jobs).Value.Outcome = "succeeded";
        Assert.Single(f.Jobs).Value.Status = "completed";
        await f.Tick();
        await f.Tick();
        Assert.Equal(2, f.Jobs.Count);
    }

    [Fact]
    public async Task Multi_day_outage_coalesces_to_latest_due_occurrence()
    {
        var f = new Fixture();
        f.Store.Schedule.Configuration.Frequency = "daily";
        f.Store.Schedule.Configuration.LocalDate = "2026-10-01";
        f.Store.Schedule.NextDueAt = new DateTimeOffset(f.Clock.Now.UtcDateTime.Date.AddDays(-3));
        await f.Tick();
        Assert.Single(f.Store.Runs);
        Assert.Equal(3, f.Store.Schedule.MissedOccurrences);
        Assert.True(f.Store.Schedule.NextDueAt > f.Clock.Now);
    }

    [Fact]
    public async Task Expired_one_time_run_is_recorded_as_missed_not_executed()
    {
        var f = new Fixture();
        f.Store.Schedule.NextDueAt = f.Clock.Now.AddDays(-2);
        f.Store.Schedule.Configuration.LocalDate = "2026-11-01";
        f.Store.Schedule.Configuration.LocalTime = "08:00";
        await f.Tick();
        Assert.Empty(f.Store.Runs);
        Assert.Equal("missed", f.Store.Schedule.LastOutcome);
    }

    [Fact]
    public async Task Concurrent_admission_transactions_allow_one_winner()
    {
        var store = new MemoryStore();
        var first = await store.GetAsync("test");
        var second = await store.GetAsync("test");
        Assert.True(await store.SaveAsync(first!, new ScheduleRun { Id = "first" }, true));
        Assert.False(await store.SaveAsync(second!, new ScheduleRun { Id = "second" }, true));
        Assert.Single(store.Runs);
    }

    [Fact]
    public async Task Retryable_pre_execution_failure_waits_and_uses_a_new_job_identity()
    {
        var f = new Fixture();
        await f.Tick();
        await f.Tick();
        var first = Assert.Single(f.Jobs).Value;
        first.Status = "failed";
        first.Retryable = true;
        await f.Tick();
        await f.Tick();
        Assert.Single(f.Jobs);
        f.Clock.Now = f.Clock.Now.AddMinutes(2);
        await f.Tick();
        Assert.Equal(2, f.Jobs.Count);
        Assert.All(f.Jobs.Values, job => Assert.Equal("azure-updates", job.JobType));
    }

    [Fact]
    public void Read_barriers_are_captured_only_inside_scheduled_execution()
    {
        JobExecutionContext.Record("feed", "outside", null);
        using var context = new JobExecutionContext(true);
        Assert.Throws<InvalidOperationException>(() => JobExecutionContext.Record("feed", "missing", null));
        JobExecutionContext.Record("feed", "one", "opaque-token");
        Assert.Equal("opaque-token", Assert.Single(context.Barriers).SessionToken);
    }

    [Fact]
    public async Task Provider_revision_is_pinned_across_primary_and_post_job()
    {
        var profiles = new Mock<IAiProfileStore>();
        var active = new AiProviderProfile { Id = "ai-profile-first" };
        profiles.Setup(x => x.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AiProfileState(active, "etag"));
        var f = new Fixture(profiles.Object);
        await f.Tick();
        await f.Tick();
        var primary = Assert.Single(f.Jobs).Value;
        Assert.Equal("ai-profile-first", primary.AiProfileId);
        active = new AiProviderProfile { Id = "ai-profile-second" };
        primary.Status = CrawlJobStatus.Completed;
        primary.Outcome = "succeeded";
        await f.Tick();
        await f.Tick();
        Assert.Equal(2, f.Jobs.Count);
        Assert.All(f.Jobs.Values, job => Assert.Equal("ai-profile-first", job.AiProfileId));
    }

    private static ScheduleRequest Request() => new()
    {
        Name = "Nightly", LocalDate = "2026-11-03", LocalTime = "00:00",
        Primary = new() { JobType = "azure-updates" },
        PostJob = new() { JobType = "blast-radius-scan" }
    };

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-11-03T08:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Fixture
    {
        public readonly Clock Clock = new();
        public readonly MemoryStore Store = new();
        public readonly Dictionary<string, CrawlJob> Jobs = [];
        public bool FailNextCreateAcknowledgement;
        private readonly ScheduleCoordinator _coordinator;
        public Fixture(IAiProfileStore? profiles = null)
        {
            var mock = new Mock<ICosmosDbService>();
            mock.Setup(db => db.GetCrawlJobAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string id, CancellationToken _) => Jobs.GetValueOrDefault(id));
            mock.Setup(db => db.CreateCrawlJobAsync(It.IsAny<CrawlJob>(), It.IsAny<CancellationToken>()))
                .Returns((CrawlJob job, CancellationToken _) =>
                {
                    Jobs.Add(job.Id, job);
                    if (FailNextCreateAcknowledgement)
                    {
                        FailNextCreateAcknowledgement = false;
                        throw new HttpRequestException("Acknowledgement lost");
                    }
                    return Task.FromResult(job);
                });
            _coordinator = new(Store, mock.Object, Clock, profiles);
        }
        public Task Tick() => _coordinator.ReconcileAsync("test");
    }

    private sealed class MemoryStore : IScheduleStore
    {
        public JobSchedule Schedule = new()
        {
            ScheduleId = "test", Configuration = Request(), ETag = "1",
            NextDueAt = DateTimeOffset.Parse("2026-11-03T00:00:00Z")
        };
        public Dictionary<string, ScheduleRun> Runs = [];
        private int _version = 1;
        private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<JobSchedule?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<JobSchedule?>(Copy(Schedule));
        public Task CreateAsync(JobSchedule schedule, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> SaveAsync(JobSchedule schedule, ScheduleRun? run = null, bool createRun = false, CancellationToken ct = default)
        {
            if (schedule.ETag != Schedule.ETag || (createRun && run is not null && Runs.ContainsKey(run.Id)))
                return Task.FromResult(false);
            schedule.ETag = (++_version).ToString();
            Schedule = Copy(schedule);
            if (run is not null) Runs[run.Id] = Copy(run);
            return Task.FromResult(true);
        }
        public Task<ScheduleRun?> GetRunAsync(string scheduleId, string runId, CancellationToken ct = default) =>
            Task.FromResult(Runs.TryGetValue(runId, out var run) ? Copy(run) : null);
        public Task<SchedulePage<JobSchedule>> ListAsync(string? continuation, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SchedulePage<ScheduleRun>> RunsAsync(string scheduleId, string? continuation, CancellationToken ct = default) => throw new NotSupportedException();
        public async IAsyncEnumerable<JobSchedule> DueAsync(DateTimeOffset now, [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield return Copy(Schedule);
        }
        public Task<int> SaveBarriersAsync(string id, IReadOnlyList<JobReadBarrier> barriers, CancellationToken ct) => throw new NotSupportedException();
        public Task ApplyBarriersAsync(string id, int pages, CancellationToken ct) => throw new NotSupportedException();
    }
}
