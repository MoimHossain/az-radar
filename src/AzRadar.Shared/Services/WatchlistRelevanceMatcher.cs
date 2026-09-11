using System.Text;
using AzRadar.Shared.Models;

namespace AzRadar.Shared.Services;

public static class WatchlistRelevanceMatcher
{
    private static readonly HashSet<string> IgnoredServiceWords =
        new(StringComparer.OrdinalIgnoreCase) { "azure", "microsoft", "for", "and", "the" };

    private static readonly HashSet<string> GlobalRegions =
        new(StringComparer.OrdinalIgnoreCase) { "global", "allregion", "allregions", "worldwide" };

    public static WatchlistItem? FindMatch(
        IEnumerable<WatchlistItem> watchlist,
        IEnumerable<string> affectedServices,
        IEnumerable<string>? affectedRegions = null)
    {
        var services = affectedServices
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(CreateServiceIdentity)
            .ToList();
        var regions = (affectedRegions ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(Normalize)
            .ToList();

        if (services.Count == 0)
            return null;

        foreach (var item in watchlist)
        {
            var watchTerms = new[] { item.ServiceName }
                .Concat(item.Aliases ?? [])
                .Concat(item.SearchTerms ?? [])
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(CreateServiceIdentity);

            if (watchTerms.Any(watch => services.Any(service => ServiceMatches(watch, service))) &&
                RegionsMatch(item.Regions ?? [], regions))
                return item;
        }

        return null;
    }

    private static bool ServiceMatches(ServiceIdentity left, ServiceIdentity right) =>
        left.Normalized == right.Normalized ||
        left.SignificantTokens == right.SignificantTokens ||
        (left.Normalized.Length >= 2 && left.Normalized == right.Acronym) ||
        (right.Normalized.Length >= 2 && right.Normalized == left.Acronym);

    private static bool RegionsMatch(IReadOnlyCollection<string> watchedRegions, IReadOnlyCollection<string> affectedRegions)
    {
        if (watchedRegions.Count == 0 || affectedRegions.Count == 0 || affectedRegions.Any(GlobalRegions.Contains))
            return true;

        var watched = watchedRegions.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return affectedRegions.Any(watched.Contains);
    }

    private static ServiceIdentity CreateServiceIdentity(string value)
    {
        var words = SplitWords(value);
        var significant = words
            .Where(word => !IgnoredServiceWords.Contains(word))
            .OrderBy(word => word, StringComparer.Ordinal)
            .ToArray();
        var acronym = string.Concat(words.Where(word => !string.Equals(word, "for", StringComparison.OrdinalIgnoreCase))
            .Select(word => char.ToLowerInvariant(word[0])));
        return new ServiceIdentity(
            Normalize(value),
            string.Join('|', significant),
            acronym);
    }

    private static IReadOnlyList<string> SplitWords(string value)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                if (char.IsUpper(character) && current.Length > 0 && char.IsLower(current[^1]))
                {
                    words.Add(current.ToString().ToLowerInvariant());
                    current.Clear();
                }
                current.Append(char.ToLowerInvariant(character));
            }
            else if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
        }
        if (current.Length > 0)
            words.Add(current.ToString());
        return words;
    }

    private static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private sealed record ServiceIdentity(string Normalized, string SignificantTokens, string Acronym);
}
