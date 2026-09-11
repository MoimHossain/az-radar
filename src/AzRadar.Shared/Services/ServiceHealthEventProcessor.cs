using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Models;
using Microsoft.Extensions.Logging;

namespace AzRadar.Shared.Services;

public class ServiceHealthEventProcessor : IServiceHealthEventProcessor
{
    private readonly ICosmosDbService _cosmosDb;
    private readonly ILlmAnalyzer _llmAnalyzer;
    private readonly ILogger<ServiceHealthEventProcessor> _logger;

    public ServiceHealthEventProcessor(
        ICosmosDbService cosmosDb,
        ILlmAnalyzer llmAnalyzer,
        ILogger<ServiceHealthEventProcessor> logger)
    {
        _cosmosDb = cosmosDb;
        _llmAnalyzer = llmAnalyzer;
        _logger = logger;
    }

    public async Task<int> ProcessAsync(
        BinaryData body,
        DateTimeOffset enqueuedTime,
        CancellationToken cancellationToken = default)
    {
        var events = ServiceHealthEventNormalizer.Normalize(body, enqueuedTime);
        var processed = 0;

        foreach (var serviceHealthEvent in events)
        {
            serviceHealthEvent.LlmAnalysis = await _llmAnalyzer.AnalyzeServiceHealthEventAsync(
                serviceHealthEvent, cancellationToken);

            var channels = await _cosmosDb.GetServiceHealthChannelsAsync(cancellationToken);
            var matchingChannels = channels
                .Where(channel =>
                    channel.Type == ServiceHealthChannelTypes.TeamsBot &&
                    channel.RegistrationStatus == ServiceHealthChannelRegistrationStatuses.Registered &&
                    channel.SubscribedEventTypes.Contains(
                        serviceHealthEvent.EventType,
                        StringComparer.OrdinalIgnoreCase))
                .ToList();

            serviceHealthEvent.MatchingChannelIds = matchingChannels.Select(channel => channel.Id).ToList();
            serviceHealthEvent.RoutingStatus = matchingChannels.Count == 0
                ? ServiceHealthRoutingStatuses.NoRoute
                : ServiceHealthRoutingStatuses.ReadyForDispatch;

            if (!await _cosmosDb.TryStoreServiceHealthEventAsync(serviceHealthEvent, cancellationToken))
            {
                _logger.LogInformation(
                    "Skipping duplicate Service Health event {EventId}",
                    serviceHealthEvent.EventDataId);
                continue;
            }

            foreach (var channel in matchingChannels)
            {
                var intent = new ServiceHealthDeliveryIntent
                {
                    Id = ServiceHealthEventNormalizer.ComputeHash(
                        $"{serviceHealthEvent.Id}|{channel.Id}|{serviceHealthEvent.ContentHash}"),
                    EventId = serviceHealthEvent.Id,
                    ChannelId = channel.Id,
                    ChannelDisplayName = channel.DisplayName,
                    EventType = serviceHealthEvent.EventType
                };
                await _cosmosDb.TryCreateServiceHealthDeliveryIntentAsync(intent, cancellationToken);
            }

            processed++;
            _logger.LogInformation(
                "Persisted Service Health event {EventId} ({EventType}) with {RouteCount} delivery intent(s)",
                serviceHealthEvent.EventDataId,
                serviceHealthEvent.EventType,
                matchingChannels.Count);
        }

        return processed;
    }
}
