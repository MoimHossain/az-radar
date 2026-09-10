using AzRadar.Shared.Models;
using System.Text.Json;

namespace AzRadar.Dispatching.Core;

public sealed class ServiceHealthAdaptiveCardRenderer
{
    public string Render(ServiceHealthEvent serviceHealthEvent)
    {
        ArgumentNullException.ThrowIfNull(serviceHealthEvent);

        var title = string.IsNullOrWhiteSpace(serviceHealthEvent.LlmAnalysis?.SuggestedTitle)
            ? serviceHealthEvent.Title
            : serviceHealthEvent.LlmAnalysis.SuggestedTitle;
        var summary = string.IsNullOrWhiteSpace(serviceHealthEvent.LlmAnalysis?.BriefSummary)
            ? serviceHealthEvent.Summary
            : serviceHealthEvent.LlmAnalysis.BriefSummary;

        var card = new
        {
            type = "AdaptiveCard",
            version = "1.5",
            schema = "http://adaptivecards.io/schemas/adaptive-card.json",
            msteams = new { width = "Full" },
            body = new object[]
            {
                new
                {
                    type = "TextBlock",
                    text = title,
                    weight = "Bolder",
                    size = "Large",
                    wrap = true
                },
                new
                {
                    type = "FactSet",
                    facts = new[]
                    {
                        new { title = "Type", value = serviceHealthEvent.EventType },
                        new { title = "Status", value = serviceHealthEvent.Status },
                        new { title = "Service", value = EmptyAsUnknown(serviceHealthEvent.Service) },
                        new { title = "Region", value = EmptyAsUnknown(serviceHealthEvent.Region) },
                        new { title = "Tracking ID", value = EmptyAsUnknown(serviceHealthEvent.TrackingId) },
                        new { title = "Subscription", value = serviceHealthEvent.SubscriptionId }
                    }
                },
                new
                {
                    type = "TextBlock",
                    text = summary,
                    wrap = true
                },
                new
                {
                    type = "TextBlock",
                    text = $"Received {serviceHealthEvent.ReceivedAt:yyyy-MM-dd HH:mm:ss} UTC",
                    isSubtle = true,
                    spacing = "Small",
                    wrap = true
                }
            }
        };

        return JsonSerializer.Serialize(card);
    }

    private static string EmptyAsUnknown(string value) =>
        string.IsNullOrWhiteSpace(value) ? "Unknown" : value;
}
