using System.Globalization;
using System.Text.RegularExpressions;
using AzRadar.Shared.Models;

namespace AzRadar.Shared.Services;

/// <summary>
/// Turns the free-form LLM deadline into a queryable yyyy-MM-dd value and recovers deadlines
/// for lifecycle changes whose analysis returned none (e.g. "will be retired on 30 September 2028").
/// </summary>
public static partial class LifecycleDeadlineResolver
{
    /// <summary>Bump when resolution rules change so stored documents are re-stamped.</summary>
    public const int Version = 1;

    public const string SourceLlm = "llm";
    public const string SourceExtracted = "extracted";

    public static readonly string[] LifecycleChangeTypes =
    [
        ChangeTypes.Retirement,
        ChangeTypes.Deprecation,
        ChangeTypes.BreakingChange,
        ChangeTypes.MigrationRequired,
    ];

    private static readonly string[] ExactFormats =
    [
        "yyyy-MM-dd", "yyyy-M-d", "yyyy/MM/dd", "MM/dd/yyyy", "M/d/yyyy",
        "MMMM d, yyyy", "MMMM d yyyy", "MMM d, yyyy", "MMM d yyyy",
        "d MMMM yyyy", "d MMMM, yyyy", "d MMM yyyy", "dd MMMM yyyy",
    ];

    private static readonly Dictionary<string, int> Months = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jan"] = 1, ["january"] = 1, ["feb"] = 2, ["february"] = 2, ["mar"] = 3, ["march"] = 3,
        ["apr"] = 4, ["april"] = 4, ["may"] = 5, ["jun"] = 6, ["june"] = 6, ["jul"] = 7, ["july"] = 7,
        ["aug"] = 8, ["august"] = 8, ["sep"] = 9, ["sept"] = 9, ["september"] = 9, ["oct"] = 10,
        ["october"] = 10, ["nov"] = 11, ["november"] = 11, ["dec"] = 12, ["december"] = 12,
    };

    private const string MonthPattern =
        "(?<mon>jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|may|june?|july?|aug(?:ust)?|sept?(?:ember)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?)";

    [GeneratedRegex(@"\b" + MonthPattern + @"\.?\s+(?<day>\d{1,2})(?:st|nd|rd|th)?,?\s+(?<year>20\d{2})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MonthDayYear();

    [GeneratedRegex(@"\b(?<day>\d{1,2})(?:st|nd|rd|th)?\s+" + MonthPattern + @"\.?,?\s+(?<year>20\d{2})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DayMonthYear();

    [GeneratedRegex(@"\b(?<year>20\d{2})-(?<month>\d{1,2})-(?<day>\d{1,2})\b", RegexOptions.CultureInvariant)]
    private static partial Regex IsoDate();

    [GeneratedRegex(@"\b(?<month>\d{1,2})/(?<day>\d{1,2})/(?<year>20\d{2})\b", RegexOptions.CultureInvariant)]
    private static partial Regex UsDate();

    [GeneratedRegex(@"retir|deprecat|end of (?:support|life)|sunset|will no longer be supported",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LifecycleWording();

    /// <summary>Normalizes an LLM deadline value to yyyy-MM-dd, or null when it is not a usable date.</summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var value = raw.Trim();
        if (value.Length >= 10 && DateOnly.TryParseExact(value[..10], "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso))
            return Format(iso);
        if (DateOnly.TryParseExact(value, ExactFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var exact))
            return Format(exact);
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var parsed))
            return Format(DateOnly.FromDateTime(parsed.UtcDateTime));
        return ExtractFromText(value);
    }

    /// <summary>Returns the first full calendar date mentioned in the text.</summary>
    public static string? ExtractFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var candidates = new List<(int Index, DateOnly Date)>();
        foreach (Match m in MonthDayYear().Matches(text))
            Add(m.Index, Months[m.Groups["mon"].Value.ToLowerInvariant()], m.Groups["day"].Value, m.Groups["year"].Value);
        foreach (Match m in DayMonthYear().Matches(text))
            Add(m.Index, Months[m.Groups["mon"].Value.ToLowerInvariant()], m.Groups["day"].Value, m.Groups["year"].Value);
        foreach (Match m in IsoDate().Matches(text))
            Add(m.Index, int.Parse(m.Groups["month"].Value, CultureInfo.InvariantCulture), m.Groups["day"].Value, m.Groups["year"].Value);
        foreach (Match m in UsDate().Matches(text))
            Add(m.Index, int.Parse(m.Groups["month"].Value, CultureInfo.InvariantCulture), m.Groups["day"].Value, m.Groups["year"].Value);

        return candidates.Count == 0 ? null : Format(candidates.MinBy(c => c.Index).Date);

        void Add(int index, int month, string day, string year)
        {
            var d = int.Parse(day, CultureInfo.InvariantCulture);
            var y = int.Parse(year, CultureInfo.InvariantCulture);
            if (month is < 1 or > 12 || d < 1 || d > DateTime.DaysInMonth(y, month))
                return;
            candidates.Add((index, new DateOnly(y, month, d)));
        }
    }

    public static bool IsLifecycleChange(LlmAnalysis? analysis, string? title) =>
        (analysis != null && LifecycleChangeTypes.Contains(analysis.ChangeType, StringComparer.OrdinalIgnoreCase)) ||
        (!string.IsNullOrWhiteSpace(title) && LifecycleWording().IsMatch(title));

    public static (string? Deadline, string? Source) Resolve(LlmAnalysis? analysis, string? title, string? summary)
    {
        if (analysis == null)
            return (null, null);

        var normalized = Normalize(analysis.Deadline);
        if (normalized != null)
            return (normalized, SourceLlm);

        if (!IsLifecycleChange(analysis, title))
            return (null, null);

        var extracted = ExtractFromText(title) ?? ExtractFromText(summary);
        return extracted != null ? (extracted, SourceExtracted) : (null, null);
    }

    public static void Stamp(FeedItem item)
    {
        var (deadline, source) = Resolve(item.LlmAnalysis, item.Title, item.Summary);
        item.LifecycleDeadline = deadline;
        item.DeadlineSource = source;
        item.DeadlineResolverVersion = Version;
    }

    public static void Stamp(DocInsight insight)
    {
        var (deadline, source) = Resolve(insight.LlmAnalysis, insight.Title, insight.Snippet);
        insight.LifecycleDeadline = deadline;
        insight.DeadlineSource = source;
        insight.DeadlineResolverVersion = Version;
    }

    private static string Format(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
