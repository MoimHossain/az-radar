using AzRadar.Shared.Services;
using FluentAssertions;

namespace AzRadar.Shared.Tests;

/// <summary>
/// Covers the dedup-id hashing used by the live Azure Updates crawl path.
/// These assertions were previously written against AzureUpdatesFeedReader, which was
/// superseded by the MRC MCP client and removed; the behaviour itself still matters
/// because the id is the Cosmos document id and therefore what makes the crawl idempotent.
/// </summary>
public class AzureUpdatesDedupIdTests
{
    [Fact]
    public void GenerateDedupId_SameInput_ReturnsSameHash()
    {
        var id1 = AzureUpdatesJobHandler.GenerateDedupId("https://example.com/update1");
        var id2 = AzureUpdatesJobHandler.GenerateDedupId("https://example.com/update1");

        id1.Should().Be(id2);
    }

    [Fact]
    public void GenerateDedupId_DifferentInputs_ReturnsDifferentHashes()
    {
        var id1 = AzureUpdatesJobHandler.GenerateDedupId("https://example.com/update1");
        var id2 = AzureUpdatesJobHandler.GenerateDedupId("https://example.com/update2");

        id1.Should().NotBe(id2);
    }

    [Fact]
    public void GenerateDedupId_CaseInsensitive()
    {
        var id1 = AzureUpdatesJobHandler.GenerateDedupId("HTTPS://EXAMPLE.COM/UPDATE1");
        var id2 = AzureUpdatesJobHandler.GenerateDedupId("https://example.com/update1");

        id1.Should().Be(id2);
    }

    [Fact]
    public void GenerateDedupId_TrimsWhitespace()
    {
        var id1 = AzureUpdatesJobHandler.GenerateDedupId("  https://example.com/update1  ");
        var id2 = AzureUpdatesJobHandler.GenerateDedupId("https://example.com/update1");

        id1.Should().Be(id2);
    }

    [Fact]
    public void GenerateDedupId_ReturnsFixedLength()
    {
        var id = AzureUpdatesJobHandler.GenerateDedupId("https://example.com/update1");

        id.Should().HaveLength(32);
        id.Should().MatchRegex("^[0-9a-f]{32}$");
    }

    [Fact]
    public void GenerateDedupId_IsStableAcrossRuns()
    {
        // Pinned so a change to the hashing scheme cannot silently orphan every
        // previously stored feed item and re-import the whole feed as "new".
        AzureUpdatesJobHandler.GenerateDedupId("https://example.com/update1")
            .Should().Be("9dfc75df49fafc32208546d8ca8367dc");
    }
}
