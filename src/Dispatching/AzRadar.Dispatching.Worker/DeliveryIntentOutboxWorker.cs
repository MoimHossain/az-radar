using Azure.Messaging.ServiceBus;
using AzRadar.Dispatching.Core;
using AzRadar.Dispatching.Core.Configuration;
using AzRadar.Dispatching.Core.Models;
using Microsoft.Extensions.Options;

namespace AzRadar.Dispatching.Worker;

public sealed class DeliveryIntentOutboxWorker : BackgroundService
{
    private readonly DispatchingRepository _repository;
    private readonly ServiceBusClient _serviceBusClient;
    private readonly DispatchingServiceBusSettings _settings;
    private readonly ILogger<DeliveryIntentOutboxWorker> _logger;

    public DeliveryIntentOutboxWorker(
        DispatchingRepository repository,
        ServiceBusClient serviceBusClient,
        IOptions<DispatchingServiceBusSettings> options,
        ILogger<DeliveryIntentOutboxWorker> logger)
    {
        _repository = repository;
        _serviceBusClient = serviceBusClient;
        _settings = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        EnsureConfigured();
        await using var sender = _serviceBusClient.CreateSender(_settings.TopicName);
        var delay = TimeSpan.FromSeconds(Math.Clamp(_settings.OutboxPollSeconds, 1, 60));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var intents = await _repository.GetPendingIntentsAsync(
                    _settings.OutboxBatchSize,
                    stoppingToken);
                foreach (var intent in intents)
                {
                    var target = await _repository.GetChannelAsync(intent.ChannelId, stoppingToken);
                    if (target == null)
                    {
                        await _repository.MarkFailedAsync(
                            intent.Id,
                            true,
                            "TargetNotFound",
                            $"Dispatch target '{intent.ChannelId}' was not found.",
                            stoppingToken);
                        continue;
                    }

                    var targetType = string.IsNullOrWhiteSpace(intent.TargetType)
                        ? target.Type
                        : intent.TargetType;
                    var envelope = new ServiceHealthDeliveryEnvelope
                    {
                        DeliveryIntentId = intent.Id,
                        EventId = intent.EventId,
                        ChannelId = intent.ChannelId,
                        TargetType = targetType,
                        EventType = intent.EventType,
                        CreatedAt = intent.CreatedAt
                    };
                    var message = new ServiceBusMessage(BinaryData.FromObjectAsJson(envelope))
                    {
                        MessageId = intent.Id,
                        CorrelationId = intent.EventId,
                        Subject = intent.EventType,
                        ContentType = "application/json"
                    };
                    message.ApplicationProperties["destinationType"] = targetType;

                    // Publish before changing Cosmos state. If the state update fails,
                    // Service Bus duplicate detection suppresses the stable MessageId.
                    await sender.SendMessageAsync(message, stoppingToken);
                    await _repository.TryMarkQueuedAsync(intent.Id, message.MessageId, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish Service Health delivery intents");
            }

            await Task.Delay(delay, stoppingToken);
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_settings.FullyQualifiedNamespace) ||
            string.IsNullOrWhiteSpace(_settings.TopicName) ||
            string.IsNullOrWhiteSpace(_settings.ManagedIdentityClientId))
        {
            throw new InvalidOperationException("Dispatching Service Bus settings are incomplete.");
        }
    }
}
