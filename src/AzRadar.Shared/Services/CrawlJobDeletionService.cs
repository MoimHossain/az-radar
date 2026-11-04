using System.Net;
using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Models;
using Microsoft.Azure.Cosmos;

namespace AzRadar.Shared.Services;

public sealed class CrawlJobDeletionService(ICosmosDbService jobs, IScheduleStore schedules)
{
    public async Task<CrawlJobDeletionResult> DeleteAsync(string id, bool force, CancellationToken ct = default)
    {
        var job = await jobs.GetCrawlJobAsync(id, ct);
        if (job is null)
            return new(CrawlJobDeletionStatus.NotFound);

        if (job.Status == CrawlJobStatus.Processing)
            return new(CrawlJobDeletionStatus.Conflict,
                "This job is running and cannot be deleted safely. Wait for it to finish before deleting it.");

        if (job.ScheduleId is not null)
        {
            var run = job.ScheduleRunId is null ? null :
                await schedules.GetRunAsync(job.ScheduleId, job.ScheduleRunId, ct);
            if (run is null)
                return new(CrawlJobDeletionStatus.Conflict,
                    "The scheduled run history is unavailable, so this job cannot be safely deleted.");
            if (job.Status is not (CrawlJobStatus.Completed or CrawlJobStatus.Failed) ||
                run.Status is not ("succeeded" or "failed") || run.CompletedAt is null)
                return new(CrawlJobDeletionStatus.Conflict,
                    "This job belongs to a scheduled run that has not finished. Wait for the entire run, including any post-job, to finish before deleting it.");
            if (!force)
                return new(CrawlJobDeletionStatus.Conflict,
                    "This job is retained as part of a finished scheduled run. Force delete removes the job record, but keeps the schedule, run history, diagnostics, and generated items.",
                    ForceDeleteAllowed: true);
        }
        else if (job.Status == CrawlJobStatus.Pending && !force)
            return new(CrawlJobDeletionStatus.Conflict,
                "This job is waiting to run. Force delete removes it from the queue. Generated items and diagnostics are kept.",
                ForceDeleteAllowed: true);

        if (string.IsNullOrEmpty(job.ETag))
            return new(CrawlJobDeletionStatus.Conflict, "The job version is unavailable. Refresh and try again.");
        try
        {
            return new(await jobs.DeleteCrawlJobAsync(id, ct, job.ETag)
                ? CrawlJobDeletionStatus.Deleted : CrawlJobDeletionStatus.NotFound);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            return new(CrawlJobDeletionStatus.Conflict,
                "This job changed while deletion was being confirmed. Refresh and try again.");
        }
    }
}

public enum CrawlJobDeletionStatus { Deleted, NotFound, Conflict }
public record CrawlJobDeletionResult(CrawlJobDeletionStatus Status, string? Error = null, bool ForceDeleteAllowed = false);
