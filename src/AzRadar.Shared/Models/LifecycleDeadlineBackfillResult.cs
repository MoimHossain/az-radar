namespace AzRadar.Shared.Models;

public sealed record LifecycleDeadlineBackfillResult(
    int FeedItemsScanned,
    int FeedItemsStamped,
    int DocInsightsScanned,
    int DocInsightsStamped,
    int FeedItemsWithDeadline,
    int DocInsightsWithDeadline);
