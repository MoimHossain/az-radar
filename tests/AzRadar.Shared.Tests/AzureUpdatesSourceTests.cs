using System.Net;
using System.Text;
using System.Text.Json;
using AzRadar.Shared.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AzRadar.Shared.Tests;

public class AzureUpdatesSourceTests
{
    private static readonly string FirstPage = AzureUpdatesSource.CatalogUrl + "?$orderby=id%20asc&$count=true";

    [Fact]
    public async Task GetUpdatesAsync_FollowsEveryPageAndUnionsRssWithFullDetails()
    {
        var first = Enumerable.Range(1, 100).Select(i => Item(i.ToString())).ToArray();
        var last = Enumerable.Range(101, 21).Select(i => Item(i.ToString())).ToArray();
        var next = "https://relcomms-prod-dagnegedescbeefs.b02.azurefd.net/api/v2/azure/?$orderby=id%20asc&$count=true&$skip=100";
        var handler = new StubHandler(new Dictionary<string, string>
        {
            [FirstPage] = Page(first, 121, next),
            [AzureUpdatesSource.NormalizeContinuation(next)] = Page(last, 121),
            [AzureUpdatesSource.RssUrl] = Rss("1", "122"),
            [AzureUpdatesSource.CatalogUrl + "122"] = JsonSerializer.Serialize(Item("122"))
        });
        using var source = CreateSource(handler);

        var snapshot = await source.GetUpdatesAsync();

        snapshot.CatalogCount.Should().Be(121);
        snapshot.RssCount.Should().Be(2);
        snapshot.Items.Should().HaveCount(122);
        snapshot.Items.Select(i => i.Id).Should().OnlyHaveUniqueItems();
        snapshot.Items.Should().Contain(i => i.Id == "122" && i.Description.Length > 200);
        snapshot.Items.Should().Contain(i => i.Id == "1" && i.Description.StartsWith("<p>"));
        handler.Requests.Should().HaveCount(4);
    }

    [Fact]
    public async Task GetUpdatesAsync_RefreshesDetailWhenRssIsNewerThanCatalogPage()
    {
        var handler = new StubHandler(new Dictionary<string, string>
        {
            [FirstPage] = Page([Item("123", "2026-08-01T00:00:00Z")], 1),
            [AzureUpdatesSource.RssUrl] = Rss("123"),
            [AzureUpdatesSource.CatalogUrl + "123"] = JsonSerializer.Serialize(Item("123"))
        });
        using var source = CreateSource(handler);

        var snapshot = await source.GetUpdatesAsync();

        snapshot.Items.Single().Modified.Should().Be("2026-09-03T17:17:08Z");
    }

    [Fact]
    public async Task GetUpdatesAsync_RssBomAndOriginalPublicationDateAreSupported()
    {
        var handler = new StubHandler(new Dictionary<string, string>
        {
            [FirstPage] = Page([Item("123")], 1),
            [AzureUpdatesSource.RssUrl] = "\uFEFF" + Rss("123")
        });
        using var source = CreateSource(handler);

        var snapshot = await source.GetUpdatesAsync();

        snapshot.Items.Single().Created.Should().Be("2026-06-08T00:00:00Z");
        snapshot.Items.Single().Modified.Should().Be("2026-09-03T17:17:08Z");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"value":[], "@odata.count":0}""")]
    [InlineData("""{"value":[]}""")]
    [InlineData("not json")]
    public async Task GetUpdatesAsync_MalformedOrEmptyCatalogFails(string payload)
    {
        using var source = CreateSource(new StubHandler(new() { [FirstPage] = payload }));

        var act = () => source.GetUpdatesAsync();

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task GetUpdatesAsync_MissingContinuationDoesNotSilentlyTruncate()
    {
        using var source = CreateSource(new StubHandler(new() { [FirstPage] = Page([Item("1")], 2) }));

        var act = () => source.GetUpdatesAsync();

        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*incomplete*");
    }

    [Fact]
    public async Task GetUpdatesAsync_DuplicateCatalogIdsFail()
    {
        using var source = CreateSource(new StubHandler(new() { [FirstPage] = Page([Item("1"), Item("1")], 2) }));

        var act = () => source.GetUpdatesAsync();

        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*repeated ID*");
    }

    [Fact]
    public async Task GetUpdatesAsync_RepeatedContinuationFails()
    {
        var next = AzureUpdatesSource.CatalogUrl + "?$skip=100";
        using var source = CreateSource(new StubHandler(new()
        {
            [FirstPage] = Page([Item("1")], 3, next),
            [next] = Page([Item("2")], 3, next)
        }));

        var act = () => source.GetUpdatesAsync();

        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*repeated a continuation*");
    }

    [Fact]
    public async Task GetUpdatesAsync_ChangingCountFails()
    {
        var next = AzureUpdatesSource.CatalogUrl + "?$skip=100";
        using var source = CreateSource(new StubHandler(new()
        {
            [FirstPage] = Page([Item("1")], 2, next),
            [next] = Page([Item("2")], 3)
        }));

        var act = () => source.GetUpdatesAsync();

        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*changed during enumeration*");
    }

    [Theory]
    [InlineData("https://example.com/api/v2/azure/?$skip=100")]
    [InlineData("http://www.microsoft.com/releasecommunications/api/v2/azure/?$skip=100")]
    [InlineData("https://www.microsoft.com/other?$skip=100")]
    [InlineData("https://user@www.microsoft.com/releasecommunications/api/v2/azure/?$skip=100")]
    public void NormalizeContinuation_RejectsUnexpectedTargets(string url)
    {
        var act = () => AzureUpdatesSource.NormalizeContinuation(url);

        act.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("<html>unavailable</html>")]
    [InlineData("<rss><channel /></rss>")]
    [InlineData("<rss><channel><item><title>Missing GUID</title></item></channel></rss>")]
    public async Task GetUpdatesAsync_MalformedRssFails(string rss)
    {
        using var source = CreateSource(new StubHandler(new()
        {
            [FirstPage] = Page([Item("1")], 1),
            [AzureUpdatesSource.RssUrl] = rss
        }));

        var act = () => source.GetUpdatesAsync();

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task GetUpdatesAsync_RssHttpFailureIsNotSuccessfulCatalogOnlyCrawl()
    {
        using var source = CreateSource(new StubHandler(new() { [FirstPage] = Page([Item("1")], 1) }));

        var act = () => source.GetUpdatesAsync();

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task GetUpdatesAsync_CancellationPropagates()
    {
        using var source = CreateSource(new StubHandler(new()));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => source.GetUpdatesAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData("123")]
    [InlineData("101-services-at-fedramp-high")]
    public void ParseCatalogItem_PreservesExistingIdScheme(string id)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(Item(id)));

        AzureUpdatesSource.ParseCatalogItem(doc.RootElement).Id.Should().Be(id);
    }

    private static AzureUpdatesSource CreateSource(StubHandler handler) =>
        new(new HttpClient(handler), NullLogger<AzureUpdatesSource>.Instance);

    private static object Item(string id, string modified = "2026-09-03T17:17:08Z") => new
    {
        id, title = $"Update {id}", description = "<p>" + new string('x', 1000) + "</p>",
        created = "2026-06-08T00:00:00Z", modified,
        products = new[] { "Azure SQL" }, tags = new[] { "Features" },
        productCategories = new[] { "Databases" }, status = "Launched"
    };

    private static string Page(object[] items, int count, string? next = null) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["value"] = items, ["@odata.count"] = count, ["@odata.nextLink"] = next
        });

    private static string Rss(params string[] ids) =>
        """<rss xmlns:a10="http://www.w3.org/2005/Atom"><channel>""" +
        string.Join("", ids.Select(id => $"""
            <item><guid isPermaLink="false">{id}</guid><title>Update {id}</title>
            <description>Short RSS summary</description>
            <pubDate>Mon, 08 Jun 2026 00:00:00 Z</pubDate>
            <a10:updated>2026-09-03T17:17:08Z</a10:updated></item>
            """)) + "</channel></rss>";

    private sealed class StubHandler(Dictionary<string, string> responses) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var url = request.RequestUri!.AbsoluteUri;
            Requests.Add(url);
            return Task.FromResult(responses.TryGetValue(url, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8) }
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }
}
