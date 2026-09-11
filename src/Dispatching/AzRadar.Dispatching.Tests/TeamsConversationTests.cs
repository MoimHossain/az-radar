using System.Text.Json;
using AzRadar.Dispatching.Core.Models;
using AzRadar.Dispatching.Worker;
using Microsoft.Agents.Builder;
using Microsoft.Agents.Builder.App.Proactive;
using Microsoft.Agents.Core.Models;

namespace AzRadar.Dispatching.Tests;

public sealed class TeamsConversationTests
{
    [Fact]
    public void RestoreConversation_PreservesGatewayProtocolRoutingAndIdentity()
    {
        var original = CreateConversation();

        var restored = TeamsDeliveryWorker.RestoreConversation(original.ToJson());

        Assert.Equal("msteams", restored.Reference.ChannelId);
        Assert.Equal("19:test-channel@thread.tacv2", restored.Reference.Conversation.Id);
        Assert.Equal("https://smba.trafficmanager.net/teams/", restored.Reference.ServiceUrl);
        Assert.Equal("28:test-bot", restored.Reference.Agent.Id);
        Assert.Equal("test-bot-client-id", restored.Identity.FindFirst("aud")?.Value);
    }

    [Fact]
    public void RestoreConversation_RejectsMissingChannelId()
    {
        var original = CreateConversation();
        original.Reference.ChannelId = string.Empty;

        Assert.Throws<JsonException>(() => TeamsDeliveryWorker.RestoreConversation(original.ToJson()));
    }

    [Fact]
    public void RestoreConversation_RejectsMissingBotAudience()
    {
        var original = new Conversation(
            new Dictionary<string, string>(),
            CreateConversation().Reference);

        Assert.Throws<JsonException>(() => TeamsDeliveryWorker.RestoreConversation(original.ToJson()));
    }

    [Fact]
    public void CreateChannelPostOptions_TargetsTeamsChannelAsNewConversation()
    {
        var conversation = CreateConversation();
        conversation.Reference.ActivityId = "registration-message-id";
        var activity = MessageFactory.Text("Maintenance notification");
        var reference = new TeamsConversationReferenceDocument
        {
            TenantId = "tenant-id",
            ChannelId = "19:planned-maintenance@thread.tacv2"
        };

        var options = TeamsDeliveryWorker.CreateChannelPostOptions(
            conversation,
            reference,
            activity);

        Assert.Equal("msteams", options.ChannelId);
        Assert.Equal("tenant-id", options.Parameters.TenantId);
        Assert.True(options.Parameters.IsGroup);
        Assert.Same(activity, options.Parameters.Activity);
        var channelData = JsonSerializer.Serialize(options.Parameters.ChannelData);
        Assert.Contains(reference.ChannelId, channelData);
        Assert.DoesNotContain("registration-message-id", channelData);
    }

    private static Conversation CreateConversation() => new(
        new Dictionary<string, string> { ["aud"] = "test-bot-client-id" },
        new ConversationReference
        {
            ChannelId = "msteams",
            Conversation = new ConversationAccount { Id = "19:test-channel@thread.tacv2" },
            Agent = new ChannelAccount { Id = "28:test-bot" },
            User = new ChannelAccount { Id = "29:test-user" },
            ServiceUrl = "https://smba.trafficmanager.net/teams/"
        });
}
