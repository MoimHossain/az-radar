using System.Net;
using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Models;
using AzRadar.Shared.Services;
using Microsoft.Azure.Cosmos;
using Moq;

namespace AzRadar.Shared.Tests;

public class CrawlJobDeletionTests
{
    [Theory]
    [InlineData("completed", "succeeded")]
    [InlineData("failed", "failed")]
    public async Task Finished_scheduled_job_requires_explicit_force_and_keeps_run_history(string jobStatus, string runStatus)
    {
        var f = new Fixture(scheduled: true);
        f.Job.Status = jobStatus;
        f.Run.Status = runStatus;

        var protectedResult = await f.Service.DeleteAsync(f.Job.Id, false);

        Assert.Equal(CrawlJobDeletionStatus.Conflict, protectedResult.Status);
        Assert.True(protectedResult.ForceDeleteAllowed);
        f.Jobs.Verify(x => x.DeleteCrawlJobAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()), Times.Never);

        var confirmedResult = await f.Service.DeleteAsync(f.Job.Id, true);

        Assert.Equal(CrawlJobDeletionStatus.Deleted, confirmedResult.Status);
        f.Jobs.Verify(x => x.DeleteCrawlJobAsync(f.Job.Id, It.IsAny<CancellationToken>(), "version"), Times.Once);
        f.Schedules.Verify(x => x.SaveAsync(It.IsAny<JobSchedule>(), It.IsAny<ScheduleRun?>(),
            It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Force_does_not_remove_a_running_job(bool scheduled)
    {
        var f = new Fixture(scheduled);
        f.Job.Status = CrawlJobStatus.Processing;

        var result = await f.Service.DeleteAsync(f.Job.Id, true);

        Assert.Equal(CrawlJobDeletionStatus.Conflict, result.Status);
        Assert.False(result.ForceDeleteAllowed);
        f.Jobs.Verify(x => x.DeleteCrawlJobAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()), Times.Never);
    }

    [Theory]
    [InlineData("primary-pending")]
    [InlineData("post-running")]
    [InlineData("attention-required")]
    public async Task Finished_job_is_retained_until_its_entire_scheduled_run_finishes(string runStatus)
    {
        var f = new Fixture(scheduled: true);
        f.Run.Status = runStatus;
        f.Run.CompletedAt = null;

        var result = await f.Service.DeleteAsync(f.Job.Id, true);

        Assert.Equal(CrawlJobDeletionStatus.Conflict, result.Status);
        Assert.False(result.ForceDeleteAllowed);
        f.Jobs.Verify(x => x.DeleteCrawlJobAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task Missing_run_history_never_bypasses_scheduled_job_protection()
    {
        var f = new Fixture(scheduled: true);
        f.Schedules.Setup(x => x.GetRunAsync("schedule", "run", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScheduleRun?)null);

        var result = await f.Service.DeleteAsync(f.Job.Id, true);

        Assert.Equal(CrawlJobDeletionStatus.Conflict, result.Status);
        Assert.False(result.ForceDeleteAllowed);
        f.Jobs.Verify(x => x.DeleteCrawlJobAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task Pending_manual_job_requires_confirmation_before_removal()
    {
        var f = new Fixture();
        f.Job.Status = CrawlJobStatus.Pending;

        var initial = await f.Service.DeleteAsync(f.Job.Id, false);
        Assert.True(initial.ForceDeleteAllowed);
        Assert.Equal(CrawlJobDeletionStatus.Conflict, initial.Status);

        Assert.Equal(CrawlJobDeletionStatus.Deleted, (await f.Service.DeleteAsync(f.Job.Id, true)).Status);
        f.Jobs.Verify(x => x.DeleteCrawlJobAsync(f.Job.Id, It.IsAny<CancellationToken>(), "version"), Times.Once);
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("failed")]
    public async Task Finished_manual_jobs_keep_normal_delete_behavior(string status)
    {
        var f = new Fixture();
        f.Job.Status = status;

        Assert.Equal(CrawlJobDeletionStatus.Deleted, (await f.Service.DeleteAsync(f.Job.Id, false)).Status);
        f.Schedules.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Concurrent_claim_or_update_prevents_deletion_of_the_changed_job()
    {
        var f = new Fixture();
        f.Job.Status = CrawlJobStatus.Pending;
        f.Jobs.Setup(x => x.DeleteCrawlJobAsync(f.Job.Id, It.IsAny<CancellationToken>(), "version"))
            .ThrowsAsync(new CosmosException("Changed", HttpStatusCode.PreconditionFailed, 0, "", 0));

        var result = await f.Service.DeleteAsync(f.Job.Id, true);

        Assert.Equal(CrawlJobDeletionStatus.Conflict, result.Status);
        Assert.False(result.ForceDeleteAllowed);
        Assert.Contains("changed", result.Error);
    }

    [Fact]
    public async Task Missing_job_returns_not_found_without_deleting()
    {
        var f = new Fixture();
        f.Jobs.Setup(x => x.GetCrawlJobAsync(f.Job.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CrawlJob?)null);

        Assert.Equal(CrawlJobDeletionStatus.NotFound, (await f.Service.DeleteAsync(f.Job.Id, true)).Status);
        f.Jobs.Verify(x => x.DeleteCrawlJobAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()), Times.Never);
    }

    private sealed class Fixture
    {
        public CrawlJob Job { get; } = new() { Id = "job", Status = CrawlJobStatus.Completed, ETag = "version" };
        public ScheduleRun Run { get; } = new() { Id = "run", ScheduleId = "schedule", Status = "succeeded", CompletedAt = DateTimeOffset.UtcNow };
        public Mock<ICosmosDbService> Jobs { get; } = new();
        public Mock<IScheduleStore> Schedules { get; } = new();
        public CrawlJobDeletionService Service { get; }

        public Fixture(bool scheduled = false)
        {
            if (scheduled) { Job.ScheduleId = "schedule"; Job.ScheduleRunId = "run"; }
            Jobs.Setup(x => x.GetCrawlJobAsync("job", It.IsAny<CancellationToken>())).ReturnsAsync(Job);
            Jobs.Setup(x => x.DeleteCrawlJobAsync("job", It.IsAny<CancellationToken>(), "version")).ReturnsAsync(true);
            Schedules.Setup(x => x.GetRunAsync("schedule", "run", It.IsAny<CancellationToken>())).ReturnsAsync(Run);
            Service = new(Jobs.Object, Schedules.Object);
        }
    }
}
