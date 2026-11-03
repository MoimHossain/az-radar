using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AzRadar.Shared.Models;

namespace AzRadar.Shared.Services;

public static class ScheduleRecurrence
{
    public static readonly string[] JobTypes =
    [
        CrawlJobTypes.AzureUpdates, CrawlJobTypes.MsLearnIntelligence,
        CrawlJobTypes.GitHubCrawl, CrawlJobTypes.BlastRadiusScan
    ];

    public static string? Validate(ScheduleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 120)
            return "A schedule name of 1-120 characters is required.";
        if (request.Frequency is not ("once" or "daily" or "weekly"))
            return "Frequency must be once, daily, or weekly.";
        if (!DateOnly.TryParseExact(request.LocalDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _) ||
            !TimeOnly.TryParseExact(request.LocalTime, "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _))
            return "A valid local date (yyyy-MM-dd) and time (HH:mm) are required.";
        if (string.IsNullOrWhiteSpace(request.TimeZone) || request.TimeZone.Length > 100)
            return "A valid time zone is required.";
        try { TimeZoneInfo.FindSystemTimeZoneById(request.TimeZone); }
        catch (TimeZoneNotFoundException) { return "Unknown time zone."; }
        catch (InvalidTimeZoneException) { return "Invalid time zone."; }
        if (request.Weekdays is null || request.Weekdays.Any(d => d < 0 || d > 6) ||
            (request.Frequency == "weekly" && request.Weekdays.Count == 0))
            return "Select at least one valid weekday (Sunday=0).";
        return ValidateTemplate(request.Primary) ?? (request.PostJob is null ? null : ValidateTemplate(request.PostJob));
    }

    public static string? ValidateTemplate(ScheduledJobTemplate? template)
    {
        if (template is null || !JobTypes.Contains(template.JobType))
            return "Select a supported job type.";
        return template.SkipLlmAnalysis && template.JobType != CrawlJobTypes.AzureUpdates
            ? "Skipping LLM analysis is supported only for Azure Updates."
            : null;
    }

    public static DateTimeOffset? Next(ScheduleRequest request, DateTimeOffset after)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(request.TimeZone);
        var start = DateOnly.ParseExact(request.LocalDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var time = TimeOnly.ParseExact(request.LocalTime, "HH:mm", CultureInfo.InvariantCulture);
        var localAfter = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(after, zone).DateTime);
        var date = request.Frequency == "once" || start > localAfter ? start : localAfter;
        for (var i = 0; i < 9; i++)
        {
            if (request.Frequency != "weekly" || request.Weekdays.Contains((int)date.DayOfWeek))
            {
                var instant = Resolve(date.ToDateTime(time, DateTimeKind.Unspecified), zone);
                if (instant > after) return instant;
            }
            if (request.Frequency == "once" || date == DateOnly.MaxValue) return null;
            date = date.AddDays(1);
        }
        return null;
    }

    internal static DateTimeOffset Resolve(DateTime local, TimeZoneInfo zone)
    {
        while (zone.IsInvalidTime(local))
            local = local.AddMinutes(1);
        var offset = zone.IsAmbiguousTime(local)
            ? zone.GetAmbiguousTimeOffsets(local).Max()
            : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    public static string StableId(params string[] parts) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", parts)))).ToLowerInvariant();

    public static void SetWake(JobSchedule schedule, DateTimeOffset now)
    {
        schedule.WakeAt = schedule.ActiveRunId is not null
            ? now.AddSeconds(30).ToUnixTimeMilliseconds()
            : schedule.Enabled && !schedule.Deleted && schedule.NextDueAt.HasValue
                ? schedule.NextDueAt.Value.ToUnixTimeMilliseconds()
                : long.MaxValue;
    }
}
