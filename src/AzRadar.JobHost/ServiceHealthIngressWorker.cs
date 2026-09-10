using Azure.Identity;
using Azure.Messaging.EventHubs.Consumer;
using AzRadar.Shared.Configuration;
using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Models;
using AzRadar.Shared.Services;
using Microsoft.Extensions.Options;

namespace AzRadar.JobHost;

public class ServiceHealthIngressWorker : BackgroundService
{
    private readonly ServiceHealthEventHubSettings _settings;
    private readonly ICosmosDbService _cosmosDb;
    private readonly IServiceHealthEventProcessor _processor;
    private readonly ILogger<ServiceHealthIngressWorker> _logger;

    public ServiceHealthIngressWorker(
        IOptions<ServiceHealthEventHubSettings> options,
        ICosmosDbService cosmosDb,
        IServiceHealthEventProcessor processor,
        ILogger<ServiceHealthIngressWorker> logger)
    {
        _settings = options.Value;
        _cosmosDb = cosmosDb;
        _processor = processor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.EnableIngress)
        {
            _logger.LogInformation("Service Health Event Hub ingress is disabled");
            return;
        }

        EnsureConfigured();
        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ManagedIdentityClientId = _settings.ManagedIdentityClientId
        });

        await using var consumer = new EventHubConsumerClient(
            _settings.ConsumerGroup,
            _settings.FullyQualifiedNamespace,
            _settings.EventHubName,
            credential);

        var partitionIds = await consumer.GetPartitionIdsAsync(stoppingToken);
        _logger.LogInformation(
            "Starting Service Health ingress for {PartitionCount} Event Hub partitions",
            partitionIds.Length);

        await Task.WhenAll(partitionIds.Select(partitionId =>
            ReadPartitionAsync(consumer, partitionId, stoppingToken)));
    }

    private async Task ReadPartitionAsync(
        EventHubConsumerClient consumer,
        string partitionId,
        CancellationToken cancellationToken)
    {
        var retryDelay = TimeSpan.FromSeconds(2);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var checkpoint = await _cosmosDb.GetServiceHealthCheckpointAsync(
                    partitionId, cancellationToken);
                var startPosition = checkpoint == null
                    ? EventPosition.Earliest
                    : EventPosition.FromOffset(checkpoint.Offset, isInclusive: false);

                await foreach (var partitionEvent in consumer.ReadEventsFromPartitionAsync(
                    partitionId,
                    startPosition,
                    cancellationToken))
                {
                    var data = partitionEvent.Data;
                    var rawPayload = data.EventBody.ToString();

                    if (data.EventBody.ToMemory().Length > _settings.MaximumPayloadBytes)
                    {
                        await QuarantineAsync(
                            partitionId,
                            data.OffsetString,
                            data.SequenceNumber,
                            $"Payload exceeds {_settings.MaximumPayloadBytes} bytes.",
                            rawPayload,
                            cancellationToken);
                    }
                    else
                    {
                        try
                        {
                            await _processor.ProcessAsync(
                                data.EventBody,
                                data.EnqueuedTime,
                                cancellationToken);
                        }
                        catch (System.Text.Json.JsonException ex)
                        {
                            await QuarantineAsync(
                                partitionId,
                                data.OffsetString,
                                data.SequenceNumber,
                                ex.Message,
                                rawPayload,
                                cancellationToken);
                        }
                    }

                    await _cosmosDb.UpsertServiceHealthCheckpointAsync(
                        new ServiceHealthEventCheckpoint
                        {
                            PartitionId = partitionId,
                            Offset = data.OffsetString,
                            SequenceNumber = data.SequenceNumber
                        },
                        cancellationToken);
                    retryDelay = TimeSpan.FromSeconds(2);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Service Health partition {PartitionId} failed; retrying in {DelaySeconds} seconds",
                    partitionId,
                    retryDelay.TotalSeconds);
                await Task.Delay(retryDelay, cancellationToken);
                retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, 60));
            }
        }
    }

    private async Task QuarantineAsync(
        string partitionId,
        string offset,
        long sequenceNumber,
        string reason,
        string rawPayload,
        CancellationToken cancellationToken)
    {
        var safePayload = rawPayload.Length <= 64 * 1024
            ? rawPayload
            : rawPayload[..(64 * 1024)];
        await _cosmosDb.StoreServiceHealthQuarantineRecordAsync(
            new ServiceHealthQuarantineRecord
            {
                PartitionId = partitionId,
                Offset = offset,
                SequenceNumber = sequenceNumber,
                Reason = reason,
                PayloadHash = ServiceHealthEventNormalizer.ComputeHash(rawPayload),
                RawPayload = safePayload
            },
            cancellationToken);
        _logger.LogWarning(
            "Quarantined Service Health Event Hub message from partition {PartitionId}, offset {Offset}: {Reason}",
            partitionId,
            offset,
            reason);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_settings.FullyQualifiedNamespace) ||
            string.IsNullOrWhiteSpace(_settings.EventHubName) ||
            string.IsNullOrWhiteSpace(_settings.ConsumerGroup) ||
            string.IsNullOrWhiteSpace(_settings.ManagedIdentityClientId))
        {
            throw new InvalidOperationException(
                "ServiceHealthEventHub settings are incomplete for Event Hub ingestion.");
        }
    }
}
