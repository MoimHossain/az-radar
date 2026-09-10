using System.Text;
using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Models;
using AzRadar.Shared.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AzRadar.Shared.Tests;

public class ServiceHealthEventNormalizerTests
{
    [Fact]
    public void Normalize_ActivityLogEnvelope_MapsServiceIssue()
    {
        var payload = BinaryData.FromString("""
            {
              "records": [{
                "eventTimestamp": "2026-09-10T10:00:00Z",
                "eventDataId": "event-123",
                "correlationId": "correlation-123",
                "category": { "value": "ServiceHealth" },
                "operationName": { "value": "Microsoft.ServiceHealth/incident/action" },
                "resourceId": "/subscriptions/5e22addc-6168-4683-afd0-789a121ca5d3",
                "resultType": "Active",
                "level": "Informational",
                "properties": {
                  "title": "Test service issue",
                  "communication": "A test incident is active.",
                  "service": "Azure Test Service",
                  "region": "Central US",
                  "incidentType": "Incident",
                  "trackingId": "TEST-123",
                  "synthetic": true
                }
              }]
            }
            """);

        var result = ServiceHealthEventNormalizer.Normalize(payload, DateTimeOffset.UtcNow);

        result.Should().ContainSingle();
        result[0].EventType.Should().Be(ServiceHealthEventTypes.ServiceIssue);
        result[0].SubscriptionId.Should().Be("5e22addc-6168-4683-afd0-789a121ca5d3");
        result[0].TrackingId.Should().Be("TEST-123");
        result[0].IsSynthetic.Should().BeTrue();
        result[0].ContentHash.Should().HaveLength(64);
    }

    [Fact]
    public void Normalize_NonServiceHealthRecord_RejectsPayload()
    {
        var payload = BinaryData.FromString("""{"category":"Administrative"}""");

        var act = () => ServiceHealthEventNormalizer.Normalize(payload, DateTimeOffset.UtcNow);

        act.Should().Throw<System.Text.Json.JsonException>()
            .WithMessage("*ServiceHealth*");
    }
}

public class ServiceHealthEventProcessorTests
{
    [Fact]
    public async Task ProcessAsync_MatchingChannel_CreatesDeliveryIntent()
    {
        var db = new Mock<ICosmosDbService>();
        var llm = new Mock<ILlmAnalyzer>();
        db.Setup(service => service.GetServiceHealthChannelsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ServiceHealthNotificationChannel
                {
                    Id = "platform-incidents",
                    DisplayName = "Platform incidents",
                    SubscribedEventTypes = [ServiceHealthEventTypes.ServiceIssue]
                }
            ]);
        db.Setup(service => service.TryStoreServiceHealthEventAsync(
                It.IsAny<ServiceHealthEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        db.Setup(service => service.TryCreateServiceHealthDeliveryIntentAsync(
                It.IsAny<ServiceHealthDeliveryIntent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        llm.Setup(service => service.AnalyzeServiceHealthEventAsync(
                It.IsAny<ServiceHealthEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmAnalysis { BriefSummary = "Analyzed" });

        var processor = new ServiceHealthEventProcessor(
            db.Object,
            llm.Object,
            NullLogger<ServiceHealthEventProcessor>.Instance);
        var payload = BinaryData.FromBytes(Encoding.UTF8.GetBytes("""
            {
              "category": "ServiceHealth",
              "eventDataId": "event-456",
              "resourceId": "/subscriptions/5e22addc-6168-4683-afd0-789a121ca5d3",
              "operationName": "Microsoft.ServiceHealth/incident/action",
              "properties": {
                "title": "Incident",
                "incidentType": "Incident",
                "trackingId": "TEST-456"
              }
            }
            """));

        var count = await processor.ProcessAsync(payload, DateTimeOffset.UtcNow);

        count.Should().Be(1);
        db.Verify(service => service.TryCreateServiceHealthDeliveryIntentAsync(
            It.Is<ServiceHealthDeliveryIntent>(intent =>
                intent.ChannelId == "platform-incidents" &&
                intent.Status == ServiceHealthDeliveryIntentStatuses.Pending),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
