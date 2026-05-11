using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Models;
using AzRadar.Shared.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace AzRadar.Shared.Tests;

public class AzureUpdatesJobHandlerTests
{
    private readonly Mock<IFeedReader> _feedReaderMock;
    private readonly Mock<ILlmAnalyzer> _llmAnalyzerMock;
    private readonly Mock<ICosmosDbService> _cosmosDbMock;
    private readonly AzureUpdatesJobHandler _handler;

    public AzureUpdatesJobHandlerTests()
    {
        _feedReaderMock = new Mock<IFeedReader>();
        _llmAnalyzerMock = new Mock<ILlmAnalyzer>();
        _cosmosDbMock = new Mock<ICosmosDbService>();
        var logger = new Mock<ILogger<AzureUpdatesJobHandler>>();

        _handler = new AzureUpdatesJobHandler(
            _feedReaderMock.Object,
            _llmAnalyzerMock.Object,
            _cosmosDbMock.Object,
            logger.Object);
    }

    [Fact]
    public void JobType_ReturnsAzureUpdates()
    {
        _handler.JobType.Should().Be(CrawlJobTypes.AzureUpdates);
    }

    [Fact]
    public async Task HandleAsync_FirstRun_LooksBackSevenDays()
    {
        _cosmosDbMock
            .Setup(x => x.GetLatestFeedItemDateAsync(CrawlJobTypes.AzureUpdates, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTimeOffset?)null);

        _feedReaderMock
            .Setup(x => x.ReadFeedAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FeedItem>());

        var job = new CrawlJob { Id = "test-job-1", JobType = CrawlJobTypes.AzureUpdates };
        await _handler.HandleAsync(job);

        _feedReaderMock.Verify(x => x.ReadFeedAsync(
            It.Is<DateTimeOffset?>(d => d.HasValue && d.Value > DateTimeOffset.UtcNow.AddDays(-8)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_SubsequentRun_UsesLatestFeedItemDate()
    {
        var latestDate = new DateTimeOffset(2026, 5, 5, 0, 0, 0, TimeSpan.Zero);

        _cosmosDbMock
            .Setup(x => x.GetLatestFeedItemDateAsync(CrawlJobTypes.AzureUpdates, It.IsAny<CancellationToken>()))
            .ReturnsAsync(latestDate);

        _feedReaderMock
            .Setup(x => x.ReadFeedAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FeedItem>());

        var job = new CrawlJob { Id = "test-job-2", JobType = CrawlJobTypes.AzureUpdates };
        await _handler.HandleAsync(job);

        _feedReaderMock.Verify(x => x.ReadFeedAsync(
            It.Is<DateTimeOffset?>(d => d.HasValue && d.Value == latestDate),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_NewItems_AnalyzedAndStored()
    {
        _cosmosDbMock
            .Setup(x => x.GetLatestFeedItemDateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTimeOffset?)null);

        var feedItems = new List<FeedItem>
        {
            CreateFeedItem("item-1", "Redis TLS Retirement"),
            CreateFeedItem("item-2", "AKS v1.27 Deprecation"),
        };

        _feedReaderMock
            .Setup(x => x.ReadFeedAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(feedItems);

        // Items don't exist yet
        _cosmosDbMock
            .Setup(x => x.GetFeedItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FeedItem?)null);

        _cosmosDbMock
            .Setup(x => x.TryStoreFeedItemAsync(It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var analysis = new LlmAnalysis
        {
            ChangeType = ChangeTypes.Retirement,
            Severity = SeverityLevels.High,
            AiConfidence = 0.9
        };

        _llmAnalyzerMock
            .Setup(x => x.AnalyzeFeedItemAsync(It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(analysis);

        var job = new CrawlJob { Id = "test-job-3", JobType = CrawlJobTypes.AzureUpdates };
        await _handler.HandleAsync(job);

        // Both items should be analyzed
        _llmAnalyzerMock.Verify(x => x.AnalyzeFeedItemAsync(
            It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()), Times.Exactly(2));

        // Both items should be stored
        _cosmosDbMock.Verify(x => x.TryStoreFeedItemAsync(
            It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()), Times.Exactly(2));

        job.Result.Should().NotBeNull();
        job.Result!.NewItems.Should().Be(2);
        job.Result.TotalChecked.Should().Be(2);
        job.Result.SkippedItems.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_ExistingItems_SkippedNotAnalyzed()
    {
        _cosmosDbMock
            .Setup(x => x.GetLatestFeedItemDateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTimeOffset?)null);

        var feedItems = new List<FeedItem>
        {
            CreateFeedItem("existing-1", "Already Seen Update"),
        };

        _feedReaderMock
            .Setup(x => x.ReadFeedAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(feedItems);

        // Item already exists
        _cosmosDbMock
            .Setup(x => x.GetFeedItemAsync("existing-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(feedItems[0]);

        var job = new CrawlJob { Id = "test-job-4", JobType = CrawlJobTypes.AzureUpdates };
        await _handler.HandleAsync(job);

        // Should NOT be analyzed
        _llmAnalyzerMock.Verify(x => x.AnalyzeFeedItemAsync(
            It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()), Times.Never);

        job.Result.Should().NotBeNull();
        job.Result!.NewItems.Should().Be(0);
        job.Result.SkippedItems.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_MixedNewAndExisting_OnlyNewAnalyzed()
    {
        _cosmosDbMock
            .Setup(x => x.GetLatestFeedItemDateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTimeOffset?)null);

        var feedItems = new List<FeedItem>
        {
            CreateFeedItem("new-item", "New Update"),
            CreateFeedItem("existing-item", "Old Update"),
        };

        _feedReaderMock
            .Setup(x => x.ReadFeedAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(feedItems);

        _cosmosDbMock
            .Setup(x => x.GetFeedItemAsync("new-item", It.IsAny<CancellationToken>()))
            .ReturnsAsync((FeedItem?)null);
        _cosmosDbMock
            .Setup(x => x.GetFeedItemAsync("existing-item", It.IsAny<CancellationToken>()))
            .ReturnsAsync(feedItems[1]);

        _cosmosDbMock
            .Setup(x => x.TryStoreFeedItemAsync(It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _llmAnalyzerMock
            .Setup(x => x.AnalyzeFeedItemAsync(It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmAnalysis { ChangeType = ChangeTypes.Update, AiConfidence = 0.8 });

        var job = new CrawlJob { Id = "test-job-5", JobType = CrawlJobTypes.AzureUpdates };
        await _handler.HandleAsync(job);

        _llmAnalyzerMock.Verify(x => x.AnalyzeFeedItemAsync(
            It.Is<FeedItem>(f => f.Id == "new-item"), It.IsAny<CancellationToken>()), Times.Once);
        _llmAnalyzerMock.Verify(x => x.AnalyzeFeedItemAsync(
            It.Is<FeedItem>(f => f.Id == "existing-item"), It.IsAny<CancellationToken>()), Times.Never);

        job.Result!.NewItems.Should().Be(1);
        job.Result.SkippedItems.Should().Be(1);
        job.Result.TotalChecked.Should().Be(2);
    }

    [Fact]
    public async Task HandleAsync_SetsJobIdOnFeedItems()
    {
        _cosmosDbMock
            .Setup(x => x.GetLatestFeedItemDateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTimeOffset?)null);

        var feedItems = new List<FeedItem> { CreateFeedItem("item-x", "Test") };

        _feedReaderMock
            .Setup(x => x.ReadFeedAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(feedItems);

        _cosmosDbMock
            .Setup(x => x.GetFeedItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FeedItem?)null);
        _cosmosDbMock
            .Setup(x => x.TryStoreFeedItemAsync(It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _llmAnalyzerMock
            .Setup(x => x.AnalyzeFeedItemAsync(It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmAnalysis { AiConfidence = 0.5 });

        var job = new CrawlJob { Id = "my-job-id", JobType = CrawlJobTypes.AzureUpdates };
        await _handler.HandleAsync(job);

        _cosmosDbMock.Verify(x => x.TryStoreFeedItemAsync(
            It.Is<FeedItem>(f => f.CrawlJobId == "my-job-id"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_EmptyFeed_CompletesWithZeroResults()
    {
        _cosmosDbMock
            .Setup(x => x.GetLatestFeedItemDateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTimeOffset?)null);

        _feedReaderMock
            .Setup(x => x.ReadFeedAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FeedItem>());

        var job = new CrawlJob { Id = "test-empty", JobType = CrawlJobTypes.AzureUpdates };
        await _handler.HandleAsync(job);

        job.Result.Should().NotBeNull();
        job.Result!.NewItems.Should().Be(0);
        job.Result.TotalChecked.Should().Be(0);
        job.Result.SkippedItems.Should().Be(0);
    }

    private static FeedItem CreateFeedItem(string id, string title) => new()
    {
        Id = id,
        Source = CrawlJobTypes.AzureUpdates,
        Title = title,
        Link = $"https://azure.microsoft.com/updates/{id}",
        PublishDate = DateTimeOffset.UtcNow,
        Summary = $"Summary for {title}",
        RawContent = $"Content for {title}"
    };
}
