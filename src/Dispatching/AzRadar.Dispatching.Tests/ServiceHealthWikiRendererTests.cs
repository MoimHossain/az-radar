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

        Assert.Contains("# CloudLens Service Health Hub", markdown);
        Assert.Contains("# Active Service Health Events", markdown);
        Assert.Contains("# Upcoming Planned Maintenance", markdown);
        Assert.Contains("# Active Health Advisories", markdown);
        Assert.Contains("# Security Advisories", markdown);
        Assert.Contains("| Service Issues | 🔴 Active | 1 |", markdown);
        Assert.Contains("| Planned Maintenance | 🟡 Upcoming | 1 |", markdown);
        Assert.Contains("| Health Advisories | 🟡 Active | 1 |", markdown);
        Assert.Contains("| Security Advisories | 🟡 Review | 1 |", markdown);
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

        Assert.Contains("| Service Issues | 🟢 Healthy | 0 |", markdown);
        Assert.Contains("| 2026-09-18 | Service Issue | Azure Test | Incident resolved | ✅ Resolved |", markdown);
        Assert.DoesNotContain("### Incident\n", markdown);
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
