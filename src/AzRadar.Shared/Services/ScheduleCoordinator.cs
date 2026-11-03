using System.Globalization;
using System.Net;
using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Models;
using Microsoft.Azure.Cosmos;

namespace AzRadar.Shared.Services;

public sealed class ScheduleCoordinator(IScheduleStore store, ICosmosDbService jobs, TimeProvider clock,
    IAiProfileStore? profiles = null)
{
    public async Task ReconcileAsync(string scheduleId, CancellationToken ct = default)
    {
        var schedule = await store.GetAsync(scheduleId, ct);
        if (schedule is null) return;
        var now = clock.GetUtcNow();
        if (schedule.ActiveRunId is not null)
        {
            var run = await store.GetRunAsync(scheduleId, schedule.ActiveRunId, ct)
                ?? throw new InvalidOperationException($"Active run {schedule.ActiveRunId} is missing.");
            await ReconcileRunAsync(schedule, run, now, ct);
            return;
        }
        if (!schedule.Enabled || schedule.Deleted || schedule.NextDueAt is null || schedule.NextDueAt > now)
            return;

        var due = schedule.NextDueAt.Value;
        long missed = 0;
        DateTimeOffset? next = ScheduleRecurrence.Next(schedule.Configuration, due);
        while (next.HasValue && next <= now)
        {
            due = next.Value;
            missed++;
            next = ScheduleRecurrence.Next(schedule.Configuration, due);
            if (missed > 100_000) throw new InvalidOperationException("Schedule catch-up range exceeds the supported limit.");
        }
        schedule.MissedOccurrences += missed;
        schedule.NextDueAt = next;
        if (now - due > TimeSpan.FromHours(24))
        {
            schedule.MissedOccurrences++;
            schedule.LastOutcome = "missed";
            ScheduleRecurrence.SetWake(schedule, now);
            await store.SaveAsync(schedule, ct: ct);
            return;
        }
        var runId = ScheduleRecurrence.StableId(scheduleId, schedule.Revision.ToString(CultureInfo.InvariantCulture), due.ToString("O"));
        var occurrence = NewRun(schedule, runId, due, now, "scheduled");
        occurrence.AiProfileSnapshot = profiles is null ? null : (await profiles.GetActiveAsync(ct)).Profile;
        occurrence.AiProfileId = occurrence.AiProfileSnapshot?.Id;
        schedule.ActiveRunId = occurrence.Id;
        schedule.LastOutcome = occurrence.Status;
        schedule.LastError = null;
        ScheduleRecurrence.SetWake(schedule, now);
        await store.SaveAsync(schedule, occurrence, createRun: true, ct);
    }

    public static ScheduleRun NewRun(JobSchedule schedule, string runId, DateTimeOffset due, DateTimeOffset now, string source) =>
        new()
        {
            Id = runId, ScheduleId = schedule.ScheduleId, Revision = schedule.Revision,
            Configuration = schedule.Configuration, ScheduledFor = due, CreatedAt = now, Source = source,
            PrimaryJobId = ScheduleRecurrence.StableId(runId, "primary"),
            PostJobId = schedule.Configuration.PostJob is null ? null : ScheduleRecurrence.StableId(runId, "post")
        };

    private async Task ReconcileRunAsync(JobSchedule schedule, ScheduleRun run, DateTimeOffset now, CancellationToken ct)
    {
        if (run.RetryAt > now)
        {
            ScheduleRecurrence.SetWake(schedule, now);
            await store.SaveAsync(schedule, ct: ct);
            return;
        }
        var post = run.Step == 1;
        var id = post ? run.PostJobId! : run.PrimaryJobId;
        var template = post ? run.Configuration.PostJob! : run.Configuration.Primary;
        var job = await jobs.GetCrawlJobAsync(id, ct);
        if (job is null)
        {
            if (!run.Status.EndsWith("-pending", StringComparison.Ordinal))
            {
                run.Status = "attention-required";
                run.Error = "An acknowledged job disappeared. Automatic recreation is blocked.";
            }
            else
            {
                var requested = new CrawlJob
                {
                    Id = id, JobType = template.JobType, SkipLlmAnalysis = template.SkipLlmAnalysis,
                    ScheduleId = schedule.ScheduleId, ScheduleRunId = run.Id,
                    ParentJobId = post ? run.PrimaryJobId : null, ScheduledFor = run.ScheduledFor,
                    AiProfileId = run.AiProfileId, AiProfileSnapshot = run.AiProfileSnapshot
                };
                try { job = await jobs.CreateCrawlJobAsync(requested, ct); }
                catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
                {
                    job = await jobs.GetCrawlJobAsync(id, ct)
                        ?? throw new InvalidOperationException("Conflicting scheduled job could not be read.");
                }
            }
        }
        if (job is not null)
        {
            if (job.ScheduleId != schedule.ScheduleId || job.ScheduleRunId != run.Id ||
                job.JobType != template.JobType || job.SkipLlmAnalysis != template.SkipLlmAnalysis ||
                job.ParentJobId != (post ? run.PrimaryJobId : null) || job.AiProfileId != run.AiProfileId)
                throw new InvalidOperationException("Scheduled job identity/payload mismatch.");
            run.Status = post ? "post-running" : "primary-running";
            if (job.Status == CrawlJobStatus.Completed && job.Outcome == "succeeded" && string.IsNullOrEmpty(job.Error))
            {
                if (!post && run.PostJobId is not null)
                {
                    run.Status = "post-pending";
                    run.Step = 1;
                    run.ExecutionNumber = 1;
                    run.RetryAt = null;
                }
                else run.Status = "succeeded";
            }
            else if (job.Status is CrawlJobStatus.Failed or CrawlJobStatus.Completed)
            {
                if (job.Status == CrawlJobStatus.Failed && job.Retryable && run.ExecutionNumber < 3)
                {
                    run.ExecutionNumber++;
                    run.RetryAt = now.AddMinutes(run.ExecutionNumber == 2 ? 1 : 5);
                    var retryId = ScheduleRecurrence.StableId(run.Id, post ? "post" : "primary",
                        run.ExecutionNumber.ToString(CultureInfo.InvariantCulture));
                    if (post) run.PostJobId = retryId;
                    else run.PrimaryJobId = retryId;
                    run.Status = post ? "post-pending" : "primary-pending";
                }
                else
                {
                    run.Status = "failed";
                    run.Error = job.Error ?? $"Job ended with outcome '{job.Outcome ?? "unknown"}'; post-job blocked.";
                }
            }
            else if (job.Status == CrawlJobStatus.Processing &&
                     now - (job.LastHeartbeatAt ?? job.StartedAt ?? job.CreatedAt) > TimeSpan.FromMinutes(5))
            {
                run.Status = "attention-required";
                run.Error = "Worker heartbeat is stale. Confirm the old worker has stopped before retrying; post-job blocked.";
            }
        }
        schedule.LastError = run.Error;
        schedule.LastOutcome = run.Status;
        if (run.Status is "succeeded" or "failed")
        {
            run.CompletedAt = now;
            schedule.ActiveRunId = null;
            schedule.LastRunId = run.Id;
        }
        // Consume dates crossed while the chain was active, including its finishing pass.
        while (schedule.NextDueAt.HasValue && schedule.NextDueAt <= now)
        {
            schedule.SkippedOccurrences++;
            schedule.NextDueAt = ScheduleRecurrence.Next(schedule.Configuration, schedule.NextDueAt.Value);
        }
        ScheduleRecurrence.SetWake(schedule, now);
        await store.SaveAsync(schedule, run, ct: ct);
    }
}
