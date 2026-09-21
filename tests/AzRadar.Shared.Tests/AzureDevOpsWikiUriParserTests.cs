using AzRadar.Shared.Services;

namespace AzRadar.Shared.Tests;

public sealed class AzureDevOpsWikiUriParserTests
{
    [Fact]
    public void TryParse_ExtractsPageFromBrowserUri()
    {
        var parsed = AzureDevOpsWikiUriParser.TryParse(
            "https://dev.azure.com/moim/Platform/_wiki/wikis/Platform.wiki/13/Azure-Service-Health",
            out var address,
            out var error);

        Assert.True(parsed, error);
        Assert.NotNull(address);
        Assert.Equal("moim", address.Organization);
        Assert.Equal("Platform", address.Project);
        Assert.Equal("Platform.wiki", address.WikiIdentifier);
        Assert.Equal(13, address.PageId);
        Assert.Equal("/Azure-Service-Health", address.PagePath);
    }

    [Theory]
    [InlineData("http://dev.azure.com/moim/Platform/_wiki/wikis/Platform.wiki/13/Page")]
    [InlineData("https://example.com/moim/Platform/_wiki/wikis/Platform.wiki/13/Page")]
    [InlineData("https://dev.azure.com/moim/Platform")]
    public void TryParse_RejectsUnsafeOrIncompleteUri(string value)
    {
        Assert.False(AzureDevOpsWikiUriParser.TryParse(value, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
