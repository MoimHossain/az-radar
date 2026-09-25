using AzRadar.Dispatching.Core;
using AzRadar.Shared.Models;

namespace AzRadar.Dispatching.Tests;

public sealed class ServiceHealthWikiRendererTests
{
    [Fact]
    public void Render_PreservesCustomerDashboardAndAllEventFamilies()
    {
        var timestamp = new DateTimeOffset(2026, 9, 18, 7, 0, 0, TimeSpan.Zero);
        var events = new[]
        {
            CreateEvent(ServiceHealthEventTypes.ServiceIssue, "Incident", "Active", timestamp),
            CreateEvent(ServiceHealthEventTypes.PlannedMaintenance, "Maintenance", "Active", timestamp),
            CreateEvent(ServiceHealthEventTypes.HealthAdvisory, "Health", "Active", timestamp),
            CreateEvent(ServiceHealthEventTypes.SecurityAdvisory, "Security", "Active", timestamp)
        };

        var markdown = new ServiceHealthWikiRenderer().Render(events, timestamp);

        Assert.Contains("# CloudLens | Azure Service Health", markdown);
        Assert.Contains("## At-a-Glance", markdown);
        Assert.Contains("## Executive Brief", markdown);
        Assert.Contains("## Priority Action Queue", markdown);
        Assert.Contains("# 🔴 Active Service Health Events", markdown);
        Assert.Contains("# 🟡 Upcoming Planned Maintenance", markdown);
        Assert.Contains("# 🟠 Active Health Advisories", markdown);
        Assert.Contains("# 🔐 Security Advisories", markdown);
        Assert.Contains("| **1**<br>Active | **1**<br>Upcoming | **1**<br>Active | **1**<br>Review |", markdown);
        Assert.Contains("| P1 | 🔴 Service Issue | Azure Test | Central US | Review affected workloads. |", markdown);
    }

    [Fact]
    public void Render_DeduplicatesTrackingIdAndMovesResolvedEvent()
    {
        var timestamp = new DateTimeOffset(2026, 9, 18, 7, 0, 0, TimeSpan.Zero);
        var original = CreateEvent(
            ServiceHealthEventTypes.ServiceIssue,
            "Incident",
            "Active",
            timestamp.AddMinutes(-5));
        var resolved = CreateEvent(
            ServiceHealthEventTypes.ServiceIssue,
            "Incident resolved",
            "Resolved",
            timestamp);
        resolved.TrackingId = original.TrackingId;

        var markdown = new ServiceHealthWikiRenderer().Render([original, resolved], timestamp);

        Assert.Contains("| **0**<br>Healthy | **0**<br>None | **0**<br>None | **0**<br>None |", markdown);
        Assert.Contains("| 2026-09-18 | Service Issue | Azure Test | Incident resolved | ✅ Resolved |", markdown);
        Assert.DoesNotContain("### Incident\n", markdown);
    }

    [Fact]
    public void Render_ConvertsSourceHtmlToSafeWikiMarkdown()
    {
        var timestamp = new DateTimeOffset(2026, 9, 18, 7, 0, 0, TimeSpan.Zero);
        var serviceEvent = CreateEvent(
            ServiceHealthEventTypes.PlannedMaintenance,
            "Maintenance",
            "Active",
            timestamp);
        serviceEvent.Summary = """
            <p><strong>Service:</strong> Azure Managed Grafana</p>
            <h2>Maintenance date/time</h2>
            <ul><li><strong>Window:</strong> 2 October 2026</li></ul>
            <p>See <a href="https://portal.azure.com/" target="_blank">Azure portal</a>.</p>
            """;

        var markdown = new ServiceHealthWikiRenderer().Render([serviceEvent], timestamp);

        Assert.Contains("**Service:** Azure Managed Grafana", markdown);
        Assert.Contains("- **Window:** 2 October 2026", markdown);
        Assert.Contains("[Azure portal](https://portal.azure.com/)", markdown);
        Assert.DoesNotContain("&lt;p&gt;", markdown);
        Assert.DoesNotContain("<strong>", markdown);
    }

    private static ServiceHealthEvent CreateEvent(
        string eventType,
        string title,
        string status,
        DateTimeOffset timestamp) =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            TrackingId = $"{eventType}-tracking",
            EventType = eventType,
            Title = title,
            Summary = $"{title} summary",
            Status = status,
            Service = "Azure Test",
            Region = "Central US",
            EventTimestamp = timestamp,
            ReceivedAt = timestamp,
            LlmAnalysis = new LlmAnalysis
            {
                SuggestedTitle = title,
                BriefSummary = $"{title} business summary",
                ActionRequired = "Review affected workloads."
            }
        };
}
