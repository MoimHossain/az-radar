using AzRadar.Dispatching.Core;
using AzRadar.Dispatching.Core.Models;
using AzRadar.Shared.Models;
using Microsoft.Agents.Builder;
using Microsoft.Agents.Builder.App;
using Microsoft.Agents.Builder.App.Proactive;
using Microsoft.Agents.Builder.State;
using Microsoft.Agents.Core.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AzRadar.Dispatching.BotGateway;

public sealed class AzRadarNotificationAgent : AgentApplication
{
    private readonly DispatchingRepository _repository;
    private readonly ILogger<AzRadarNotificationAgent> _logger;

    public AzRadarNotificationAgent(
        AgentApplicationOptions options,
        DispatchingRepository repository,
        ILogger<AzRadarNotificationAgent> logger)
        : base(options)
    {
        _repository = repository;
        _logger = logger;

        OnActivity(ActivityTypes.InstallationUpdate, OnInstallationUpdateAsync);
        OnConversationUpdate(
            ConversationUpdateEvents.MembersAdded,
            CaptureConversationAsync);
        OnActivity(ActivityTypes.Message, OnMessageAsync, rank: RouteRank.Last);
    }

    private async Task OnInstallationUpdateAsync(
        ITurnContext turnContext,
        ITurnState turnState,
        CancellationToken cancellationToken)
    {
        var channelData = ReadChannelData(turnContext.Activity.ChannelData);
        if (string.Equals(turnContext.Activity.Action, "remove", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(channelData.TenantId) &&
                !string.IsNullOrWhiteSpace(channelData.TeamId))
            {
                await _repository.MarkTeamUninstalledAsync(
                    channelData.TenantId,
                    channelData.TeamId,
                    cancellationToken);
            }
            _logger.LogInformation(
                "Teams bot uninstalled from team {TeamId}",
                channelData.TeamId);
            return;
        }

        if (!TryCreateRegistration(turnContext, out var registration))
        {
            _logger.LogInformation(
                "Teams app installed in team {TeamId}; waiting for a channel registration message",
                channelData.TeamId);
            return;
        }

        await _repository.UpsertConversationReferenceAsync(registration, cancellationToken);
        _logger.LogInformation(
            "Registered Teams destination {TeamId}/{ChannelId}",
            registration.TeamId,
            registration.ChannelId);
    }

    private async Task CaptureConversationAsync(
        ITurnContext turnContext,
        ITurnState turnState,
        CancellationToken cancellationToken)
    {
        if (TryCreateRegistration(turnContext, out var registration))
        {
            await _repository.UpsertConversationReferenceAsync(registration, cancellationToken);
        }
    }

    private async Task OnMessageAsync(
        ITurnContext turnContext,
        ITurnState turnState,
        CancellationToken cancellationToken)
    {
        if (!TryCreateRegistration(turnContext, out var registration))
        {
            await turnContext.SendActivityAsync(
                MessageFactory.Text("This command must be sent from a Teams channel."),
                cancellationToken);
            return;
        }

        await _repository.UpsertConversationReferenceAsync(registration, cancellationToken);
        await turnContext.SendActivityAsync(
            MessageFactory.Text(
                "This channel is registered with CloudLens and is disabled until a platform administrator selects event families and enables it."),
            cancellationToken);
    }

    private static bool TryCreateRegistration(
        ITurnContext turnContext,
        out TeamsConversationReferenceDocument registration)
    {
        var channelData = ReadChannelData(turnContext.Activity.ChannelData);
        var tenantId = channelData.TenantId;
        var teamId = channelData.TeamId;
        var channelId = channelData.ChannelId;
        if (string.IsNullOrWhiteSpace(tenantId) ||
            string.IsNullOrWhiteSpace(teamId) ||
            string.IsNullOrWhiteSpace(channelId))
        {
            registration = null!;
            return false;
        }

        registration = new TeamsConversationReferenceDocument
        {
            Id = ComputeId(tenantId, teamId, channelId),
            TenantId = tenantId,
            TeamId = teamId,
            TeamName = channelData.TeamName,
            ChannelId = channelId,
            ChannelName = channelData.ChannelName,
            ConversationJson = new Conversation(turnContext).ToJson(),
            Active = true
        };
        return true;
    }

    private static TeamsChannelData ReadChannelData(object? channelData)
    {
        if (channelData == null)
        {
            return new TeamsChannelData();
        }

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(channelData));
        var root = document.RootElement;
        return new TeamsChannelData
        {
            TenantId = ReadNested(root, "tenant", "id"),
            TeamId = ReadNested(root, "team", "id"),
            TeamName = ReadNested(root, "team", "name"),
            ChannelId = ReadNested(root, "channel", "id"),
            ChannelName = ReadNested(root, "channel", "name")
        };
    }

    private static string ReadNested(JsonElement root, string parent, string property)
    {
        if (root.TryGetProperty(parent, out var parentElement) &&
            parentElement.ValueKind == JsonValueKind.Object &&
            parentElement.TryGetProperty(property, out var value))
        {
            return value.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static string ComputeId(string tenantId, string teamId, string channelId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{tenantId}|{teamId}|{channelId}"));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private sealed class TeamsChannelData
    {
        public string TenantId { get; init; } = string.Empty;
        public string TeamId { get; init; } = string.Empty;
        public string TeamName { get; init; } = string.Empty;
        public string ChannelId { get; init; } = string.Empty;
        public string ChannelName { get; init; } = string.Empty;
    }
}
