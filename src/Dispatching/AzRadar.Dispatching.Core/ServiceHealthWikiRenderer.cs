using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AzRadar.Shared.Models;

namespace AzRadar.Dispatching.Core;

public sealed class ServiceHealthWikiRenderer
{
    private static readonly Regex AnchorPattern = new(
        """<a\b[^>]*\bhref\s*=\s*["'](?<href>[^"']+)["'][^>]*>(?<text>.*?)</a>""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex TagPattern = new(
        "<[^>]+>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex ExcessBlankLinesPattern = new(
        @"\n{3,}",
        RegexOptions.Compiled);

    public string Render(
        IReadOnlyList<ServiceHealthEvent> sourceEvents,
        DateTimeOffset renderedAt)
    {
        var events = sourceEvents
            .GroupBy(
                item => string.IsNullOrWhiteSpace(item.TrackingId) ? item.Id : item.TrackingId,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(item => item.EventTimestamp)
                .ThenByDescending(item => item.ReceivedAt)
                .First())
            .OrderByDescending(RequiresAction)
            .ThenBy(item => EventTypeOrder(item.EventType))
            .ThenByDescending(item => item.EventTimestamp)
            .ThenBy(item => item.TrackingId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var active = events.Where(item => !IsResolved(item)).ToList();
        var recentlyResolved = events
            .Where(IsResolved)
            .Where(item => item.EventTimestamp >= renderedAt.AddDays(-30))
            .OrderByDescending(item => item.EventTimestamp)
            .ToList();
        var serviceIssues = active.Where(item => item.EventType == ServiceHealthEventTypes.ServiceIssue).ToList();
        var maintenance = active.Where(item => item.EventType == ServiceHealthEventTypes.PlannedMaintenance).ToList();
        var healthAdvisories = active.Where(item => item.EventType == ServiceHealthEventTypes.HealthAdvisory).ToList();
        var securityAdvisories = active.Where(item => item.EventType == ServiceHealthEventTypes.SecurityAdvisory).ToList();
        var actionItems = active.Where(RequiresAction).ToList();
        var overall = serviceIssues.Count > 0 || securityAdvisories.Any(RequiresAction)
            ? ("🔴 Action Required", "Review active service issues and follow recommended actions.")
            : maintenance.Count > 0 || healthAdvisories.Count > 0 || securityAdvisories.Count > 0
                ? ("🟡 Awareness Required", "Review active advisories and upcoming maintenance.")
                : ("🟢 Normal Operations", "No action required. Continue normal operations.");

        var builder = new StringBuilder();
        AppendBrandHeader(builder);
        builder.AppendLine("# CloudLens | Azure Service Health");
        builder.AppendLine();
        builder.AppendLine("> **EXECUTIVE OPERATIONS DASHBOARD**");
        builder.AppendLine(">");
        builder.AppendLine($"> {overall.Item1} — **{overall.Item2}**");
        builder.AppendLine(">");
        builder.AppendLine($"> _Refreshed {renderedAt:dd MMM yyyy, HH:mm 'UTC'} · CloudLens continuously reconciles Microsoft Azure Service Health signals._");
        builder.AppendLine();
        builder.AppendLine("## At-a-Glance");
        builder.AppendLine();
        builder.AppendLine("| 🚨 Service Issues | 🛠️ Planned Maintenance | ⚠️ Health Advisories | 🔐 Security Advisories |");
        builder.AppendLine("|:---:|:---:|:---:|:---:|");
        builder.AppendLine(
            $"| **{serviceIssues.Count}**<br>{(serviceIssues.Count == 0 ? "Healthy" : "Active")} | " +
            $"**{maintenance.Count}**<br>{(maintenance.Count == 0 ? "None" : "Upcoming")} | " +
            $"**{healthAdvisories.Count}**<br>{(healthAdvisories.Count == 0 ? "None" : "Active")} | " +
            $"**{securityAdvisories.Count}**<br>{(securityAdvisories.Count == 0 ? "None" : securityAdvisories.Any(RequiresAction) ? "Action required" : "Review")} |");
        builder.AppendLine();
        builder.AppendLine("## Executive Brief");
        builder.AppendLine();
        builder.AppendLine("| Decision Lens | Current Position |");
        builder.AppendLine("|:---|:---|");
        builder.AppendLine($"| **Executive status** | {overall.Item1} |");
        builder.AppendLine($"| **Items requiring action** | **{actionItems.Count}** of {active.Count} active notifications |");
        builder.AppendLine($"| **Top priority** | {(actionItems.Count == 0 ? "No immediate intervention required" : Title(actionItems[0]))} |");
        builder.AppendLine($"| **Services in view** | {CountDistinctServices(active)} |");
        builder.AppendLine($"| **Geographic scope** | {CountDistinctRegions(active)} |");
        builder.AppendLine();
        AppendPriorityActionQueue(builder, actionItems);
        builder.AppendLine();
        AppendServiceIssues(builder, serviceIssues);
        AppendMaintenance(builder, maintenance);
        AppendHealthAdvisories(builder, healthAdvisories);
        AppendSecurityAdvisories(builder, securityAdvisories);
        AppendResolved(builder, recentlyResolved);
        AppendStaticGuidance(builder);
        return builder.ToString();
    }

    private static void AppendServiceIssues(
        StringBuilder builder,
        IReadOnlyList<ServiceHealthEvent> events)
    {
        builder.AppendLine("# 🔴 Active Service Health Events");
        builder.AppendLine();
        if (events.Count == 0)
        {
            builder.AppendLine("_No active service issues._");
            builder.AppendLine();
            builder.AppendLine("---");
            builder.AppendLine();
            return;
        }

        foreach (var item in events)
        {
            builder.AppendLine($"## 🔴 {Title(item)}");
            builder.AppendLine();
            AppendPropertyTable(builder, item, "Service Issue");
            builder.AppendLine("> **Executive impact**");
            builder.AppendLine(">");
            builder.AppendLine($"> {BusinessImpact(item)}");
            builder.AppendLine();
            builder.AppendLine("### Microsoft Update");
            builder.AppendLine(SourceSummary(item));
            builder.AppendLine();
            builder.AppendLine("### Recommended Response");
            AppendActions(builder, item);
            builder.AppendLine();
            builder.AppendLine("---");
            builder.AppendLine();
        }
    }

    private static void AppendMaintenance(
        StringBuilder builder,
        IReadOnlyList<ServiceHealthEvent> events)
    {
        builder.AppendLine("# 🟡 Upcoming Planned Maintenance");
        builder.AppendLine();
        if (events.Count == 0)
        {
            builder.AppendLine("_No upcoming planned maintenance._");
            builder.AppendLine();
            builder.AppendLine("---");
            builder.AppendLine();
            return;
        }

        foreach (var item in events)
        {
            builder.AppendLine($"## 🟡 {Title(item)}");
            builder.AppendLine();
            builder.AppendLine("| Property | Value |");
            builder.AppendLine("|----------|-------|");
            builder.AppendLine($"| Azure Service | {Service(item)} |");
            builder.AppendLine($"| Region(s) | {Regions(item)} |");
            builder.AppendLine($"| Scheduled Date | {FormatDate(item.EventTimestamp)} |");
            builder.AppendLine("| Maintenance Window | Not specified by Microsoft |");
            builder.AppendLine($"| Status | {Value(item.Status, "Scheduled")} |");
            builder.AppendLine();
            builder.AppendLine("> **Readiness impact**");
            builder.AppendLine(">");
            builder.AppendLine($"> {BusinessImpact(item)}");
            builder.AppendLine();
            builder.AppendLine("### Microsoft Update");
            builder.AppendLine(SourceSummary(item));
            builder.AppendLine();
            builder.AppendLine("### Preparation Checklist");
            AppendActions(builder, item, [
                "Review critical workloads.",
                "Avoid major deployments during the maintenance window.",
                "Inform application owners and stakeholders.",
                "Validate contingency procedures if applicable."
            ]);
            builder.AppendLine();
            builder.AppendLine("---");
            builder.AppendLine();
        }
    }

    private static void AppendHealthAdvisories(
        StringBuilder builder,
        IReadOnlyList<ServiceHealthEvent> events)
    {
        builder.AppendLine("# 🟠 Active Health Advisories");
        builder.AppendLine();
        if (events.Count == 0)
        {
            builder.AppendLine("_No active health advisories._");
            builder.AppendLine();
            builder.AppendLine("---");
            builder.AppendLine();
            return;
        }

        foreach (var item in events)
        {
            builder.AppendLine($"## 🟠 {Title(item)}");
            builder.AppendLine();
            builder.AppendLine("| Property | Value |");
            builder.AppendLine("|----------|-------|");
            builder.AppendLine($"| Azure Service | {Service(item)} |");
            builder.AppendLine($"| Tracking ID | {Escape(item.TrackingId)} |");
            builder.AppendLine($"| Published | {FormatDate(item.EventTimestamp)} |");
            builder.AppendLine($"| Status | {Value(item.Status, "Active")} |");
            builder.AppendLine();
            builder.AppendLine("> **Business relevance**");
            builder.AppendLine(">");
            builder.AppendLine($"> {BusinessImpact(item)}");
            builder.AppendLine();
            builder.AppendLine("### Microsoft Update");
            builder.AppendLine(SourceSummary(item));
            builder.AppendLine();
            builder.AppendLine("### Recommended Response");
            AppendActions(builder, item);
            builder.AppendLine();
            builder.AppendLine("---");
            builder.AppendLine();
        }
    }

    private static void AppendSecurityAdvisories(
        StringBuilder builder,
        IReadOnlyList<ServiceHealthEvent> events)
    {
        builder.AppendLine("# 🔐 Security Advisories");
        builder.AppendLine();
        if (events.Count == 0)
        {
            builder.AppendLine("_No active security advisories._");
            builder.AppendLine();
            builder.AppendLine("---");
            builder.AppendLine();
            return;
        }

        foreach (var item in events)
        {
            builder.AppendLine($"## 🔐 {Title(item)}");
            builder.AppendLine();
            builder.AppendLine("| Property | Value |");
            builder.AppendLine("|----------|-------|");
            builder.AppendLine($"| Severity | {Severity(item)} |");
            builder.AppendLine($"| Azure Service | {Service(item)} |");
            builder.AppendLine($"| Tracking ID | {Escape(item.TrackingId)} |");
            builder.AppendLine($"| Last Updated | {FormatDate(item.EventTimestamp)} |");
            builder.AppendLine();
            builder.AppendLine("> **Risk statement**");
            builder.AppendLine(">");
            builder.AppendLine($"> {BusinessImpact(item)}");
            builder.AppendLine();
            builder.AppendLine("### Microsoft Update");
            builder.AppendLine(SourceSummary(item));
            builder.AppendLine();
            builder.AppendLine("### Recommended Response");
            AppendActions(builder, item);
            builder.AppendLine();
            builder.AppendLine("---");
            builder.AppendLine();
        }
    }

    private static void AppendResolved(
        StringBuilder builder,
        IReadOnlyList<ServiceHealthEvent> events)
    {
        builder.AppendLine("# ✅ Recently Resolved Events");
        builder.AppendLine();
        if (events.Count == 0)
        {
            builder.AppendLine("_No events were resolved during the last 30 days._");
            builder.AppendLine();
        }
        else
        {
            builder.AppendLine("| Date | Type | Service | Title | Status |");
            builder.AppendLine("|------|------|---------|-------|--------|");
            foreach (var item in events)
            {
                builder.AppendLine(
                    $"| {item.EventTimestamp:yyyy-MM-dd} | {EventTypeLabel(item.EventType)} | " +
                    $"{Service(item)} | {Title(item)} | ✅ {Value(item.Status, "Resolved")} |");
            }
            builder.AppendLine();
        }

        builder.AppendLine("---");
        builder.AppendLine();
    }

    private static void AppendStaticGuidance(StringBuilder builder)
    {
        builder.AppendLine("""
# Operating Guide

| Signal | Executive Interpretation | Expected Response |
|:---|:---|:---|
| 🔴 **Service Issue** | Unplanned disruption or degradation | Validate impact, activate incident response, track Microsoft updates |
| 🟡 **Planned Maintenance** | Scheduled platform activity | Check workload readiness, deployment windows, and contingency plans |
| 🟠 **Health Advisory** | Change, retirement, or configuration guidance | Assess exposure, assign ownership, plan remediation |
| 🔐 **Security Advisory** | Security-related risk or required review | Assess severity, validate controls, prioritize remediation |

## Governance Notes

- **Source of truth:** Microsoft Azure Service Health.
- **Refresh model:** Event-driven updates plus daily CloudLens reconciliation.
- **Decision ownership:** Platform Operations coordinates triage; workload owners validate business impact and remediation.
- **Support path:** Use the standard incident and Azure support processes when active impact is confirmed.

---

_CloudLens Service Health Intelligence · Executive visibility from Azure signal to operational action._
""");
    }

    private static void AppendPriorityActionQueue(
        StringBuilder builder,
        IReadOnlyList<ServiceHealthEvent> actionItems)
    {
        builder.AppendLine("## Priority Action Queue");
        builder.AppendLine();
        if (actionItems.Count == 0)
        {
            builder.AppendLine("> 🟢 **No immediate action queue.** Continue monitoring and normal operational readiness.");
            return;
        }

        builder.AppendLine("| Priority | Signal | Service | Region | Recommended Next Step |");
        builder.AppendLine("|:---:|:---|:---|:---|:---|");
        foreach (var item in actionItems.Take(10))
        {
            builder.AppendLine(
                $"| {Priority(item)} | {EventTypeIcon(item.EventType)} {EventTypeLabel(item.EventType)} | " +
                $"{Service(item)} | {Regions(item)} | {ActionSummary(item)} |");
        }
    }

    private static void AppendBrandHeader(StringBuilder builder)
    {
        var logoUrl = Environment.GetEnvironmentVariable("CLOUDLENS_LOGO_URL");
        if (string.IsNullOrWhiteSpace(logoUrl))
        {
            var hostname = Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME");
            if (!string.IsNullOrWhiteSpace(hostname))
                logoUrl = $"https://{hostname.TrimEnd('/')}/cloudlens-logo.png";
        }

        if (!string.IsNullOrWhiteSpace(logoUrl) &&
            Uri.TryCreate(logoUrl, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps)
        {
            builder.AppendLine($"![CloudLens]({uri.AbsoluteUri})");
            builder.AppendLine();
        }
    }

    private static void AppendPropertyTable(
        StringBuilder builder,
        ServiceHealthEvent item,
        string eventType)
    {
        builder.AppendLine("| Property | Value |");
        builder.AppendLine("|----------|-------|");
        builder.AppendLine($"| Status | {Value(item.Status, "Active")} |");
        builder.AppendLine($"| Azure Service | {Service(item)} |");
        builder.AppendLine($"| Affected Regions | {Regions(item)} |");
        builder.AppendLine($"| Event Type | {eventType} |");
        builder.AppendLine($"| Tracking ID | {Escape(item.TrackingId)} |");
        builder.AppendLine($"| Last Updated | {FormatDate(item.EventTimestamp)} |");
        builder.AppendLine();
    }

    private static void AppendActions(
        StringBuilder builder,
        ServiceHealthEvent item,
        IReadOnlyList<string>? fallback = null)
    {
        var action = item.LlmAnalysis?.ActionRequired;
        if (!string.IsNullOrWhiteSpace(action))
        {
            builder.AppendLine($"- {Escape(action)}");
            return;
        }

        foreach (var fallbackAction in fallback ??
                 ["Review the event details and affected workloads.", "Follow the latest Microsoft guidance."])
        {
            builder.AppendLine($"- {fallbackAction}");
        }
    }

    private static string Title(ServiceHealthEvent item) =>
        Escape(Value(item.LlmAnalysis?.SuggestedTitle, item.Title, "Azure Service Health notification"));

    private static string SourceSummary(ServiceHealthEvent item) =>
        RichTextToMarkdown(Value(item.Summary, item.Title, "No additional Microsoft summary was provided."));

    private static string BusinessImpact(ServiceHealthEvent item) =>
        RichTextToMarkdown(Value(
            item.LlmAnalysis?.AttentionJustification,
            item.LlmAnalysis?.BriefSummary,
            "Review affected services and regions to determine business impact."));

    private static string Service(ServiceHealthEvent item) =>
        Escape(Value(item.Service, item.LlmAnalysis?.AffectedServices.FirstOrDefault(), "Not specified by Microsoft"));

    private static string Regions(ServiceHealthEvent item) =>
        item.AffectedRegions.Count > 0
            ? Escape(string.Join(", ", item.AffectedRegions))
            : item.RegionScope == ServiceHealthRegionScopes.Global
                ? "Global"
                : Escape(Value(
                    item.Region,
                    item.LlmAnalysis?.AffectedRegions.Count > 0
                        ? string.Join(", ", item.LlmAnalysis.AffectedRegions)
                        : null,
                    "Global or not specified by Microsoft"));

    private static string Severity(ServiceHealthEvent item) =>
        Escape(Value(item.LlmAnalysis?.Severity, item.Level, "Not specified by Microsoft"));

    private static string ActionSummary(ServiceHealthEvent item) =>
        MarkdownCell(Value(
            item.LlmAnalysis?.ActionRequired,
            "Review the event, validate workload exposure, and follow Microsoft guidance."));

    private static string Priority(ServiceHealthEvent item) =>
        item.EventType == ServiceHealthEventTypes.ServiceIssue ||
        string.Equals(item.LlmAnalysis?.Severity, SeverityLevels.Critical, StringComparison.OrdinalIgnoreCase)
            ? "P1"
            : string.Equals(item.LlmAnalysis?.Severity, SeverityLevels.High, StringComparison.OrdinalIgnoreCase)
                ? "P2"
                : "P3";

    private static string EventTypeIcon(string eventType) => eventType switch
    {
        ServiceHealthEventTypes.ServiceIssue => "🔴",
        ServiceHealthEventTypes.PlannedMaintenance => "🟡",
        ServiceHealthEventTypes.HealthAdvisory => "🟠",
        ServiceHealthEventTypes.SecurityAdvisory => "🔐",
        _ => "🔵"
    };

    private static int CountDistinctServices(IReadOnlyList<ServiceHealthEvent> events) =>
        events.Select(item => Value(item.Service, item.LlmAnalysis?.AffectedServices.FirstOrDefault()))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

    private static string CountDistinctRegions(IReadOnlyList<ServiceHealthEvent> events)
    {
        if (events.Any(item => item.RegionScope == ServiceHealthRegionScopes.Global))
            return "Global and regional";

        var count = events
            .SelectMany(item => item.AffectedRegions.Count > 0
                ? item.AffectedRegions
                : [Value(item.Region)])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        return count == 0 ? "Not specified" : $"{count} region{(count == 1 ? string.Empty : "s")}";
    }

    private static bool RequiresAction(ServiceHealthEvent item) =>
        item.EventType == ServiceHealthEventTypes.ServiceIssue ||
        item.LlmAnalysis?.RequiresAttention == true ||
        string.Equals(item.LlmAnalysis?.Severity, SeverityLevels.Critical, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(item.LlmAnalysis?.Severity, SeverityLevels.High, StringComparison.OrdinalIgnoreCase);

    private static bool IsResolved(ServiceHealthEvent item)
    {
        var status = item.Status ?? string.Empty;
        return status.Contains("resolved", StringComparison.OrdinalIgnoreCase) ||
               status.Contains("complete", StringComparison.OrdinalIgnoreCase) ||
               status.Contains("closed", StringComparison.OrdinalIgnoreCase) ||
               status.Contains("cancel", StringComparison.OrdinalIgnoreCase);
    }

    private static int EventTypeOrder(string eventType) => eventType switch
    {
        ServiceHealthEventTypes.ServiceIssue => 0,
        ServiceHealthEventTypes.PlannedMaintenance => 1,
        ServiceHealthEventTypes.HealthAdvisory => 2,
        ServiceHealthEventTypes.SecurityAdvisory => 3,
        _ => 4
    };

    private static string EventTypeLabel(string eventType) => eventType switch
    {
        ServiceHealthEventTypes.ServiceIssue => "Service Issue",
        ServiceHealthEventTypes.PlannedMaintenance => "Planned Maintenance",
        ServiceHealthEventTypes.HealthAdvisory => "Health Advisory",
        ServiceHealthEventTypes.SecurityAdvisory => "Security Advisory",
        _ => Escape(eventType)
    };

    private static string FormatDate(DateTimeOffset value) =>
        value == default ? "Not specified by Microsoft" : Escape(value.ToString("yyyy-MM-dd HH:mm 'UTC'"));

    private static string Value(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string Escape(string? value) =>
        WebUtility.HtmlEncode(value?.Replace("|", "\\|", StringComparison.Ordinal).Trim() ?? string.Empty);

    private static string RichTextToMarkdown(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var markdown = WebUtility.HtmlDecode(value).Replace("\r\n", "\n", StringComparison.Ordinal);
        markdown = AnchorPattern.Replace(markdown, match =>
        {
            var href = WebUtility.HtmlDecode(match.Groups["href"].Value).Trim().TrimEnd('"', '\'');
            var text = WebUtility.HtmlDecode(TagPattern.Replace(match.Groups["text"].Value, string.Empty)).Trim();
            return Uri.TryCreate(href, UriKind.Absolute, out var uri) &&
                   (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
                ? $"[{EscapeMarkdownText(Value(text, uri.Host))}]({uri.AbsoluteUri})"
                : EscapeMarkdownText(text);
        });

        markdown = Regex.Replace(markdown, @"<\s*br\s*/?\s*>", "\n", RegexOptions.IgnoreCase);
        markdown = Regex.Replace(markdown, @"<\s*li\b[^>]*>", "- ", RegexOptions.IgnoreCase);
        markdown = Regex.Replace(markdown, @"<\s*/\s*li\s*>", "\n", RegexOptions.IgnoreCase);
        markdown = Regex.Replace(markdown, @"<\s*(?:h[1-6]|p|div)\b[^>]*>", "\n", RegexOptions.IgnoreCase);
        markdown = Regex.Replace(markdown, @"<\s*/\s*(?:h[1-6]|p|div|ul|ol)\s*>", "\n", RegexOptions.IgnoreCase);
        markdown = Regex.Replace(markdown, @"<\s*(?:strong|b)\b[^>]*>", "**", RegexOptions.IgnoreCase);
        markdown = Regex.Replace(markdown, @"<\s*/\s*(?:strong|b)\s*>", "**", RegexOptions.IgnoreCase);
        markdown = Regex.Replace(markdown, @"<\s*(?:em|i)\b[^>]*>", "_", RegexOptions.IgnoreCase);
        markdown = Regex.Replace(markdown, @"<\s*/\s*(?:em|i)\s*>", "_", RegexOptions.IgnoreCase);
        markdown = TagPattern.Replace(markdown, string.Empty);

        var lines = markdown
            .Split('\n')
            .Select(line => line.Trim())
            .ToArray();
        return ExcessBlankLinesPattern.Replace(string.Join('\n', lines), "\n\n").Trim();
    }

    private static string EscapeMarkdownText(string value) =>
        value.Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal);

    private static string MarkdownCell(string value)
    {
        var normalized = RichTextToMarkdown(value)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal);
        return normalized.Length <= 180 ? normalized : $"{normalized[..177]}...";
    }
}
