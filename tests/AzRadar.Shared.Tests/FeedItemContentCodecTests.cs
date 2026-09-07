using System.Text.Json;
using AzRadar.Shared.Models;
using AzRadar.Shared.Services;
using FluentAssertions;

namespace AzRadar.Shared.Tests;

public class FeedItemContentCodecTests
{
    [Fact]
    public void LargeHtml_RoundTripsLosslesslyWithoutDuplicatingTheSummary()
    {
        var content = string.Concat(Enumerable.Repeat("<table><tr><td>Archived update &amp; details</td></tr></table>", 20000));
        var item = new FeedItem
        {
            Id = "oversized", RawContent = content, Summary = content,
            SourceContentHash = "source-hash", LlmAnalysisSkipped = true, ETag = "etag"
        };

        var encoded = FeedItemContentCodec.Encode(item);

        encoded.RawContent.Should().BeEmpty();
        encoded.RawContentGzip.Should().NotBeNullOrEmpty();
        encoded.Summary.Length.Should().Be(4003);
        JsonSerializer.SerializeToUtf8Bytes(encoded).Length.Should().BeLessThan(2 * 1024 * 1024);
        item.RawContent.Should().Be(content);
        item.RawContentGzip.Should().BeNull();
        var decoded = FeedItemContentCodec.Decode(encoded);
        decoded.RawContent.Should().Be(content);
        decoded.RawContentGzip.Should().BeNull();
        decoded.SourceContentHash.Should().Be("source-hash");
        decoded.LlmAnalysisSkipped.Should().BeTrue();
        decoded.ETag.Should().Be("etag");
    }

    [Fact]
    public void SmallAndLegacyContent_RemainsReadableWithoutCompression()
    {
        var item = new FeedItem { RawContent = "Short announcement", Summary = "Summary" };

        var decoded = FeedItemContentCodec.Decode(FeedItemContentCodec.Encode(item));

        decoded.RawContent.Should().Be(item.RawContent);
        decoded.RawContentGzip.Should().BeNull();
        decoded.Summary.Should().Be("Summary");
    }

    [Fact]
    public void CorruptCompressedContent_FailsVisibly()
    {
        var act = () => FeedItemContentCodec.Decode(new FeedItem { RawContentGzip = "invalid base64!" });

        act.Should().Throw<FormatException>();
    }
}
