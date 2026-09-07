using System.IO.Compression;
using System.Text;
using AzRadar.Shared.Models;

namespace AzRadar.Shared.Services;

internal static class FeedItemContentCodec
{
    private const int CompressionThreshold = 64 * 1024;
    private const int MaximumExpandedBytes = 32 * 1024 * 1024;

    internal static FeedItem Encode(FeedItem item)
    {
        var document = item.CopyForStorage();
        // Summary is a preview, not a second copy of potentially megabytes of HTML.
        if (document.Summary.Length > 4000)
            document.Summary = document.Summary[..4000] + "...";
        var bytes = Encoding.UTF8.GetBytes(document.RawContent);
        if (bytes.Length > MaximumExpandedBytes)
            throw new InvalidDataException($"Feed item {item.Id} exceeds the 32 MiB source-content limit.");
        if (bytes.Length < CompressionThreshold)
            return document;

        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
            gzip.Write(bytes);
        document.RawContentGzip = Convert.ToBase64String(buffer.ToArray());
        if (document.RawContentGzip.Length > 1_500_000)
            throw new InvalidDataException($"Feed item {item.Id} remains too large for Cosmos DB after lossless compression.");
        document.RawContent = "";
        return document;
    }

    internal static FeedItem Decode(FeedItem item)
    {
        if (item.RawContentGzip == null)
            return item;

        using var input = new MemoryStream(Convert.FromBase64String(item.RawContentGzip));
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = gzip.Read(buffer)) > 0)
        {
            if (output.Length + count > MaximumExpandedBytes)
                throw new InvalidDataException($"Feed item {item.Id} has oversized compressed content.");
            output.Write(buffer, 0, count);
        }
        item.RawContent = Encoding.UTF8.GetString(output.ToArray());
        item.RawContentGzip = null;
        return item;
    }
}
