using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using AzRadar.Shared.Interfaces;
using Microsoft.Extensions.Logging;

namespace AzRadar.Shared.Services;

/// <summary>
/// Reads the full public Release Communications catalog and unions it with RSS.
/// Unlike the MCP discovery tool, the catalog supplies untruncated descriptions.
/// </summary>
public sealed class AzureUpdatesSource(HttpClient http, ILogger<AzureUpdatesSource> logger)
    : IAzureUpdatesSource, IDisposable
{
    internal const string CatalogUrl = "https://www.microsoft.com/releasecommunications/api/v2/azure/";
    internal const string RssUrl = CatalogUrl + "rss";
    private const string BackendHost = "relcomms-prod-dagnegedescbeefs.b02.azurefd.net";

    public async Task<AzureUpdatesSnapshot> GetUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var items = new Dictionary<string, AzureUpdateItem>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        // Stable ID ordering prevents modifications to old posts from moving them between pages.
        string? url = CatalogUrl + "?$orderby=id%20asc&$count=true";
        int? expectedCount = null;

        while (url != null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(url))
                throw new InvalidDataException("Azure Updates catalog repeated a continuation URL.");

            using var response = await GetAsync(url, "application/json", cancellationToken);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (!root.TryGetProperty("value", out var page) || page.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Azure Updates catalog response has no value array.");

            if (root.TryGetProperty("@odata.count", out var count))
            {
                var reportedCount = count.GetInt32();
                if (expectedCount.HasValue && expectedCount != reportedCount)
                    throw new InvalidDataException("Azure Updates catalog changed during enumeration; retry the crawl.");
                expectedCount = reportedCount;
            }
            if (!expectedCount.HasValue || expectedCount <= 0)
                throw new InvalidDataException("Azure Updates catalog is missing a positive total count.");

            foreach (var element in page.EnumerateArray())
            {
                var item = ParseCatalogItem(element);
                if (!items.TryAdd(item.Id, item))
                    throw new InvalidDataException($"Azure Updates catalog repeated ID {item.Id}; retry the crawl.");
            }

            url = root.TryGetProperty("@odata.nextLink", out var next) && next.ValueKind != JsonValueKind.Null
                ? NormalizeContinuation(next.GetString()!)
                : null;
            if (url != null && page.GetArrayLength() == 0)
                throw new InvalidDataException("Azure Updates catalog returned an empty page with a continuation.");

            logger.LogInformation("Azure Updates catalog: {Count}/{Expected} posts read", items.Count, expectedCount);
        }

        if (items.Count != expectedCount)
            throw new InvalidDataException(
                $"Azure Updates catalog is incomplete: expected {expectedCount}, received {items.Count} unique posts.");

        var catalogCount = items.Count;
        using var rssResponse = await GetAsync(RssUrl, "application/rss+xml, application/xml", cancellationToken);
        await using var rssStream = await rssResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = XmlReader.Create(rssStream, new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        });
        var rssDocument = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);
        var channel = rssDocument.Root?.Name == "rss" ? rssDocument.Root.Element("channel") : null;
        if (channel == null)
            throw new InvalidDataException("Azure Updates RSS response has no rss/channel element.");
        var rssItems = channel.Elements("item").Select(ParseRssItem).ToList();
        if (rssItems.Count == 0)
            throw new InvalidDataException("Azure Updates RSS feed unexpectedly returned no entries.");

        foreach (var rssItem in rssItems)
        {
            // RSS can change while the catalog is being paged, or be ahead of its cache.
            if (!items.TryGetValue(rssItem.Id, out var catalogItem) ||
                ParseDate(rssItem.Modified) > ParseDate(catalogItem.Modified))
            {
                using var detailResponse = await GetAsync(
                    CatalogUrl + Uri.EscapeDataString(rssItem.Id), "application/json", cancellationToken);
                await using var detailStream = await detailResponse.Content.ReadAsStreamAsync(cancellationToken);
                using var detailDocument = await JsonDocument.ParseAsync(detailStream, cancellationToken: cancellationToken);
                var detail = ParseCatalogItem(detailDocument.RootElement);
                if (!string.Equals(detail.Id, rssItem.Id, StringComparison.OrdinalIgnoreCase) ||
                    ParseDate(detail.Modified) < ParseDate(rssItem.Modified))
                    throw new InvalidDataException($"Catalog detail for RSS update {rssItem.Id} is stale or mismatched; retry the crawl.");
                items[rssItem.Id] = detail;
            }
        }

        return new AzureUpdatesSnapshot(
            items.Values.OrderByDescending(i => ParseDate(i.Modified)).ThenBy(i => i.Id, StringComparer.Ordinal).ToList(),
            catalogCount, rssItems.Count);
    }

    private async Task<HttpResponseMessage> GetAsync(string url, string accept, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; AzRadar/1.0)");
        request.Headers.TryAddWithoutValidation("Accept", accept);
        var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var status = response.StatusCode;
            response.Dispose();
            throw new HttpRequestException($"Azure Updates source {url} returned HTTP {(int)status}.", null, status);
        }
        return response;
    }

    internal static string NormalizeContinuation(string link)
    {
        if (!Uri.TryCreate(new Uri(CatalogUrl), link, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length != 0 ||
            (uri.Host != "www.microsoft.com" && uri.Host != BackendHost) ||
            (uri.AbsolutePath.TrimEnd('/') != "/api/v2/azure" &&
             uri.AbsolutePath.TrimEnd('/') != "/releasecommunications/api/v2/azure") ||
            uri.Query.Length == 0 || uri.Fragment.Length != 0)
            throw new InvalidDataException("Azure Updates catalog returned an unexpected continuation URL.");

        // The service emits its Front Door origin; keep requests on the public website endpoint.
        return CatalogUrl + uri.Query;
    }

    internal static AzureUpdateItem ParseCatalogItem(JsonElement element)
    {
        var item = new AzureUpdateItem
        {
            Id = RequiredString(element, "id").Trim(),
            Title = RequiredString(element, "title"),
            Description = RequiredString(element, "description"),
            Created = RequiredString(element, "created"),
            Modified = RequiredString(element, "modified"),
            Status = OptionalString(element, "status") ?? "",
            Products = Strings(element, "products"),
            Tags = Strings(element, "tags"),
            ProductCategories = Strings(element, "productCategories"),
            GeneralAvailabilityDate = OptionalString(element, "generalAvailabilityDate")
        };
        _ = ParseDate(item.Created);
        _ = ParseDate(item.Modified);
        return item;
    }

    internal static AzureUpdateItem ParseRssItem(XElement element)
    {
        string Required(string name) => !string.IsNullOrWhiteSpace(element.Element(name)?.Value)
            ? element.Element(name)!.Value
            : throw new InvalidDataException($"Azure Updates RSS entry has no {name}.");

        var id = Required("guid").Trim();
        var created = ParseDate(Required("pubDate"));
        var modifiedText = element.Element(XName.Get("updated", "http://www.w3.org/2005/Atom"))?.Value;
        var modified = string.IsNullOrWhiteSpace(modifiedText) ? created : ParseDate(modifiedText);
        return new AzureUpdateItem
        {
            Id = id,
            Title = Required("title"),
            Description = Required("description"),
            Link = $"https://azure.microsoft.com/updates?id={Uri.EscapeDataString(id)}",
            Created = created.ToString("O"),
            Modified = modified.ToString("O"),
            Tags = element.Elements("category").Select(e => e.Value).ToList()
        };
    }

    private static DateTimeOffset ParseDate(string value)
    {
        // RSS uses a literal "Z" where RFC 1123 normally uses "GMT".
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var date))
            return date;
        throw new InvalidDataException($"Azure Updates source contains an invalid date: {value}");
    }

    private static string RequiredString(JsonElement element, string name) =>
        !string.IsNullOrWhiteSpace(OptionalString(element, name))
            ? element.GetProperty(name).GetString()!
            : throw new InvalidDataException($"Azure Updates catalog entry has no {name}.");

    private static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString()
            : null;

    private static List<string> Strings(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(e => e.GetString() ??
                throw new InvalidDataException($"Null value in Azure Updates {name}.")).ToList()
            : [];

    public void Dispose() => http.Dispose();
}
