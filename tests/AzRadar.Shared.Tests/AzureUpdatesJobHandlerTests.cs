using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Models;
using AzRadar.Shared.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace AzRadar.Shared.Tests;

public class AzureUpdatesJobHandlerTests
{
    private readonly Mock<IMrcMcpClient> _mrcClientMock;
    private readonly Mock<ILlmAnalyzer> _llmAnalyzerMock;
    private readonly Mock<ICosmosDbService> _cosmosDbMock;
    private readonly AzureUpdatesJobHandler _handler;

    public AzureUpdatesJobHandlerTests()
    {
        _mrcClientMock = new Mock<IMrcMcpClient>();
        _llmAnalyzerMock = new Mock<ILlmAnalyzer>();
        _cosmosDbMock = new Mock<ICosmosDbService>();
        var logger = new Mock<ILogger<AzureUpdatesJobHandler>>();

        _handler = new AzureUpdatesJobHandler(
            _mrcClientMock.Object,
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
    public async Task HandleAsync_NewUpdates_AnalyzedAndStored()
    {
        var updates = new List<AzureUpdateItem>
        {
            new() { Id = "redis-tls-retirement", Title = "Redis TLS Retirement", Description = "Redis TLS 1.0 retiring", Tags = ["Retirements"], Products = ["Azure Cache for Redis"], Modified = "2026-05-01T00:00:00Z" },
            new() { Id = "aks-deprecation", Title = "AKS v1.27 Deprecation", Description = "AKS 1.27 deprecated", Tags = ["Retirements"], Products = ["Azure Kubernetes Service"], Modified = "2026-05-02T00:00:00Z" },
        };

        _mrcClientMock
            .Setup(x => x.GetRecentAzureUpdatesAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updates);

        _cosmosDbMock
            .Setup(x => x.GetFeedItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FeedItem?)null);

        _cosmosDbMock
            .Setup(x => x.TryStoreFeedItemAsync(It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _llmAnalyzerMock
            .Setup(x => x.AnalyzeFeedItemAsync(It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmAnalysis { ChangeType = ChangeTypes.Retirement, Severity = SeverityLevels.High, AiConfidence = 0.9 });

        var job = new CrawlJob { Id = "test-job-3", JobType = CrawlJobTypes.AzureUpdates };
        await _handler.HandleAsync(job);

        _llmAnalyzerMock.Verify(x => x.AnalyzeFeedItemAsync(It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _cosmosDbMock.Verify(x => x.TryStoreFeedItemAsync(It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()), Times.Exactly(2));

        job.Result.Should().NotBeNull();
        job.Result!.NewItems.Should().Be(2);
        job.Result.TotalChecked.Should().Be(2);
        job.Result.SkippedItems.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_ExistingItems_SkippedNotAnalyzed()
    {
        var updates = new List<AzureUpdateItem>
        {
            new() { Id = "already-seen", Title = "Already Seen Update", Description = "Old", Modified = "2026-05-01T00:00:00Z" },
        };

        _mrcClientMock
            .Setup(x => x.GetRecentAzureUpdatesAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updates);

        // Item already exists
        _cosmosDbMock
            .Setup(x => x.GetFeedItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FeedItem { Id = "existing" });

        var job = new CrawlJob { Id = "test-job-4", JobType = CrawlJobTypes.AzureUpdates };
        await _handler.HandleAsync(job);

        _llmAnalyzerMock.Verify(x => x.AnalyzeFeedItemAsync(It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()), Times.Never);

        job.Result.Should().NotBeNull();
        job.Result!.NewItems.Should().Be(0);
        job.Result.SkippedItems.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_SetsJobIdOnFeedItems()
    {
        var updates = new List<AzureUpdateItem>
        {
            new() { Id = "test-update", Title = "Test", Description = "Test desc", Modified = "2026-05-01T00:00:00Z" },
        };

        _mrcClientMock
            .Setup(x => x.GetRecentAzureUpdatesAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updates);

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
    public async Task HandleAsync_EmptyResults_CompletesWithZero()
    {
        _mrcClientMock
            .Setup(x => x.GetRecentAzureUpdatesAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AzureUpdateItem>());

        var job = new CrawlJob { Id = "test-empty", JobType = CrawlJobTypes.AzureUpdates };
        await _handler.HandleAsync(job);

        job.Result.Should().NotBeNull();
        job.Result!.NewItems.Should().Be(0);
        job.Result.TotalChecked.Should().Be(0);
        job.Result.SkippedItems.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_EnrichesAnalysisWithMrcProducts()
    {
        var updates = new List<AzureUpdateItem>
        {
            new() { Id = "enrich-test", Title = "Test Update", Description = "desc",
                Products = ["Azure Cache for Redis", "Azure SQL"], Modified = "2026-05-01T00:00:00Z" },
        };

        _mrcClientMock
            .Setup(x => x.GetRecentAzureUpdatesAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updates);

        _cosmosDbMock
            .Setup(x => x.GetFeedItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FeedItem?)null);
        _cosmosDbMock
            .Setup(x => x.TryStoreFeedItemAsync(It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // LLM returns empty affectedServices — handler should enrich from MRC products
        _llmAnalyzerMock
            .Setup(x => x.AnalyzeFeedItemAsync(It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmAnalysis { AiConfidence = 0.8, AffectedServices = [] });

        var job = new CrawlJob { Id = "enrich-job", JobType = CrawlJobTypes.AzureUpdates };
        await _handler.HandleAsync(job);

        _cosmosDbMock.Verify(x => x.TryStoreFeedItemAsync(
            It.Is<FeedItem>(f => f.LlmAnalysis != null &&
                f.LlmAnalysis.AffectedServices.Contains("Azure Cache for Redis") &&
                f.LlmAnalysis.AffectedServices.Contains("Azure SQL")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void GenerateDedupId_SameInput_ReturnsSameHash()
    {
        var id1 = AzureUpdatesJobHandler.GenerateDedupId("redis-tls-retirement");
        var id2 = AzureUpdatesJobHandler.GenerateDedupId("redis-tls-retirement");
        id1.Should().Be(id2);
    }

    [Fact]
    public void GenerateDedupId_DifferentInputs_ReturnsDifferentHashes()
    {
        var id1 = AzureUpdatesJobHandler.GenerateDedupId("redis-tls-retirement");
        var id2 = AzureUpdatesJobHandler.GenerateDedupId("aks-deprecation");
        id1.Should().NotBe(id2);
    }
}
