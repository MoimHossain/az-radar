using System.Security.Cryptography;
using System.ServiceModel.Syndication;
using System.Text;
using System.Xml;
using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Models;
using Microsoft.Extensions.Logging;

namespace AzRadar.Shared.Services;

public class AzureUpdatesFeedReader : IFeedReader
{
    public const string AzureUpdatesRssUrl = "https://www.microsoft.com/releasecommunications/api/v2/azure/rss";
    private readonly HttpClient _httpClient;
    private readonly ILogger<AzureUpdatesFeedReader> _logger;

    public string Source => CrawlJobTypes.AzureUpdates;

    public AzureUpdatesFeedReader(HttpClient httpClient, ILogger<AzureUpdatesFeedReader> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<FeedItem>> ReadFeedAsync(
        DateTimeOffset? since = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Reading Azure Updates RSS feed since {Since}", since?.ToString("o") ?? "all");

        using var stream = await _httpClient.GetStreamAsync(AzureUpdatesRssUrl, cancellationToken);
        var xmlSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Parse,
            MaxCharactersFromEntities = 1024
        };
        using var xmlReader = XmlReader.Create(stream, xmlSettings);

        var feed = SyndicationFeed.Load(xmlReader);
        if (feed == null)
        {
            _logger.LogWarning("Failed to load RSS feed");
            return [];
        }

        var items = new List<FeedItem>();

        foreach (var entry in feed.Items)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var publishDate = entry.PublishDate != default
                ? entry.PublishDate
                : entry.LastUpdatedTime;

            if (since.HasValue && publishDate < since.Value)
                continue;

            var item = MapToFeedItem(entry, publishDate);
            items.Add(item);
        }

        _logger.LogInformation("Read {Count} feed items from Azure Updates", items.Count);
        return items;
    }

    internal static FeedItem MapToFeedItem(SyndicationItem entry, DateTimeOffset publishDate)
    {
        var link = entry.Links.FirstOrDefault()?.Uri?.ToString() ?? string.Empty;
        var guid = entry.Id ?? link;
        var id = GenerateDedupId(CrawlJobTypes.AzureUpdates, guid);

        var summary = entry.Summary?.Text ?? string.Empty;
        var rawContent = entry.Content is TextSyndicationContent textContent
            ? textContent.Text
            : summary;

        var categories = entry.Categories
            .Select(c => c.Name)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();

        return new FeedItem
        {
            Id = id,
            Source = CrawlJobTypes.AzureUpdates,
            Title = entry.Title?.Text ?? string.Empty,
            Link = link,
            PublishDate = publishDate,
            Summary = summary,
            Categories = categories,
            RawContent = rawContent
        };
    }

    /// <summary>
    /// Generates a deterministic dedup ID from source + normalized identifier.
    /// </summary>
    internal static string GenerateDedupId(string source, string identifier)
    {
        var input = $"{source}:{identifier.Trim().ToLowerInvariant()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant()[..32];
    }
}
