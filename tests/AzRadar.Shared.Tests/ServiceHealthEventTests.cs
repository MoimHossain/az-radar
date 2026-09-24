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
        result[0].AffectedRegions.Should().Equal("Central US");
        result[0].RegionScope.Should().Be(ServiceHealthRegionScopes.Regional);
    }

    public class ServiceHealthRegionMatcherTests
    {
        [Theory]
        [InlineData("West Europe", "westeurope")]
        [InlineData("East US 2", "east-us-2")]
        public void Resolver_ApprovedForms_ReturnCanonicalRegion(string expected, string source)
        {
            AzureRegionResolver.TryResolve(source, out var canonical).Should().BeTrue();
            canonical.Should().Be(expected);
        }

        [Fact]
        public void MatchesTarget_KnownNonIntersectingRegion_IsExcluded()
        {
            var serviceHealthEvent = Event(ServiceHealthRegionScopes.Regional, ["Japan East"]);

            ServiceHealthRegionMatcher.MatchesTarget(serviceHealthEvent, ["Central US"]).Should().BeFalse();
        }

        [Fact]
        public void MatchesTarget_KnownNonMatchWithUnknownValue_RemainsExcluded()
        {
            var serviceHealthEvent = Event(ServiceHealthRegionScopes.Regional, ["Japan East"]);
            serviceHealthEvent.UnresolvedRegionValues = ["Moon Base 1"];

            ServiceHealthRegionMatcher.MatchesTarget(serviceHealthEvent, ["Central US"]).Should().BeFalse();
        }

        [Theory]
        [InlineData(ServiceHealthRegionScopes.Global)]
        [InlineData(ServiceHealthRegionScopes.Unscoped)]
        [InlineData(ServiceHealthRegionScopes.Unknown)]
        public void MatchesTarget_GlobalOrUnscoped_IsIncluded(string scope)
        {
            ServiceHealthRegionMatcher.MatchesTarget(Event(scope, []), ["Central US"]).Should().BeTrue();
        }

        [Fact]
        public void FilterForTarget_ResolutionWithoutRegion_InheritsPriorScope()
        {
            var active = Event(ServiceHealthRegionScopes.Regional, ["Central US"]);
            active.TrackingId = "TRACK-1";
            active.Status = "Active";
            active.EventTimestamp = DateTimeOffset.UtcNow.AddMinutes(-5);
            var resolved = Event(ServiceHealthRegionScopes.Unscoped, []);
            resolved.TrackingId = active.TrackingId;
            resolved.Status = "Resolved";
            resolved.EventTimestamp = DateTimeOffset.UtcNow;

            var result = ServiceHealthRegionMatcher.FilterForTarget([active, resolved], ["Central US"]);

            result.Should().ContainSingle();
            result[0].Status.Should().Be("Resolved");
            result[0].AffectedRegions.Should().Equal("Central US");
        }

        private static ServiceHealthEvent Event(string scope, List<string> regions) => new()
        {
            Id = Guid.NewGuid().ToString(),
            TrackingId = Guid.NewGuid().ToString(),
            RegionScope = scope,
            AffectedRegions = regions,
            EventTimestamp = DateTimeOffset.UtcNow,
            ReceivedAt = DateTimeOffset.UtcNow
        };
    }

    [Fact]
    public void Normalize_NonServiceHealthRecord_RejectsPayload()
    {
        var payload = BinaryData.FromString("""{"category":"Administrative"}""");

        var act = () => ServiceHealthEventNormalizer.Normalize(payload, DateTimeOffset.UtcNow);

        act.Should().Throw<System.Text.Json.JsonException>()
            .WithMessage("*ServiceHealth*");
    }

    [Fact]
    public void Normalize_MultipleRegions_ResolvesCanonicalAndUnknownValues()
    {
        var payload = BinaryData.FromString("""
            {
              "category": "ServiceHealth",
              "eventDataId": "event-regions",
              "operationName": "Microsoft.ServiceHealth/maintenance/action",
              "properties": {
                "regions": ["centralus", "Japan East", "Moon Base 1"],
                "trackingId": "REGIONS-1"
              }
            }
            """);

        var result = ServiceHealthEventNormalizer.Normalize(payload, DateTimeOffset.UtcNow).Single();

        result.AffectedRegions.Should().BeEquivalentTo(["Central US", "Japan East"]);
        result.UnresolvedRegionValues.Should().Equal("Moon Base 1");
        result.RegionScope.Should().Be(ServiceHealthRegionScopes.Regional);
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
                    Type = ServiceHealthChannelTypes.TeamsBot,
                    RegistrationStatus = ServiceHealthChannelRegistrationStatuses.Registered,
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

    [Theory]
    [InlineData("Central US", true)]
    [InlineData("Japan East", false)]
    [InlineData("Global", true)]
    [InlineData("", true)]
    [InlineData("Moon Base 1", true)]
    public async Task ProcessAsync_WikiTarget_AppliesRegionPolicy(string eventRegion, bool expectedIntent)
    {
        var db = new Mock<ICosmosDbService>();
        var llm = new Mock<ILlmAnalyzer>();
        db.Setup(service => service.GetServiceHealthChannelsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ServiceHealthNotificationChannel
                {
                    Id = "wiki",
                    DisplayName = "Wiki",
                    Type = ServiceHealthChannelTypes.AzureDevOpsWiki,
                    RegistrationStatus = ServiceHealthChannelRegistrationStatuses.Registered,
                    IncludedRegions = ["Central US"]
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
            .ReturnsAsync(new LlmAnalysis());
        var escapedRegion = eventRegion.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var payload = BinaryData.FromString($$"""
            {
              "category": "ServiceHealth",
              "eventDataId": "{{Guid.NewGuid()}}",
              "resourceId": "/subscriptions/5e22addc-6168-4683-afd0-789a121ca5d3",
              "operationName": "Microsoft.ServiceHealth/incident/action",
              "properties": {
                "title": "Incident",
                "incidentType": "Incident",
                "trackingId": "{{Guid.NewGuid()}}",
                "region": "{{escapedRegion}}"
              }
            }
            """);

        await new ServiceHealthEventProcessor(
            db.Object,
            llm.Object,
            NullLogger<ServiceHealthEventProcessor>.Instance)
            .ProcessAsync(payload, DateTimeOffset.UtcNow);

        db.Verify(service => service.TryCreateServiceHealthDeliveryIntentAsync(
            It.IsAny<ServiceHealthDeliveryIntent>(), It.IsAny<CancellationToken>()),
            expectedIntent ? Times.Once() : Times.Never());
    }
}
