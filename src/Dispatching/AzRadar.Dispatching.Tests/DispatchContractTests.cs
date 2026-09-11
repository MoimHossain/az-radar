using AzRadar.Dispatching.Core.Models;
using AzRadar.Shared.Models;

namespace AzRadar.Dispatching.Tests;

public sealed class DispatchContractTests
{
    [Fact]
    public void TeamsEnvelope_PreservesStableDeliveryIntentIdentity()
    {
        var intent = new ServiceHealthDeliveryIntent
        {
            Id = "intent-123",
            EventId = "event-123",
            ChannelId = "channel-123",
            EventType = ServiceHealthEventTypes.ServiceIssue
        };

        var envelope = new TeamsDeliveryEnvelope
        {
            DeliveryIntentId = intent.Id,
            EventId = intent.EventId,
            ChannelId = intent.ChannelId,
            EventType = intent.EventType
        };

        Assert.Equal(intent.Id, envelope.DeliveryIntentId);
        Assert.Equal(ServiceHealthDeliveryIntentStatuses.Pending, intent.Status);
    }

    [Fact]
    public void TeamsBotChannel_StartsWithoutEventFamilySubscriptions()
    {
        var channel = new ServiceHealthNotificationChannel
        {
            Type = ServiceHealthChannelTypes.TeamsBot,
            RegistrationStatus = ServiceHealthChannelRegistrationStatuses.Registered
        };

        Assert.Empty(channel.SubscribedEventTypes);
        Assert.Equal(ServiceHealthChannelTypes.TeamsBot, channel.Type);
        Assert.Equal(ServiceHealthChannelRegistrationStatuses.Registered, channel.RegistrationStatus);
    }
}
