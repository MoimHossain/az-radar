namespace AzRadar.Shared.Models;

public static class AzureRegionCatalog
{
    public static readonly IReadOnlyList<string> PublicRegions =
    [
        "Australia Central",
        "Australia Central 2",
        "Australia East",
        "Australia Southeast",
        "Brazil South",
        "Brazil Southeast",
        "Canada Central",
        "Canada East",
        "Central India",
        "Central US",
        "Central US EUAP",
        "Chile Central",
        "East Asia",
        "East US",
        "East US 2",
        "East US 2 EUAP",
        "France Central",
        "France South",
        "Germany North",
        "Germany West Central",
        "Indonesia Central",
        "Israel Central",
        "Italy North",
        "Japan East",
        "Japan West",
        "Jio India Central",
        "Jio India West",
        "Korea Central",
        "Korea South",
        "Malaysia West",
        "Mexico Central",
        "New Zealand North",
        "North Central US",
        "North Europe",
        "Norway East",
        "Norway West",
        "Poland Central",
        "Qatar Central",
        "South Africa North",
        "South Africa West",
        "South Central US",
        "South India",
        "Southeast Asia",
        "Spain Central",
        "Sweden Central",
        "Sweden South",
        "Switzerland North",
        "Switzerland West",
        "UAE Central",
        "UAE North",
        "UK South",
        "UK West",
        "West Central US",
        "West Europe",
        "West India",
        "West US",
        "West US 2",
        "West US 3"
    ];

    public static bool TryGetCanonicalName(string value, out string canonicalName)
    {
        return AzureRegionResolver.TryResolve(value, out canonicalName);
    }
}

public static class AzureRegionResolver
{
    private static readonly IReadOnlyDictionary<string, string> RegionsByKey = BuildRegionsByKey();
    private static readonly HashSet<string> GlobalKeys =
        new(["global", "worldwide", "allregion", "allregions"], StringComparer.Ordinal);

    public static bool TryResolve(string? value, out string canonicalName)
    {
        canonicalName = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (!RegionsByKey.TryGetValue(NormalizeKey(value), out var resolved))
            return false;

        canonicalName = resolved;
        return true;
    }

    public static bool IsGlobal(string? value) =>
        !string.IsNullOrWhiteSpace(value) && GlobalKeys.Contains(NormalizeKey(value));

    public static IReadOnlyList<string> NormalizeRegions(
        IEnumerable<string>? values,
        out IReadOnlyList<string> unresolvedValues,
        out bool isGlobal)
    {
        var regions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unresolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        isGlobal = false;

        foreach (var value in values ?? [])
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;
            if (IsGlobal(value))
            {
                isGlobal = true;
                continue;
            }
            if (TryResolve(value, out var canonicalName))
                regions.Add(canonicalName);
            else
                unresolved.Add(value.Trim());
        }

        unresolvedValues = unresolved.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
        return regions.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string NormalizeKey(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static IReadOnlyDictionary<string, string> BuildRegionsByKey()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var region in AzureRegionCatalog.PublicRegions)
        {
            var key = NormalizeKey(region);
            if (!result.TryAdd(key, region))
                throw new InvalidOperationException($"Duplicate Azure region key '{key}'.");
        }
        return result;
    }
}
