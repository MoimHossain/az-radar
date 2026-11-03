namespace AzRadar.Shared.Models;

public class ScheduledJobTemplate
{
    public string JobType { get; set; } = "";
    public bool SkipLlmAnalysis { get; set; }
}

public class ScheduleRequest
{
    public string Name { get; set; } = "";
    public string Frequency { get; set; } = "once";
    public string TimeZone { get; set; } = "UTC";
    public string LocalDate { get; set; } = "";
    public string LocalTime { get; set; } = "00:00";
    public List<int> Weekdays { get; set; } = [];
    public ScheduledJobTemplate Primary { get; set; } = new();
    public ScheduledJobTemplate? PostJob { get; set; }
}

public class JobSchedule
{
    public string Id { get; set; } = "definition";
    public string ScheduleId { get; set; } = "";
    public string Kind { get; set; } = "schedule";
    public int Revision { get; set; } = 1;
    public ScheduleRequest Configuration { get; set; } = new();
    public bool Enabled { get; set; } = true;
    public bool Deleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? NextDueAt { get; set; }
    public long WakeAt { get; set; }
    public string? ActiveRunId { get; set; }
    public string? LastRunId { get; set; }
    public string? LastOutcome { get; set; }
    public string? LastError { get; set; }
    public long SkippedOccurrences { get; set; }
    public long MissedOccurrences { get; set; }
    public string RequestFingerprint { get; set; } = "";
    [Newtonsoft.Json.JsonProperty("_etag")]
    public string? ETag { get; set; }
}

public class ScheduleRun
{
    public string Id { get; set; } = "";
    public string ScheduleId { get; set; } = "";
    public string Kind { get; set; } = "run";
    public int Revision { get; set; }
    public ScheduleRequest Configuration { get; set; } = new();
    public string Source { get; set; } = "scheduled";
    public DateTimeOffset ScheduledFor { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string Status { get; set; } = "primary-pending";
    public string PrimaryJobId { get; set; } = "";
    public string? PostJobId { get; set; }
    public string? Error { get; set; }
    public int ExecutionNumber { get; set; } = 1;
    public int Step { get; set; }
    public DateTimeOffset? RetryAt { get; set; }
    public string? RetryOfRunId { get; set; }
    [Newtonsoft.Json.JsonProperty("_etag")]
    public string? ETag { get; set; }
    public int Ttl { get; set; } = -1;
}

public record SchedulePage<T>(IReadOnlyList<T> Items, string? ContinuationToken);
