using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Models;
using AzRadar.Shared.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace AzRadar.Shared.Tests;

public class MsLearnIntelligenceJobHandlerTests
{
    private readonly Mock<IMcpDocsClient> _mcpClientMock;
    private readonly Mock<ILlmAnalyzer> _llmAnalyzerMock;
    private readonly Mock<ICosmosDbService> _cosmosDbMock;
    private readonly MsLearnIntelligenceJobHandler _handler;

    public MsLearnIntelligenceJobHandlerTests()
    {
        _mcpClientMock = new Mock<IMcpDocsClient>();
        _llmAnalyzerMock = new Mock<ILlmAnalyzer>();
        _cosmosDbMock = new Mock<ICosmosDbService>();
        var logger = new Mock<ILogger<MsLearnIntelligenceJobHandler>>();

        _handler = new MsLearnIntelligenceJobHandler(
            _mcpClientMock.Object,
            _llmAnalyzerMock.Object,
            _cosmosDbMock.Object,
            logger.Object);
    }

    [Fact]
    public void JobType_ReturnsMsLearnIntelligence()
    {
        _handler.JobType.Should().Be(CrawlJobTypes.MsLearnIntelligence);
    }

    [Fact]
    public async Task HandleAsync_EmptyWatchlist_CompletesWithZeroResults()
    {
        _cosmosDbMock.Setup(x => x.GetWatchlistAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WatchlistItem>());

        var job = new CrawlJob { Id = "test-1", JobType = CrawlJobTypes.MsLearnIntelligence };
        await _handler.HandleAsync(job);

        job.Result.Should().NotBeNull();
        job.Result!.NewItems.Should().Be(0);
        job.Result.TotalChecked.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_WithWatchlist_SearchesForEachService()
    {
        var watchlist = new List<WatchlistItem>
        {
            new() { ServiceName = "Azure Kubernetes Service" },
            new() { ServiceName = "Azure Redis Cache" },
        };

        _cosmosDbMock.Setup(x => x.GetWatchlistAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(watchlist);

        _mcpClientMock.Setup(x => x.SearchDocsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<McpSearchResult>());

        var job = new CrawlJob { Id = "test-2", JobType = CrawlJobTypes.MsLearnIntelligence };
        await _handler.HandleAsync(job);

        // 2 services × 2 searches (targeted + broad) = 4 search calls
        _mcpClientMock.Verify(x => x.SearchDocsAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(4));
    }

    [Fact]
    public async Task HandleAsync_NewDoc_AnalyzesAndStores()
    {
        var watchlist = new List<WatchlistItem>
        {
            new() { ServiceName = "Azure Kubernetes Service" },
        };

        _cosmosDbMock.Setup(x => x.GetWatchlistAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(watchlist);

        var searchResults = new List<McpSearchResult>
        {
            new("AKS Retirement Notice", "https://learn.microsoft.com/aks/retirement", "AKS retirement info")
            { FullContent = "Full document content about AKS retirement" },
        };

        _mcpClientMock.Setup(x => x.SearchDocsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResults);

        // No existing insight
        _cosmosDbMock.Setup(x => x.GetDocInsightAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocInsight?)null);

        _cosmosDbMock.Setup(x => x.UpsertDocInsightAsync(It.IsAny<DocInsight>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _llmAnalyzerMock.Setup(x => x.AnalyzeFeedItemAsync(It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmAnalysis { ChangeType = ChangeTypes.Retirement, Severity = SeverityLevels.High, AiConfidence = 0.9 });

        var job = new CrawlJob { Id = "test-3", JobType = CrawlJobTypes.MsLearnIntelligence };
        await _handler.HandleAsync(job);

        _llmAnalyzerMock.Verify(x => x.AnalyzeFeedItemAsync(It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()), Times.Once);
        _cosmosDbMock.Verify(x => x.UpsertDocInsightAsync(It.IsAny<DocInsight>(), It.IsAny<CancellationToken>()), Times.Once);

        job.Result!.NewItems.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_UnchangedDoc_SkipsAnalysis()
    {
        var watchlist = new List<WatchlistItem>
        {
            new() { ServiceName = "Azure Redis Cache" },
        };

        _cosmosDbMock.Setup(x => x.GetWatchlistAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(watchlist);

        var docContent = "Redis TLS documentation content";
        var searchResults = new List<McpSearchResult>
        {
            new("Redis TLS Info", "https://learn.microsoft.com/redis/tls", "TLS info") { FullContent = docContent },
        };

        _mcpClientMock.Setup(x => x.SearchDocsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResults);

        // Existing insight with same content hash
        var existingInsight = new DocInsight
        {
            Id = DocInsight.GenerateId("https://learn.microsoft.com/redis/tls"),
            ContentHash = DocInsight.HashContent(docContent),
        };
        _cosmosDbMock.Setup(x => x.GetDocInsightAsync(existingInsight.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingInsight);

        var job = new CrawlJob { Id = "test-4", JobType = CrawlJobTypes.MsLearnIntelligence };
        await _handler.HandleAsync(job);

        // Should NOT analyze unchanged doc
        _llmAnalyzerMock.Verify(x => x.AnalyzeFeedItemAsync(It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()), Times.Never);

        job.Result!.SkippedItems.Should().Be(1);
        job.Result.NewItems.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_ChangedDoc_ReAnalyzes()
    {
        var watchlist = new List<WatchlistItem>
        {
            new() { ServiceName = "Azure Redis Cache" },
        };

        _cosmosDbMock.Setup(x => x.GetWatchlistAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(watchlist);

        var searchResults = new List<McpSearchResult>
        {
            new("Redis TLS Info", "https://learn.microsoft.com/redis/tls", "TLS info")
            { FullContent = "UPDATED Redis TLS documentation content with new deadline" },
        };

        _mcpClientMock.Setup(x => x.SearchDocsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResults);

        // Existing insight with DIFFERENT content hash
        var existingInsight = new DocInsight
        {
            Id = DocInsight.GenerateId("https://learn.microsoft.com/redis/tls"),
            ContentHash = "old-hash-that-differs",
            FirstSeenAt = DateTimeOffset.UtcNow.AddDays(-5),
        };
        _cosmosDbMock.Setup(x => x.GetDocInsightAsync(existingInsight.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingInsight);

        _cosmosDbMock.Setup(x => x.UpsertDocInsightAsync(It.IsAny<DocInsight>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _llmAnalyzerMock.Setup(x => x.AnalyzeFeedItemAsync(It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmAnalysis { ChangeType = ChangeTypes.Retirement, AiConfidence = 0.85 });

        var job = new CrawlJob { Id = "test-5", JobType = CrawlJobTypes.MsLearnIntelligence };
        await _handler.HandleAsync(job);

        // Should re-analyze because content changed
        _llmAnalyzerMock.Verify(x => x.AnalyzeFeedItemAsync(It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()), Times.Once);

        // Should preserve original firstSeenAt
        _cosmosDbMock.Verify(x => x.UpsertDocInsightAsync(
            It.Is<DocInsight>(d => d.FirstSeenAt == existingInsight.FirstSeenAt),
            It.IsAny<CancellationToken>()), Times.Once);

        job.Result!.NewItems.Should().Be(1);
    }
}

public class DocInsightModelTests
{
    [Fact]
    public void GenerateId_SameUrl_ReturnsSameHash()
    {
        var id1 = DocInsight.GenerateId("https://learn.microsoft.com/aks/retirement");
        var id2 = DocInsight.GenerateId("https://learn.microsoft.com/aks/retirement");
        id1.Should().Be(id2);
    }

    [Fact]
    public void GenerateId_DifferentUrls_ReturnsDifferentHashes()
    {
        var id1 = DocInsight.GenerateId("https://learn.microsoft.com/aks/retirement");
        var id2 = DocInsight.GenerateId("https://learn.microsoft.com/redis/retirement");
        id1.Should().NotBe(id2);
    }

    [Fact]
    public void GenerateId_CaseInsensitive()
    {
        var id1 = DocInsight.GenerateId("HTTPS://LEARN.MICROSOFT.COM/AKS/RETIREMENT");
        var id2 = DocInsight.GenerateId("https://learn.microsoft.com/aks/retirement");
        id1.Should().Be(id2);
    }

    [Fact]
    public void HashContent_SameContent_ReturnsSameHash()
    {
        var h1 = DocInsight.HashContent("some document content");
        var h2 = DocInsight.HashContent("some document content");
        h1.Should().Be(h2);
    }

    [Fact]
    public void HashContent_DifferentContent_ReturnsDifferentHash()
    {
        var h1 = DocInsight.HashContent("original content");
        var h2 = DocInsight.HashContent("updated content");
        h1.Should().NotBe(h2);
    }
}
