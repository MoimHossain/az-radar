using AzRadar.Dispatching.Core.Configuration;
using AzRadar.Dispatching.Core.Models;
using AzRadar.Shared.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using System.Net;

namespace AzRadar.Dispatching.Core;

public sealed class DispatchingRepository
{
    private readonly Container _events;
    private readonly Container _channels;
    private readonly Container _deliveryIntents;
    private readonly Container _conversationReferences;
    private readonly Container _deliveryAttempts;

    public DispatchingRepository(CosmosClient client, IOptions<DispatchingCosmosSettings> options)
    {
        var settings = options.Value;
        var database = client.GetDatabase(settings.DatabaseName);
        _events = database.GetContainer(settings.EventsContainer);
        _channels = database.GetContainer(settings.ChannelsContainer);
        _deliveryIntents = database.GetContainer(settings.DeliveryIntentsContainer);
        _conversationReferences = database.GetContainer(settings.ConversationReferencesContainer);
        _deliveryAttempts = database.GetContainer(settings.DeliveryAttemptsContainer);
    }

    public async Task<IReadOnlyList<ServiceHealthDeliveryIntent>> GetPendingIntentsAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        var query = _deliveryIntents.GetItemQueryIterator<ServiceHealthDeliveryIntent>(
            new QueryDefinition(
                "SELECT TOP @limit * FROM c WHERE c.status = @status ORDER BY c.createdAt")
                .WithParameter("@limit", Math.Clamp(limit, 1, 200))
                .WithParameter("@status", ServiceHealthDeliveryIntentStatuses.Pending));
        var results = new List<ServiceHealthDeliveryIntent>();
        while (query.HasMoreResults)
        {
            var response = await query.ReadNextAsync(cancellationToken);
            results.AddRange(response);
        }

        return results;
    }

    public async Task<ServiceHealthDeliveryIntent?> GetIntentAsync(
        string id,
        CancellationToken cancellationToken) =>
        await ReadAsync<ServiceHealthDeliveryIntent>(_deliveryIntents, id, cancellationToken);

    public async Task<ServiceHealthEvent?> GetEventAsync(
        string id,
        CancellationToken cancellationToken) =>
        await ReadAsync<ServiceHealthEvent>(_events, id, cancellationToken);

    public async Task<ServiceHealthNotificationChannel?> GetChannelAsync(
        string id,
        CancellationToken cancellationToken) =>
        await ReadAsync<ServiceHealthNotificationChannel>(_channels, id, cancellationToken);

    public async Task<TeamsConversationReferenceDocument?> GetConversationReferenceAsync(
        string id,
        CancellationToken cancellationToken) =>
        await ReadAsync<TeamsConversationReferenceDocument>(
            _conversationReferences,
            id,
            cancellationToken);

    public async Task<bool> TryMarkQueuedAsync(
        string id,
        string messageId,
        CancellationToken cancellationToken) =>
        await TryPatchAsync(
            id,
            ServiceHealthDeliveryIntentStatuses.Pending,
            [
                PatchOperation.Set("/status", ServiceHealthDeliveryIntentStatuses.Queued),
                PatchOperation.Set("/serviceBusMessageId", messageId),
                PatchOperation.Set("/queuedAt", DateTimeOffset.UtcNow),
                PatchOperation.Set("/updatedAt", DateTimeOffset.UtcNow)
            ],
            cancellationToken);

    public async Task<bool> TryBeginDispatchAsync(
        ServiceHealthDeliveryIntent intent,
        CancellationToken cancellationToken)
    {
        if (intent.Status is not (
            ServiceHealthDeliveryIntentStatuses.Queued or
            ServiceHealthDeliveryIntentStatuses.Dispatching or
            ServiceHealthDeliveryIntentStatuses.RetryScheduled))
        {
            return false;
        }

        return await TryPatchAsync(
            intent.Id,
            intent.Status,
            [
                PatchOperation.Set("/status", ServiceHealthDeliveryIntentStatuses.Dispatching),
                PatchOperation.Set("/attemptCount", intent.AttemptCount + 1),
                PatchOperation.Set("/updatedAt", DateTimeOffset.UtcNow)
            ],
            cancellationToken);
    }

    public async Task MarkDeliveredAsync(
        string id,
        string activityId,
        CancellationToken cancellationToken)
    {
        await _deliveryIntents.PatchItemAsync<ServiceHealthDeliveryIntent>(
            id,
            new PartitionKey(id),
            [
                PatchOperation.Set("/status", ServiceHealthDeliveryIntentStatuses.Delivered),
                PatchOperation.Set("/teamsActivityId", activityId),
                PatchOperation.Set("/deliveredAt", DateTimeOffset.UtcNow),
                PatchOperation.Set<string?>("/lastErrorCode", null),
                PatchOperation.Set<string?>("/lastErrorMessage", null),
                PatchOperation.Set("/updatedAt", DateTimeOffset.UtcNow)
            ],
            cancellationToken: cancellationToken);
    }

    public async Task MarkFailedAsync(
        string id,
        bool deadLettered,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        await _deliveryIntents.PatchItemAsync<ServiceHealthDeliveryIntent>(
            id,
            new PartitionKey(id),
            [
                PatchOperation.Set(
                    "/status",
                    deadLettered
                        ? ServiceHealthDeliveryIntentStatuses.DeadLettered
                        : ServiceHealthDeliveryIntentStatuses.RetryScheduled),
                PatchOperation.Set("/lastErrorCode", errorCode),
                PatchOperation.Set("/lastErrorMessage", Truncate(errorMessage, 2048)),
                PatchOperation.Set("/updatedAt", DateTimeOffset.UtcNow)
            ],
            cancellationToken: cancellationToken);
    }

    public async Task UpsertConversationReferenceAsync(
        TeamsConversationReferenceDocument reference,
        CancellationToken cancellationToken)
    {
        var existing = await GetConversationReferenceAsync(reference.Id, cancellationToken);
        reference.CreatedAt = existing?.CreatedAt ?? DateTimeOffset.UtcNow;
        reference.UpdatedAt = DateTimeOffset.UtcNow;
        await _conversationReferences.UpsertItemAsync(
            reference,
            new PartitionKey(reference.Id),
            cancellationToken: cancellationToken);

        var channel = await FindChannelAsync(
            reference.TenantId,
            reference.TeamId,
            reference.ChannelId,
            cancellationToken);
        channel ??= new ServiceHealthNotificationChannel
        {
            Id = reference.Id,
            Type = ServiceHealthChannelTypes.TeamsBot,
            CreatedAt = DateTimeOffset.UtcNow
        };
        channel.DisplayName = $"{reference.TeamName} / {reference.ChannelName}".Trim(' ', '/');
        channel.TenantId = reference.TenantId;
        channel.TeamId = reference.TeamId;
        channel.TeamName = reference.TeamName;
        channel.ChannelId = reference.ChannelId;
        channel.ChannelName = reference.ChannelName;
        channel.ConversationReferenceId = reference.Id;
        channel.RegistrationStatus = ServiceHealthChannelRegistrationStatuses.Registered;
        channel.LastRegisteredAt = DateTimeOffset.UtcNow;
        channel.UpdatedAt = DateTimeOffset.UtcNow;
        await _channels.UpsertItemAsync(
            channel,
            new PartitionKey(channel.Id),
            cancellationToken: cancellationToken);
    }

    public async Task MarkConversationUninstalledAsync(
        string referenceId,
        CancellationToken cancellationToken)
    {
        var reference = await GetConversationReferenceAsync(referenceId, cancellationToken);
        if (reference != null)
        {
            reference.Active = false;
            reference.UpdatedAt = DateTimeOffset.UtcNow;
            await _conversationReferences.UpsertItemAsync(
                reference,
                new PartitionKey(reference.Id),
                cancellationToken: cancellationToken);
        }

        var channel = await GetChannelAsync(referenceId, cancellationToken);
        if (channel != null)
        {
            channel.RegistrationStatus = ServiceHealthChannelRegistrationStatuses.Uninstalled;
            channel.UpdatedAt = DateTimeOffset.UtcNow;
            await _channels.UpsertItemAsync(
                channel,
                new PartitionKey(channel.Id),
                cancellationToken: cancellationToken);
        }
    }

    public async Task MarkTeamUninstalledAsync(
        string tenantId,
        string teamId,
        CancellationToken cancellationToken)
    {
        var query = _conversationReferences.GetItemQueryIterator<TeamsConversationReferenceDocument>(
            new QueryDefinition(
                "SELECT * FROM c WHERE c.tenantId = @tenantId AND c.teamId = @teamId AND c.active = true")
                .WithParameter("@tenantId", tenantId)
                .WithParameter("@teamId", teamId));
        while (query.HasMoreResults)
        {
            var response = await query.ReadNextAsync(cancellationToken);
            foreach (var reference in response)
            {
                await MarkConversationUninstalledAsync(reference.Id, cancellationToken);
            }
        }
    }

    public async Task StoreAttemptAsync(
        TeamsDeliveryAttempt attempt,
        CancellationToken cancellationToken)
    {
        await _deliveryAttempts.CreateItemAsync(
            attempt,
            new PartitionKey(attempt.Id),
            cancellationToken: cancellationToken);
    }

    private async Task<ServiceHealthNotificationChannel?> FindChannelAsync(
        string tenantId,
        string teamId,
        string channelId,
        CancellationToken cancellationToken)
    {
        var query = _channels.GetItemQueryIterator<ServiceHealthNotificationChannel>(
            new QueryDefinition(
                "SELECT TOP 1 * FROM c WHERE c.tenantId = @tenantId AND c.teamId = @teamId AND c.channelId = @channelId")
                .WithParameter("@tenantId", tenantId)
                .WithParameter("@teamId", teamId)
                .WithParameter("@channelId", channelId));
        if (!query.HasMoreResults)
        {
            return null;
        }

        var response = await query.ReadNextAsync(cancellationToken);
        return response.FirstOrDefault();
    }

    private async Task<bool> TryPatchAsync(
        string id,
        string expectedStatus,
        IReadOnlyList<PatchOperation> operations,
        CancellationToken cancellationToken)
    {
        try
        {
            await _deliveryIntents.PatchItemAsync<ServiceHealthDeliveryIntent>(
                id,
                new PartitionKey(id),
                operations,
                new PatchItemRequestOptions
                {
                    FilterPredicate = $"FROM c WHERE c.status = \"{expectedStatus}\""
                },
                cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (
            ex.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private static async Task<T?> ReadAsync<T>(
        Container container,
        string id,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await container.ReadItemAsync<T>(
                id,
                new PartitionKey(id),
                cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }
    }

    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..length];
}
