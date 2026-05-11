using System.ServiceModel.Syndication;
using AzRadar.Shared.Models;
using AzRadar.Shared.Services;
using FluentAssertions;

namespace AzRadar.Shared.Tests;

public class AzureUpdatesFeedReaderTests
{
    [Fact]
    public void GenerateDedupId_SameInput_ReturnsSameHash()
    {
        var id1 = AzureUpdatesFeedReader.GenerateDedupId("azure-updates", "https://example.com/update1");
        var id2 = AzureUpdatesFeedReader.GenerateDedupId("azure-updates", "https://example.com/update1");

        id1.Should().Be(id2);
    }

    [Fact]
    public void GenerateDedupId_DifferentInputs_ReturnsDifferentHashes()
    {
        var id1 = AzureUpdatesFeedReader.GenerateDedupId("azure-updates", "https://example.com/update1");
        var id2 = AzureUpdatesFeedReader.GenerateDedupId("azure-updates", "https://example.com/update2");

        id1.Should().NotBe(id2);
    }

    [Fact]
    public void GenerateDedupId_CaseInsensitive()
    {
        var id1 = AzureUpdatesFeedReader.GenerateDedupId("azure-updates", "HTTPS://EXAMPLE.COM/UPDATE1");
        var id2 = AzureUpdatesFeedReader.GenerateDedupId("azure-updates", "https://example.com/update1");

        id1.Should().Be(id2);
    }

    [Fact]
    public void GenerateDedupId_TrimsWhitespace()
    {
        var id1 = AzureUpdatesFeedReader.GenerateDedupId("azure-updates", "  https://example.com/update1  ");
        var id2 = AzureUpdatesFeedReader.GenerateDedupId("azure-updates", "https://example.com/update1");

        id1.Should().Be(id2);
    }

    [Fact]
    public void GenerateDedupId_ReturnsFixedLength()
    {
        var id = AzureUpdatesFeedReader.GenerateDedupId("azure-updates", "some-identifier");
        id.Should().HaveLength(32);
    }

    [Fact]
    public void MapToFeedItem_MapsBasicFields()
    {
        var syndicationItem = new SyndicationItem(
            "Redis TLS Retirement",
            "Redis is retiring TLS 1.0",
            new Uri("https://azure.microsoft.com/updates/redis-tls"));

        syndicationItem.PublishDate = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        syndicationItem.Id = "unique-guid-123";
        syndicationItem.Categories.Add(new SyndicationCategory("Retirements"));

        var result = AzureUpdatesFeedReader.MapToFeedItem(
            syndicationItem, syndicationItem.PublishDate);

        result.Title.Should().Be("Redis TLS Retirement");
        result.Link.Should().Contain("redis-tls");
        result.PublishDate.Should().Be(syndicationItem.PublishDate);
        result.Source.Should().Be(CrawlJobTypes.AzureUpdates);
        result.Categories.Should().Contain("Retirements");
        result.Id.Should().NotBeNullOrEmpty();
        result.Id.Should().HaveLength(32);
    }

    [Fact]
    public void MapToFeedItem_SameGuid_ProducesSameId()
    {
        var item1 = new SyndicationItem("Title 1", "Content 1", new Uri("https://example.com/1"));
        item1.Id = "same-guid";

        var item2 = new SyndicationItem("Title 2", "Content 2", new Uri("https://example.com/2"));
        item2.Id = "same-guid";

        var publishDate = DateTimeOffset.UtcNow;
        var result1 = AzureUpdatesFeedReader.MapToFeedItem(item1, publishDate);
        var result2 = AzureUpdatesFeedReader.MapToFeedItem(item2, publishDate);

        result1.Id.Should().Be(result2.Id);
    }

    [Fact]
    public void MapToFeedItem_HandlesEmptySummary()
    {
        var item = new SyndicationItem("Title", null, new Uri("https://example.com/1"));
        item.Id = "test-id";

        var result = AzureUpdatesFeedReader.MapToFeedItem(item, DateTimeOffset.UtcNow);

        result.Summary.Should().BeEmpty();
    }

    [Fact]
    public void MapToFeedItem_HandlesNoCategories()
    {
        var item = new SyndicationItem("Title", "Content", new Uri("https://example.com/1"));
        item.Id = "test-id";

        var result = AzureUpdatesFeedReader.MapToFeedItem(item, DateTimeOffset.UtcNow);

        result.Categories.Should().BeEmpty();
    }
}
