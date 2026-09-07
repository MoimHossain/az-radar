using System.Net;
using AzRadar.Shared.Configuration;
using AzRadar.Shared.Models;
using AzRadar.Shared.Services;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace AzRadar.Shared.Tests;

public class AzureUpdatesPersistenceTests
{
    [Fact]
    public async Task GetFeedItemAsync_UsesResponseEtagForConditionalRevisions()
    {
        var container = new Mock<Container>();
        var response = new Mock<ItemResponse<FeedItem>>();
        response.SetupGet(x => x.Resource).Returns(new FeedItem { Id = "123" });
        response.SetupGet(x => x.ETag).Returns("current-etag");
        container.Setup(x => x.ReadItemAsync<FeedItem>(
                "123", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);
        var service = await CreateServiceAsync(container);

        var item = await service.GetFeedItemAsync("123");

        item!.ETag.Should().Be("current-etag");
    }

    [Fact]
    public async Task TryReplaceFeedItemAsync_UsesIfMatchEtag()
    {
        var container = new Mock<Container>();
        container.Setup(x => x.ReplaceItemAsync(
                It.IsAny<FeedItem>(), "123", It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<ItemResponse<FeedItem>>());
        var service = await CreateServiceAsync(container);
        var item = new FeedItem { Id = "123", ETag = "current-etag" };

        (await service.TryReplaceFeedItemAsync(item)).Should().BeTrue();

        container.Verify(x => x.ReplaceItemAsync(It.Is<FeedItem>(f => f.Id == "123"), "123", new PartitionKey("123"),
            It.Is<ItemRequestOptions>(o => o.IfMatchEtag == "current-etag"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(HttpStatusCode.PreconditionFailed, false)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    public async Task TryReplaceFeedItemAsync_OnlyHandlesConcurrencyConflicts(HttpStatusCode status, bool throws)
    {
        var container = new Mock<Container>();
        container.Setup(x => x.ReplaceItemAsync(
                It.IsAny<FeedItem>(), It.IsAny<string>(), It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("Failed", status, 0, "activity", 0));
        var service = await CreateServiceAsync(container);
        var item = new FeedItem { Id = "123", ETag = "current-etag" };
        var act = () => service.TryReplaceFeedItemAsync(item);

        if (throws)
            await act.Should().ThrowAsync<CosmosException>();
        else
            (await act()).Should().BeFalse();
    }

    [Fact]
    public async Task TryReplaceFeedItemAsync_RejectsMissingEtag()
    {
        var service = await CreateServiceAsync(new Mock<Container>());
        var act = () => service.TryReplaceFeedItemAsync(new FeedItem { Id = "123" });

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task HeartbeatPatchTouchesOnlyLivenessAndRequiresCurrentAttempt()
    {
        var container = new Mock<Container>();
        container.Setup(x => x.PatchItemAsync<CrawlJob>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<IReadOnlyList<PatchOperation>>(),
                It.IsAny<PatchItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<ItemResponse<CrawlJob>>());
        var service = await CreateServiceAsync(container);

        await service.HeartbeatCrawlJobAsync("job", 3, DateTimeOffset.UtcNow);

        container.Verify(x => x.PatchItemAsync<CrawlJob>("job", new PartitionKey("job"),
            It.Is<IReadOnlyList<PatchOperation>>(ops => ops.Count == 1 && ops[0].Path == "/lastHeartbeatAt"),
            It.Is<PatchItemRequestOptions>(o => o.FilterPredicate.Contains("c.status = 'processing'") &&
                o.FilterPredicate.Contains("c.attemptCount = 3")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProgressPatchCannotOverwriteConcurrentHeartbeat()
    {
        var container = new Mock<Container>();
        var response = new Mock<ItemResponse<CrawlJob>>();
        response.SetupGet(x => x.Resource).Returns(new CrawlJob());
        response.SetupGet(x => x.ETag).Returns("new-etag");
        container.Setup(x => x.PatchItemAsync<CrawlJob>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<IReadOnlyList<PatchOperation>>(),
                It.IsAny<PatchItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);
        var service = await CreateServiceAsync(container);

        var result = await service.UpdateCrawlJobAsync(new CrawlJob { Id = "job", AttemptCount = 2 });

        result.ETag.Should().Be("new-etag");
        container.Verify(x => x.PatchItemAsync<CrawlJob>("job", new PartitionKey("job"),
            It.Is<IReadOnlyList<PatchOperation>>(ops => ops.All(o => o.Path != "/lastHeartbeatAt") &&
                ops.Any(o => o.Path == "/lastProgressAt")),
            It.Is<PatchItemRequestOptions>(o => o.FilterPredicate.Contains("c.attemptCount = 2")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReadCompressedFeedItemReturnsOriginalContent()
    {
        var original = new string('x', 100000);
        var container = new Mock<Container>();
        var response = new Mock<ItemResponse<FeedItem>>();
        response.SetupGet(x => x.Resource).Returns(
            FeedItemContentCodec.Encode(new FeedItem { Id = "123", RawContent = original }));
        response.SetupGet(x => x.ETag).Returns("etag");
        container.Setup(x => x.ReadItemAsync<FeedItem>(
                "123", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);
        var service = await CreateServiceAsync(container);

        var item = await service.GetFeedItemAsync("123");

        item!.RawContent.Should().Be(original);
        item.RawContentGzip.Should().BeNull();
    }

    private static async Task<CosmosDbService> CreateServiceAsync(Mock<Container> container)
    {
        var client = new Mock<CosmosClient>();
        var database = new Mock<Database>();
        var dbResponse = new Mock<DatabaseResponse>();
        dbResponse.SetupGet(x => x.Database).Returns(database.Object);
        client.Setup(x => x.CreateDatabaseIfNotExistsAsync(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dbResponse.Object);
        var containerResponse = new Mock<ContainerResponse>();
        containerResponse.SetupGet(x => x.Container).Returns(container.Object);
        database.Setup(x => x.CreateContainerIfNotExistsAsync(
                It.IsAny<ContainerProperties>(), It.IsAny<int?>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(containerResponse.Object);
        var service = new CosmosDbService(client.Object, Options.Create(new CosmosDbSettings()),
            NullLogger<CosmosDbService>.Instance);
        await service.InitializeAsync();
        return service;
    }
}
