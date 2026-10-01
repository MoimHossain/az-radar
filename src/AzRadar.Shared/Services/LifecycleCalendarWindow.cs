using System.Globalization;

namespace AzRadar.Shared.Services;

/// <summary>Deadline window for the lifecycle calendar (inclusive yyyy-MM-dd bounds).</summary>
public sealed record LifecycleCalendarWindow(string From, string To)
{
    public const int DefaultPastDays = 90;
    public const int DefaultFutureMonths = 60;
    public const int MaxSpanYears = 10;

    public static bool TryCreate(
        string? from, string? to, DateOnly today, out LifecycleCalendarWindow window, out string? error)
    {
        window = new LifecycleCalendarWindow(string.Empty, string.Empty);
        error = null;

        var start = today.AddDays(-DefaultPastDays);
        var end = today.AddMonths(DefaultFutureMonths);
        if (!string.IsNullOrWhiteSpace(from) && !TryParse(from, out start))
        {
            error = "'from' must be a date in yyyy-MM-dd format.";
            return false;
        }
        if (!string.IsNullOrWhiteSpace(to) && !TryParse(to, out end))
        {
            error = "'to' must be a date in yyyy-MM-dd format.";
            return false;
        }
        if (end < start)
        {
            error = "'to' must not be earlier than 'from'.";
            return false;
        }
        if (end > start.AddYears(MaxSpanYears))
        {
            error = $"The calendar window cannot exceed {MaxSpanYears} years.";
            return false;
        }

        window = new LifecycleCalendarWindow(Format(start), Format(end));
        return true;
    }

    private static bool TryParse(string value, out DateOnly date) =>
        DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
