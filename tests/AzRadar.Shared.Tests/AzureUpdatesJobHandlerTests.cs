using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Models;
using AzRadar.Shared.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace AzRadar.Shared.Tests;

public class AzureUpdatesJobHandlerTests
{
    private readonly Mock<IAzureUpdatesSource> _sourceMock;
    private readonly Mock<ILlmAnalyzer> _llmAnalyzerMock;
    private readonly Mock<ICosmosDbService> _cosmosDbMock;
    private readonly AzureUpdatesJobHandler _handler;

    public AzureUpdatesJobHandlerTests()
    {
        _sourceMock = new Mock<IAzureUpdatesSource>();
        _llmAnalyzerMock = new Mock<ILlmAnalyzer>();
        _cosmosDbMock = new Mock<ICosmosDbService>();
        var logger = new Mock<ILogger<AzureUpdatesJobHandler>>();

        _handler = new AzureUpdatesJobHandler(
            _sourceMock.Object,
            _llmAnalyzerMock.Object,
            _cosmosDbMock.Object,
            logger.Object);
        _cosmosDbMock
            .Setup(x => x.UpdateCrawlJobAsync(It.IsAny<CrawlJob>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CrawlJob job, CancellationToken _) => job);
        _cosmosDbMock
            .Setup(x => x.GetWatchlistAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new WatchlistItem { ServiceName = "Test Service" },
                new WatchlistItem { ServiceName = "Azure Kubernetes Service" },
                new WatchlistItem { ServiceName = "Azure Cache for Redis" },
                new WatchlistItem { ServiceName = "Azure SQL" }
            ]);
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

        _sourceMock
            .Setup(x => x.GetUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AzureUpdatesSnapshot(updates, updates.Count, 0));

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

        _sourceMock
            .Setup(x => x.GetUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AzureUpdatesSnapshot(updates, updates.Count, 0));

        // Item already exists
        _cosmosDbMock
            .Setup(x => x.GetFeedItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FeedItem
            {
                Id = "existing", SourceContentHash = AzureUpdatesJobHandler.GenerateContentHash(updates[0]),
                LlmAnalysis = new LlmAnalysis { AiConfidence = 0.9, AffectedServices = ["Test Service"] }
            });

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

        _sourceMock
            .Setup(x => x.GetUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AzureUpdatesSnapshot(updates, updates.Count, 0));

        _cosmosDbMock
            .Setup(x => x.GetFeedItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FeedItem?)null);
        _cosmosDbMock
            .Setup(x => x.TryStoreFeedItemAsync(It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _llmAnalyzerMock
            .Setup(x => x.AnalyzeFeedItemAsync(It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmAnalysis { AiConfidence = 0.5, AffectedServices = ["Test Service"] });

        var job = new CrawlJob { Id = "my-job-id", JobType = CrawlJobTypes.AzureUpdates };
        await _handler.HandleAsync(job);

        _cosmosDbMock.Verify(x => x.TryStoreFeedItemAsync(
            It.Is<FeedItem>(f => f.CrawlJobId == "my-job-id"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_EmptyResults_CompletesWithZero()
    {
        _sourceMock
            .Setup(x => x.GetUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AzureUpdatesSnapshot([], 0, 0));

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

        _sourceMock
            .Setup(x => x.GetUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AzureUpdatesSnapshot(updates, updates.Count, 0));

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
    public async Task HandleAsync_ChecksBeyondFiftyExistingItems()
    {
        var updates = Enumerable.Range(1, 121)
            .Select(i => new AzureUpdateItem
            {
                Id = i.ToString(), Title = $"Update {i}", Modified = "2026-09-01T00:00:00Z"
            }).ToList();
        _sourceMock.Setup(x => x.GetUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AzureUpdatesSnapshot(updates, 120, 10));
        _cosmosDbMock.Setup(x => x.GetFeedItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => new FeedItem
            {
                Id = id,
                SourceContentHash = AzureUpdatesJobHandler.GenerateContentHash(
                    updates.Single(u => AzureUpdatesJobHandler.GenerateDedupId(u.Id) == id)),
                LlmAnalysis = new LlmAnalysis { AiConfidence = 0.9, AffectedServices = ["Test Service"] }
            });

        var job = new CrawlJob();
        await _handler.HandleAsync(job);

        job.Result!.TotalChecked.Should().Be(121);
        job.Result.SkippedItems.Should().Be(121);
        _cosmosDbMock.Verify(x => x.GetFeedItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(121));
        _cosmosDbMock.Verify(x => x.UpdateCrawlJobAsync(job, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        _cosmosDbMock.Verify(x => x.StoreDiagnosticAsync(
            It.Is<JobDiagnosticEntry>(d => d.Step == "azure-updates-coverage" && d.ResultCount == 121),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_InsertConflictIsSkippedNotNew()
    {
        _sourceMock.Setup(x => x.GetUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AzureUpdatesSnapshot(
                [new() { Id = "123", Title = "Update", Modified = "2026-09-01T00:00:00Z" }], 1, 1));
        _llmAnalyzerMock.Setup(x => x.AnalyzeFeedItemAsync(It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmAnalysis { AffectedServices = ["Test Service"] });
        _cosmosDbMock.Setup(x => x.TryStoreFeedItemAsync(It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var job = new CrawlJob();
        await _handler.HandleAsync(job);

        job.Result!.NewItems.Should().Be(0);
        job.Result.SkippedItems.Should().Be(1);
        job.Result.TotalChecked.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_UnwatchedService_DeletesExistingItem()
    {
        var update = new AzureUpdateItem
        {
            Id = "artifact-streaming",
            Title = "Artifact Streaming update",
            Description = "Artifact Streaming is changing",
            Products = ["Artifact Streaming"],
            Modified = "2026-09-01T00:00:00Z"
        };
        _sourceMock.Setup(x => x.GetUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AzureUpdatesSnapshot([update], 1, 1));
        _cosmosDbMock.Setup(x => x.GetFeedItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FeedItem { Id = "existing", ETag = "etag" });
        _llmAnalyzerMock.Setup(x => x.AnalyzeFeedItemAsync(It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmAnalysis
            {
                AiConfidence = 0.9,
                AffectedServices = ["Artifact Streaming"]
            });

        var job = new CrawlJob();
        await _handler.HandleAsync(job);

        _cosmosDbMock.Verify(
            x => x.DeleteFeedItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _cosmosDbMock.Verify(
            x => x.TryReplaceFeedItemAsync(It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()),
            Times.Never);
        job.Result!.SkippedItems.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_SourceFailurePropagatesWithoutSuccessfulResult()
    {
        _sourceMock.Setup(x => x.GetUpdatesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("RSS unavailable"));
        var job = new CrawlJob();

        var act = () => _handler.HandleAsync(job);

        await act.Should().ThrowAsync<HttpRequestException>();
        job.Result.Should().BeNull();
        _cosmosDbMock.Verify(x => x.TryStoreFeedItemAsync(It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_UsesFullContentAndPublicationDate()
    {
        var update = new AzureUpdateItem
        {
            Id = "123", Title = "Update", Description = new string('x', 5000),
            Created = "2026-08-01T00:00:00Z", Modified = "2026-09-01T00:00:00Z",
            Link = "https://azure.microsoft.com/en-us/updates?id=123"
        };
        _sourceMock.Setup(x => x.GetUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AzureUpdatesSnapshot([update], 1, 1));
        _llmAnalyzerMock.Setup(x => x.AnalyzeFeedItemAsync(It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmAnalysis());

        await _handler.HandleAsync(new CrawlJob());

        _llmAnalyzerMock.Verify(x => x.AnalyzeFeedItemAsync(
            It.Is<FeedItem>(f => f.RawContent == update.Description &&
                f.PublishDate == DateTimeOffset.Parse(update.Created) && f.Link == update.Link),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_CancellationPropagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _sourceMock.Setup(x => x.GetUpdatesAsync(cts.Token))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var act = () => _handler.HandleAsync(new CrawlJob(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("old-source-hash")]
    public async Task HandleAsync_RefreshesLegacyOrRevisedPosts(string? previousHash)
    {
        var firstSeen = DateTimeOffset.Parse("2026-07-13T00:00:00Z");
        var update = new AzureUpdateItem
        {
            Id = "123", Title = "Revised retirement deadline", Description = "Full updated description",
            Created = "2026-06-01T00:00:00Z", Modified = "2026-09-03T00:00:00Z"
        };
        _sourceMock.Setup(x => x.GetUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AzureUpdatesSnapshot([update], 1, 1));
        _cosmosDbMock.Setup(x => x.GetFeedItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FeedItem
            {
                SourceContentHash = previousHash, FirstSeenAt = firstSeen, ETag = "etag",
                LlmAnalysis = new LlmAnalysis { AiConfidence = 0.8 }
            });
        _cosmosDbMock.Setup(x => x.TryReplaceFeedItemAsync(It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _llmAnalyzerMock.Setup(x => x.AnalyzeFeedItemAsync(It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmAnalysis { AiConfidence = 0.9, AffectedServices = ["Test Service"] });

        var job = new CrawlJob();
        await _handler.HandleAsync(job);

        job.Result!.UpdatedItems.Should().Be(1);
        job.Result.NewItems.Should().Be(0);
        job.Result.SkippedItems.Should().Be(0);
        job.Result.TotalChecked.Should().Be(1);
        _cosmosDbMock.Verify(x => x.TryReplaceFeedItemAsync(
            It.Is<FeedItem>(f => f.FirstSeenAt == firstSeen && f.ETag == "etag" &&
                f.RawContent == update.Description &&
                f.SourceContentHash == AzureUpdatesJobHandler.GenerateContentHash(update)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_ConcurrentRevisionFailsRatherThanOverwriting()
    {
        _sourceMock.Setup(x => x.GetUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AzureUpdatesSnapshot(
                [new() { Id = "123", Title = "Update", Modified = "2026-09-01T00:00:00Z" }], 1, 1));
        _cosmosDbMock.Setup(x => x.GetFeedItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FeedItem { ETag = "etag" });
        _llmAnalyzerMock.Setup(x => x.AnalyzeFeedItemAsync(It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmAnalysis { AiConfidence = 0.9, AffectedServices = ["Test Service"] });

        var act = () => _handler.HandleAsync(new CrawlJob());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*changed concurrently*");
    }

    [Fact]
    public async Task HandleAsync_RetriesPreviousFailedAnalysisEvenWithUnchangedSource()
    {
        var update = new AzureUpdateItem { Id = "123", Title = "Update", Modified = "2026-09-01T00:00:00Z" };
        _sourceMock.Setup(x => x.GetUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AzureUpdatesSnapshot([update], 1, 1));
        _cosmosDbMock.Setup(x => x.GetFeedItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FeedItem
            {
                SourceContentHash = AzureUpdatesJobHandler.GenerateContentHash(update),
                LlmAnalysis = new LlmAnalysis { AiConfidence = 0 }, ETag = "etag"
            });
        _cosmosDbMock.Setup(x => x.TryReplaceFeedItemAsync(It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _llmAnalyzerMock.Setup(x => x.AnalyzeFeedItemAsync(It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmAnalysis { AiConfidence = 0.9, AffectedServices = ["Test Service"] });

        await _handler.HandleAsync(new CrawlJob());

        _llmAnalyzerMock.Verify(x => x.AnalyzeFeedItemAsync(It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_BackfillStoresContentWithoutAnyLlmCalls()
    {
        var updates = Enumerable.Range(1, 101).Select(i => new AzureUpdateItem
        {
            Id = i.ToString(), Title = $"Update {i}", Description = "Full description",
            Created = "2020-01-01T00:00:00Z", Modified = "2026-09-01T00:00:00Z"
        }).ToList();
        _sourceMock.Setup(x => x.GetUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AzureUpdatesSnapshot(updates, 101, 20));
        _cosmosDbMock.Setup(x => x.TryStoreFeedItemAsync(It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var job = new CrawlJob { SkipLlmAnalysis = true };

        await _handler.HandleAsync(job);

        _llmAnalyzerMock.Verify(x => x.AnalyzeFeedItemAsync(It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _cosmosDbMock.Verify(x => x.TryStoreFeedItemAsync(
            It.Is<FeedItem>(f => f.LlmAnalysis == null && f.LlmAnalysisSkipped &&
                f.RawContent == "Full description" && f.SourceContentHash != null),
            It.IsAny<CancellationToken>()), Times.Exactly(101));
        _cosmosDbMock.Verify(x => x.UpdateCrawlJobAsync(job, It.IsAny<CancellationToken>()), Times.Exactly(2));
        job.Result!.NewItems.Should().Be(101);
        job.Result.TotalChecked.Should().Be(101);
    }

    [Fact]
    public async Task HandleAsync_NormalCrawlDoesNotAnalyzeUnchangedBackfill()
    {
        var update = new AzureUpdateItem { Id = "123", Title = "Historical item", Modified = "2026-09-01T00:00:00Z" };
        _sourceMock.Setup(x => x.GetUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AzureUpdatesSnapshot([update], 1, 1));
        _cosmosDbMock.Setup(x => x.GetFeedItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FeedItem
            {
                SourceContentHash = AzureUpdatesJobHandler.GenerateContentHash(update), LlmAnalysisSkipped = true
            });
        var job = new CrawlJob();

        await _handler.HandleAsync(job);

        _llmAnalyzerMock.Verify(x => x.AnalyzeFeedItemAsync(It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()),
            Times.Never);
        job.Result!.SkippedItems.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_NormalCrawlAnalyzesRevisedBackfill()
    {
        var update = new AzureUpdateItem { Id = "123", Title = "Revised deadline", Modified = "2026-09-01T00:00:00Z" };
        _sourceMock.Setup(x => x.GetUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AzureUpdatesSnapshot([update], 1, 1));
        _cosmosDbMock.Setup(x => x.GetFeedItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FeedItem { SourceContentHash = "old-content", LlmAnalysisSkipped = true, ETag = "etag" });
        _cosmosDbMock.Setup(x => x.TryReplaceFeedItemAsync(It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _llmAnalyzerMock.Setup(x => x.AnalyzeFeedItemAsync(It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmAnalysis { AiConfidence = 0.9, AffectedServices = ["Test Service"] });

        await _handler.HandleAsync(new CrawlJob());

        _cosmosDbMock.Verify(x => x.TryReplaceFeedItemAsync(
            It.Is<FeedItem>(f => !f.LlmAnalysisSkipped && f.LlmAnalysis != null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_BackfillMarksExistingFailedAnalysisAsIntentionalSkip()
    {
        var update = new AzureUpdateItem { Id = "123", Title = "Past item", Modified = "2026-09-01T00:00:00Z" };
        _sourceMock.Setup(x => x.GetUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AzureUpdatesSnapshot([update], 1, 1));
        _cosmosDbMock.Setup(x => x.GetFeedItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FeedItem
            {
                SourceContentHash = AzureUpdatesJobHandler.GenerateContentHash(update),
                LlmAnalysis = new LlmAnalysis { AiConfidence = 0 }, ETag = "etag"
            });
        _cosmosDbMock.Setup(x => x.TryReplaceFeedItemAsync(It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await _handler.HandleAsync(new CrawlJob { SkipLlmAnalysis = true });

        _llmAnalyzerMock.Verify(x => x.AnalyzeFeedItemAsync(It.IsAny<FeedItem>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _cosmosDbMock.Verify(x => x.TryReplaceFeedItemAsync(
            It.Is<FeedItem>(f => f.LlmAnalysisSkipped && f.LlmAnalysis == null),
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
