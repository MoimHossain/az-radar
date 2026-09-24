using AzRadar.Shared.Models;

namespace AzRadar.Shared.Services;

public static class ServiceHealthRegionMatcher
{
    public static bool MatchesTarget(
        ServiceHealthEvent serviceHealthEvent,
        IReadOnlyCollection<string> targetRegions)
    {
        ArgumentNullException.ThrowIfNull(serviceHealthEvent);
        if (targetRegions.Count == 0)
            return false;

        if (serviceHealthEvent.RegionScope is ServiceHealthRegionScopes.Global or
            ServiceHealthRegionScopes.Unscoped or ServiceHealthRegionScopes.Unknown)
        {
            return true;
        }

        var targetKeys = targetRegions
            .Select(AzureRegionResolver.NormalizeKey)
            .ToHashSet(StringComparer.Ordinal);
        return serviceHealthEvent.AffectedRegions
            .Select(AzureRegionResolver.NormalizeKey)
            .Any(targetKeys.Contains);
    }

    public static IReadOnlyList<ServiceHealthEvent> FilterForTarget(
        IEnumerable<ServiceHealthEvent> sourceEvents,
        IReadOnlyCollection<string> targetRegions)
    {
        var results = new List<ServiceHealthEvent>();
        foreach (var group in sourceEvents.GroupBy(
                     item => string.IsNullOrWhiteSpace(item.TrackingId) ? item.Id : item.TrackingId,
                     StringComparer.OrdinalIgnoreCase))
        {
            var versions = group
                .OrderByDescending(item => item.EventTimestamp)
                .ThenByDescending(item => item.ReceivedAt)
                .ToList();
            var latest = versions[0];
            var effectiveScope = versions.FirstOrDefault(item =>
                item.AffectedRegions.Count > 0 ||
                item.RegionScope == ServiceHealthRegionScopes.Global ||
                item.UnresolvedRegionValues.Count > 0);

            if (effectiveScope != null && latest.AffectedRegions.Count == 0 &&
                latest.RegionScope == ServiceHealthRegionScopes.Unscoped)
            {
                latest.AffectedRegions = [.. effectiveScope.AffectedRegions];
                latest.UnresolvedRegionValues = [.. effectiveScope.UnresolvedRegionValues];
                latest.RegionScope = effectiveScope.RegionScope;
                latest.Region = effectiveScope.Region;
            }

            if (MatchesTarget(latest, targetRegions))
                results.Add(latest);
        }

        return results;
    }
}
