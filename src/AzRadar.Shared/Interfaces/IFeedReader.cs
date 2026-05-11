using AzRadar.Shared.Models;

namespace AzRadar.Shared.Interfaces;

public interface IFeedReader
{
    string Source { get; }
    Task<IReadOnlyList<FeedItem>> ReadFeedAsync(
        DateTimeOffset? since = null,
        CancellationToken cancellationToken = default);
}
