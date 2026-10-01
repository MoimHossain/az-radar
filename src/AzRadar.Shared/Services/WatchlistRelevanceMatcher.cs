using System.Text;
using AzRadar.Shared.Models;

namespace AzRadar.Shared.Services;

public static class WatchlistRelevanceMatcher
{
    private static readonly HashSet<string> IgnoredServiceWords =
        new(StringComparer.OrdinalIgnoreCase) { "azure", "microsoft", "for", "and", "the" };

    private static readonly HashSet<string> GlobalRegions =
        new(StringComparer.OrdinalIgnoreCase) { "global", "allregion", "allregions", "worldwide" };

    // Single tokens too broad to establish relevance on their own during containment matching.
    private static readonly HashSet<string> GenericTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "service", "plan", "account", "api", "app", "cloud", "data", "management", "manager",
        "platform", "support", "center", "portal", "resource", "sku", "tier", "network", "virtual",
        "machine", "server", "database", "standard", "basic", "premium", "classic", "legacy",
        "preview", "feature", "version", "new", "ai", "by", "on", "in", "of", "to", "with", "log",
        "storage", "security", "agent", "gateway", "instance", "container", "web", "insight",
    };

    /// <summary>
    /// Built-in families: when a watch term matches a root, the members are watched too, because
    /// lifecycle announcements are often published under a sub-component name
    /// (e.g. "Azure Disk Encryption" for a Virtual Machines watch).
    /// </summary>
    private static readonly (string[] Roots, string[] Members)[] ServiceFamilies =
    [
        (["Virtual Machines", "VM"],
         ["Virtual Machine Scale Sets", "Managed Disks", "Azure Disks", "Disk Storage", "Standard HDD",
          "Azure Disk Encryption", "Dependency Agent", "VM Insights", "Dedicated Host", "VM Extensions"]),
        (["Virtual Machine Scale Sets", "VMSS"],
         ["Virtual Machines", "Azure Disk Encryption", "Managed Disks", "Dependency Agent", "VM Insights"]),
        (["Key Vault"], ["Managed HSM"]),
        (["Network Watcher"], ["NSG Flow Logs", "Flow Logs", "Traffic Analytics"]),
        (["Virtual Network", "VNet"],
         ["Network Security Groups", "NSG Flow Logs", "VNet Flow Logs", "Network Watcher", "Public IP Addresses",
          "Virtual Network Manager"]),
        (["Front Door"], ["Azure CDN", "Content Delivery Network"]),
        (["CDN", "Content Delivery Network"], ["Front Door"]),
        (["Azure Cache for Redis", "Redis Cache", "Redis"], ["Azure Managed Redis", "Redis Enterprise"]),
        (["App Service"],
         ["Azure Functions", "Logic Apps", "App Service Environment", "Web Apps", "Web App for Containers"]),
        (["Azure Monitor"],
         ["Log Analytics", "Application Insights", "Azure Monitor Agent", "Dependency Agent", "VM Insights",
          "Container Insights", "Data Collection Rules"]),
        (["Log Analytics"], ["Azure Monitor Logs", "Dependency Agent", "VM Insights", "Azure Monitor Agent"]),
        (["Azure Storage", "Storage Accounts", "Storage"],
         ["Blob Storage", "Azure Files", "Queue Storage", "Table Storage", "Data Lake Storage", "Storage Account"]),
        (["Azure Kubernetes Service", "AKS"], ["Container Insights", "Kubernetes Fleet Manager"]),
        (["Cognitive Services", "Azure AI Services", "Foundry Tools"],
         ["Azure AI Vision", "Computer Vision", "Image Analysis", "Azure AI Language", "Language Service",
          "Azure AI Speech", "Azure OpenAI", "Document Intelligence", "Translator", "Content Safety"]),
        (["Azure Automation", "Automation"],
         ["Update Management", "State Configuration", "Desired State Configuration", "Runbooks"]),
        (["Application Gateway"], ["Web Application Firewall", "WAF", "Application Gateway for Containers"]),
    ];

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
            var userTerms = new[] { item.ServiceName }
                .Concat(item.Aliases ?? [])
                .Concat(item.SearchTerms ?? [])
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(CreateServiceIdentity)
                .ToList();

            if (!RegionsMatch(item.Regions ?? [], regions))
                continue;

            if (userTerms.Any(watch => services.Any(service => ServiceMatches(watch, service, allowReverse: true))))
                return item;

            var familyTerms = ExpandFamilies(userTerms);
            if (familyTerms.Any(watch => services.Any(service => ServiceMatches(watch, service, allowReverse: false))))
                return item;
        }

        return null;
    }

    private static List<ServiceIdentity> ExpandFamilies(IReadOnlyCollection<ServiceIdentity> userTerms)
    {
        var expanded = new List<ServiceIdentity>();
        foreach (var (roots, members) in ServiceFamilies)
        {
            if (roots.Select(CreateServiceIdentity).Any(root => userTerms.Any(term => ExactMatches(root, term))))
                expanded.AddRange(members.Select(CreateServiceIdentity));
        }
        return expanded;
    }

    private static bool ServiceMatches(ServiceIdentity watch, ServiceIdentity service, bool allowReverse) =>
        ExactMatches(watch, service) ||
        Contains(service, watch) ||
        (allowReverse && Contains(watch, service)) ||
        (IsSpecificToken(watch.Acronym) && watch.Stems.Count > 1 && service.Stems.Contains(watch.Acronym)) ||
        (IsSpecificToken(watch.Normalized) && service.Stems.Contains(watch.Normalized));

    private static bool ExactMatches(ServiceIdentity left, ServiceIdentity right) =>
        left.Normalized == right.Normalized ||
        left.SignificantTokens == right.SignificantTokens ||
        (left.Stems.Count > 0 && left.Stems.SetEquals(right.Stems)) ||
        (left.Normalized.Length >= 2 && left.Normalized == right.Acronym) ||
        (right.Normalized.Length >= 2 && right.Normalized == left.Acronym);

    /// <summary>
    /// True when <paramref name="part"/> occurs as a contiguous phrase in <paramref name="whole"/> and
    /// is specific enough (a non-generic token, or at least two tokens). A single-token part must be
    /// the head of <paramref name="whole"/>, so "Cosmos" matches "Cosmos DB" while "Azure Files"
    /// does not match "Azure NetApp Files".
    /// </summary>
    private static bool Contains(ServiceIdentity whole, ServiceIdentity part) =>
        part.Sequence.Length > 0 &&
        (part.Stems.Count >= 2 || part.Stems.Any(stem => !GenericTokens.Contains(stem))) &&
        (part.Sequence.Length == 1
            ? whole.Sequence.Length > 0 && whole.Sequence[0] == part.Sequence[0]
            : ContainsPhrase(whole.Sequence, part.Sequence));

    private static bool ContainsPhrase(string[] whole, string[] part)
    {
        for (var start = 0; start + part.Length <= whole.Length; start++)
        {
            if (whole.AsSpan(start, part.Length).SequenceEqual(part))
                return true;
        }
        return false;
    }

    private static bool IsSpecificToken(string token) =>
        token.Length >= 3 && !GenericTokens.Contains(token);

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
        var sequence = words
            .Where(word => !IgnoredServiceWords.Contains(word))
            .ToArray();
        var significant = sequence
            .OrderBy(word => word, StringComparer.Ordinal)
            .ToArray();
        var acronym = string.Concat(words.Where(word => !string.Equals(word, "for", StringComparison.OrdinalIgnoreCase))
            .Select(word => char.ToLowerInvariant(word[0])));
        return new ServiceIdentity(
            Normalize(value),
            string.Join('|', significant),
            acronym,
            significant.Select(Stem).ToHashSet(StringComparer.Ordinal),
            sequence.Select(Stem).ToArray());
    }

    private static string Stem(string word) =>
        word.Length > 3 && word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal) &&
        !word.EndsWith("us", StringComparison.Ordinal) && !word.EndsWith("is", StringComparison.Ordinal)
            ? word[..^1]
            : word;

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

    private sealed record ServiceIdentity(
        string Normalized, string SignificantTokens, string Acronym, HashSet<string> Stems, string[] Sequence);
}
