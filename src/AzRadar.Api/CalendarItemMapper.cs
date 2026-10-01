using AzRadar.Shared.Models;
using AzRadar.Shared.Services;

namespace AzRadar.Api;

public sealed record CalendarItemDto(
    string Id,
    string Title,
    string Link,
    string? Deadline,
    string? DeadlineSource,
    DateTimeOffset? PublishDate,
    string ChangeType,
    string Severity,
    List<string> AffectedServices,
    string ActionRequired,
    string Source,
    string BriefSummary);

public static class CalendarItemMapper
{
    public static CalendarItemDto FromFeedItem(FeedItem item)
    {
        var (deadline, source) = EffectiveDeadline(item);
        var analysis = item.LlmAnalysis ?? new LlmAnalysis();
        return new CalendarItemDto(
            item.Id, item.Title, item.Link, deadline, source, item.PublishDate,
            analysis.ChangeType, analysis.Severity, analysis.AffectedServices, analysis.ActionRequired,
            CrawlJobTypes.AzureUpdates, analysis.BriefSummary);
    }

    public static CalendarItemDto FromDocInsight(DocInsight insight)
    {
        var (deadline, source) = EffectiveDeadline(insight);
        var analysis = insight.LlmAnalysis ?? new LlmAnalysis();
        return new CalendarItemDto(
            insight.Id, insight.Title, insight.DocUrl, deadline, source, insight.LastAnalyzedAt,
            analysis.ChangeType, analysis.Severity, analysis.AffectedServices, analysis.ActionRequired,
            "ms-learn", analysis.BriefSummary);
    }

    public static (string? Deadline, string? Source) EffectiveDeadline(FeedItem item) =>
        item.DeadlineResolverVersion is not null
            ? (item.LifecycleDeadline, item.DeadlineSource)
            : LifecycleDeadlineResolver.Resolve(item.LlmAnalysis, item.Title, item.Summary);

    public static (string? Deadline, string? Source) EffectiveDeadline(DocInsight insight) =>
        insight.DeadlineResolverVersion is not null
            ? (insight.LifecycleDeadline, insight.DeadlineSource)
            : LifecycleDeadlineResolver.Resolve(insight.LlmAnalysis, insight.Title, insight.Snippet);
}
