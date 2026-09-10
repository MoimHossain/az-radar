using System.Text.Json;
using AzRadar.Dispatching.Core;
using AzRadar.Shared.Models;

namespace AzRadar.Dispatching.Tests;

public sealed class ServiceHealthAdaptiveCardRendererTests
{
    [Fact]
    public void Render_UsesAiSummaryWithoutRemovingAuthoritativeFields()
    {
        var serviceHealthEvent = new ServiceHealthEvent
        {
            Title = "Microsoft title",
            Summary = "Microsoft summary",
            EventType = ServiceHealthEventTypes.ServiceIssue,
            Status = "Active",
            Service = "Azure Storage",
            Region = "West Europe",
            TrackingId = "TRACK-123",
            SubscriptionId = "00000000-0000-0000-0000-000000000001",
            ReceivedAt = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero),
            LlmAnalysis = new LlmAnalysis
            {
                SuggestedTitle = "Storage incident requires attention",
                BriefSummary = "A concise AI-generated summary."
            }
        };

        var json = new ServiceHealthAdaptiveCardRenderer().Render(serviceHealthEvent);
        using var document = JsonDocument.Parse(json);
        var rendered = document.RootElement.ToString();

        Assert.Equal("AdaptiveCard", document.RootElement.GetProperty("type").GetString());
        Assert.Contains("Storage incident requires attention", rendered);
        Assert.Contains("A concise AI-generated summary.", rendered);
        Assert.Contains("TRACK-123", rendered);
        Assert.Contains("00000000-0000-0000-0000-000000000001", rendered);
        Assert.Contains(ServiceHealthEventTypes.ServiceIssue, rendered);
    }

    [Fact]
    public void Render_UsesUnknownForMissingServiceAndRegion()
    {
        var serviceHealthEvent = new ServiceHealthEvent
        {
            Title = "Planned maintenance",
            Summary = "Maintenance details",
            EventType = ServiceHealthEventTypes.PlannedMaintenance,
            Status = "Active",
            TrackingId = "TRACK-456",
            SubscriptionId = "00000000-0000-0000-0000-000000000002"
        };

        var json = new ServiceHealthAdaptiveCardRenderer().Render(serviceHealthEvent);

        Assert.Contains("\"value\":\"Unknown\"", json);
    }
}
