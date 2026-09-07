using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Models;
using AzRadar.Shared.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AzRadar.Shared.Tests;

public class JobHeartbeatTests
{
    [Fact]
    public async Task KeepsPulsingDuringLongAwaitAndStopsAfterCompletion()
    {
        var db = new Mock<ICosmosDbService>();
        var pulses = 0;
        var thirdPulse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        db.Setup(x => x.HeartbeatCrawlJobAsync("job", 1, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (Interlocked.Increment(ref pulses) == 3) thirdPulse.TrySetResult();
                return Task.CompletedTask;
            });
        var runner = new JobHeartbeatRunner(db.Object, NullLogger<JobHeartbeatRunner>.Instance);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await runner.RunAsync(new CrawlJob { Id = "job", AttemptCount = 1 },
            ct => thirdPulse.Task.WaitAsync(ct), TimeSpan.FromMilliseconds(10), timeout.Token);

        pulses.Should().BeGreaterThanOrEqualTo(3);
        var finishedCount = pulses;
        await Task.Delay(50);
        pulses.Should().Be(finishedCount);
    }

    [Fact]
    public async Task HeartbeatFailureCancelsWorkAndPropagates()
    {
        var db = new Mock<ICosmosDbService>();
        db.SetupSequence(x => x.HeartbeatCrawlJobAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .ThrowsAsync(new InvalidOperationException("Heartbeat write failed"));
        var runner = new JobHeartbeatRunner(db.Object, NullLogger<JobHeartbeatRunner>.Instance);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var workStopped = false;
        async Task Work(CancellationToken ct)
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            finally { workStopped = true; }
        }

        var act = () => runner.RunAsync(new CrawlJob(), Work, TimeSpan.FromMilliseconds(10), timeout.Token);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Heartbeat write failed");
        workStopped.Should().BeTrue();
    }

    [Fact]
    public async Task HandlerFailurePropagatesAndStopsHeartbeat()
    {
        var db = new Mock<ICosmosDbService>();
        var runner = new JobHeartbeatRunner(db.Object, NullLogger<JobHeartbeatRunner>.Instance);
        var act = () => runner.RunAsync(new CrawlJob(),
            _ => Task.FromException(new InvalidOperationException("Handler failed")), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Handler failed");
        db.Verify(x => x.HeartbeatCrawlJobAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(CrawlJobStatus.Processing, 1, false)]
    [InlineData(CrawlJobStatus.Processing, 5, true)]
    [InlineData(CrawlJobStatus.Completed, 5, false)]
    [InlineData(CrawlJobStatus.Failed, 5, false)]
    [InlineData(CrawlJobStatus.Pending, 5, false)]
    public void StalenessDependsOnHeartbeatNotJobAge(string status, int heartbeatAgeMinutes, bool stale)
    {
        var job = new CrawlJob
        {
            Status = status, CreatedAt = DateTimeOffset.UtcNow.AddDays(-3),
            LastHeartbeatAt = DateTimeOffset.UtcNow.AddMinutes(-heartbeatAgeMinutes)
        };

        job.IsStale.Should().Be(stale);
    }

    [Fact]
    public void LegacyJobWithoutHeartbeat_IsUnknownNotAssumedStale()
    {
        new CrawlJob { Status = CrawlJobStatus.Processing, CreatedAt = DateTimeOffset.UtcNow.AddDays(-3) }
            .IsStale.Should().BeFalse();
    }
}
