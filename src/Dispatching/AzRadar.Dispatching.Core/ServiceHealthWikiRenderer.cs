using System.Net;
using System.Text;
using AzRadar.Shared.Models;

namespace AzRadar.Dispatching.Core;

public sealed class ServiceHealthWikiRenderer
{
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
        var overall = serviceIssues.Count > 0 || securityAdvisories.Any(RequiresAction)
            ? ("🔴 Action Required", "Review active service issues and follow recommended actions.")
            : maintenance.Count > 0 || healthAdvisories.Count > 0 || securityAdvisories.Count > 0
                ? ("🟡 Awareness Required", "Review active advisories and upcoming maintenance.")
                : ("🟢 Normal Operations", "No action required. Continue normal operations.");

        var builder = new StringBuilder();
        builder.AppendLine("# CloudLens Service Health Hub");
        builder.AppendLine();
        builder.AppendLine("> Stay informed about Azure service events that may impact your business operations.");
        builder.AppendLine();
        builder.AppendLine("---");
        builder.AppendLine();
        builder.AppendLine("## Current Service Status");
        builder.AppendLine();
        builder.AppendLine($"**Last Updated:** _{renderedAt:yyyy-MM-dd HH:mm:ss 'UTC'}_");
        builder.AppendLine();
        builder.AppendLine("| Category | Status | Active Events |");
        builder.AppendLine("|-----------|--------|---------------|");
        builder.AppendLine($"| Service Issues | {(serviceIssues.Count == 0 ? "🟢 Healthy" : "🔴 Active")} | {serviceIssues.Count} |");
        builder.AppendLine($"| Planned Maintenance | {(maintenance.Count == 0 ? "🟢 None" : "🟡 Upcoming")} | {maintenance.Count} |");
        builder.AppendLine($"| Health Advisories | {(healthAdvisories.Count == 0 ? "🟢 None" : "🟡 Active")} | {healthAdvisories.Count} |");
        builder.AppendLine($"| Security Advisories | {(securityAdvisories.Count == 0 ? "🟢 None" : securityAdvisories.Any(RequiresAction) ? "🔴 Action Required" : "🟡 Review")} | {securityAdvisories.Count} |");
        builder.AppendLine();
        builder.AppendLine("---");
        builder.AppendLine();
        builder.AppendLine("# Executive Summary");
        builder.AppendLine();
        builder.AppendLine("## Do I Need To Take Action?");
        builder.AppendLine();
        builder.AppendLine("| Overall Status | Business Recommendation |");
        builder.AppendLine("|----------------|-------------------------|");
        builder.AppendLine($"| {overall.Item1} | {overall.Item2} |");
        builder.AppendLine();
        builder.AppendLine("---");
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
        builder.AppendLine("# Active Service Health Events");
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
            builder.AppendLine("## 🔴 Service Issue");
            builder.AppendLine();
            builder.AppendLine($"### {Title(item)}");
            builder.AppendLine();
            AppendPropertyTable(builder, item, "Service Issue");
            builder.AppendLine("### What Happened?");
            builder.AppendLine(SourceSummary(item));
            builder.AppendLine();
            builder.AppendLine("### Business Impact");
            builder.AppendLine(BusinessImpact(item));
            builder.AppendLine();
            builder.AppendLine("### Recommended Actions");
            AppendActions(builder, item);
            builder.AppendLine();
            builder.AppendLine("### Latest Microsoft Update");
            builder.AppendLine($"> {SourceSummary(item)}");
            builder.AppendLine();
            builder.AppendLine("---");
            builder.AppendLine();
        }
    }

    private static void AppendMaintenance(
        StringBuilder builder,
        IReadOnlyList<ServiceHealthEvent> events)
    {
        builder.AppendLine("# Upcoming Planned Maintenance");
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
            builder.AppendLine("## 🟡 Planned Maintenance");
            builder.AppendLine();
            builder.AppendLine($"### {Title(item)}");
            builder.AppendLine();
            builder.AppendLine("| Property | Value |");
            builder.AppendLine("|----------|-------|");
            builder.AppendLine($"| Azure Service | {Service(item)} |");
            builder.AppendLine($"| Region(s) | {Regions(item)} |");
            builder.AppendLine($"| Scheduled Date | {FormatDate(item.EventTimestamp)} |");
            builder.AppendLine("| Maintenance Window | Not specified by Microsoft |");
            builder.AppendLine($"| Status | {Value(item.Status, "Scheduled")} |");
            builder.AppendLine();
            builder.AppendLine("### What Is Changing?");
            builder.AppendLine(SourceSummary(item));
            builder.AppendLine();
            builder.AppendLine("### Potential Business Impact");
            builder.AppendLine(BusinessImpact(item));
            builder.AppendLine();
            builder.AppendLine("### Recommended Preparation");
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
        builder.AppendLine("# Active Health Advisories");
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
            builder.AppendLine("## 🟡 Health Advisory");
            builder.AppendLine();
            builder.AppendLine($"### {Title(item)}");
            builder.AppendLine();
            builder.AppendLine("| Property | Value |");
            builder.AppendLine("|----------|-------|");
            builder.AppendLine($"| Azure Service | {Service(item)} |");
            builder.AppendLine($"| Tracking ID | {Escape(item.TrackingId)} |");
            builder.AppendLine($"| Published | {FormatDate(item.EventTimestamp)} |");
            builder.AppendLine($"| Status | {Value(item.Status, "Active")} |");
            builder.AppendLine();
            builder.AppendLine("### Summary");
            builder.AppendLine(SourceSummary(item));
            builder.AppendLine();
            builder.AppendLine("### Why Should You Care?");
            builder.AppendLine(BusinessImpact(item));
            builder.AppendLine();
            builder.AppendLine("### Recommended Actions");
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
        builder.AppendLine("# Security Advisories");
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
            builder.AppendLine("## 🔒 Security Advisory");
            builder.AppendLine();
            builder.AppendLine($"### {Title(item)}");
            builder.AppendLine();
            builder.AppendLine("| Property | Value |");
            builder.AppendLine("|----------|-------|");
            builder.AppendLine($"| Severity | {Severity(item)} |");
            builder.AppendLine($"| Azure Service | {Service(item)} |");
            builder.AppendLine($"| Tracking ID | {Escape(item.TrackingId)} |");
            builder.AppendLine($"| Last Updated | {FormatDate(item.EventTimestamp)} |");
            builder.AppendLine();
            builder.AppendLine("### Summary");
            builder.AppendLine(SourceSummary(item));
            builder.AppendLine();
            builder.AppendLine("### Potential Risk");
            builder.AppendLine(BusinessImpact(item));
            builder.AppendLine();
            builder.AppendLine("### Recommended Actions");
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
        builder.AppendLine("# Recently Resolved Events");
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
# Understanding Service Health Notifications

### Service Issues
Unexpected Azure service disruptions that may affect availability, performance, connectivity, or functionality.

### Planned Maintenance
Scheduled Microsoft activities that may temporarily affect Azure services and require preparation.

### Health Advisories
Announcements regarding platform changes, service retirements, configuration recommendations, or future actions.

### Security Advisories
Important security-related notifications that may require assessment, review, or remediation.

---

# Why Am I Receiving These Notifications?

Cloud services are highly reliable, but planned maintenance, service incidents, health advisories, and security notifications can occasionally occur.

Being informed allows your organization to:

✅ Respond faster to service disruptions

✅ Reduce troubleshooting effort

✅ Determine whether an issue is within Azure or your own environment

✅ Prepare for planned maintenance and platform changes

✅ Improve communication with users and stakeholders

✅ Protect critical business operations

---

# Frequently Asked Questions

## How often is this page updated?

The CloudLens Service Health Hub is automatically updated whenever Microsoft publishes a relevant Azure Service Health notification. CloudLens also performs a daily reconciliation.

## Does every alert require action?

No. Some notifications are informational, while others may require preparation or remediation. Recommended actions are provided when applicable.

## How do I know if my services are impacted?

Review the affected services, regions, business impact, and recommended actions for each notification.

## Who manages this service?

The CloudLens Service Health Hub is maintained by the Platform Operations team to provide a centralized and transparent view of Azure service health information.

---

# Contact & Support

If you require assistance or believe a service event is impacting your environment, contact your support team using your standard support process.

---

_This page is automatically generated and maintained by CloudLens Service Health Intelligence._
""");
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
        Escape(Value(item.Summary, item.Title, "No additional Microsoft summary was provided."));

    private static string BusinessImpact(ServiceHealthEvent item) =>
        Escape(Value(
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
}
