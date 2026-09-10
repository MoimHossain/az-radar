using AzRadar.Shared.Models;

namespace AzRadar.Shared.Interfaces;

public interface IServiceHealthEventProcessor
{
    Task<int> ProcessAsync(
        BinaryData body,
        DateTimeOffset enqueuedTime,
        CancellationToken cancellationToken = default);
}

public interface IServiceHealthTestEventPublisher
{
    Task<ServiceHealthTestEventResult> PublishAsync(
        string subscriptionId,
        string eventType,
        CancellationToken cancellationToken = default);
}

public sealed record ServiceHealthTestEventResult(
    string EventDataId,
    string TrackingId,
    string EventType,
    DateTimeOffset PublishedAt);
