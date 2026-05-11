using AzRadar.Shared.Models;

namespace AzRadar.Shared.Interfaces;

public interface ILlmAnalyzer
{
    Task<LlmAnalysis> AnalyzeFeedItemAsync(
        FeedItem item,
        CancellationToken cancellationToken = default);
}
