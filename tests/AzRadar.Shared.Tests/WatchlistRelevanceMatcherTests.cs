using AzRadar.Shared.Models;
using AzRadar.Shared.Services;
using FluentAssertions;

namespace AzRadar.Shared.Tests;

public class WatchlistRelevanceMatcherTests
{
    [Fact]
    public void FindMatch_AcronymAndCanonicalName_Matches()
    {
        var watchlist = new[]
        {
            new WatchlistItem { ServiceName = "AKS", Regions = ["West Europe"] }
        };

        var match = WatchlistRelevanceMatcher.FindMatch(
            watchlist,
            ["Azure Kubernetes Service"],
            ["West Europe"]);

        match.Should().BeSameAs(watchlist[0]);
    }

    [Fact]
    public void FindMatch_ReorderedServiceWords_Matches()
    {
        var watchlist = new[]
        {
            new WatchlistItem { ServiceName = "Azure Redis Cache" }
        };

        var match = WatchlistRelevanceMatcher.FindMatch(
            watchlist,
            ["Azure Cache for Redis"]);

        match.Should().BeSameAs(watchlist[0]);
    }

    [Fact]
    public void FindMatch_UnrelatedService_DoesNotMatch()
    {
        var watchlist = new[]
        {
            new WatchlistItem { ServiceName = "Azure Kubernetes Service" }
        };

        WatchlistRelevanceMatcher.FindMatch(
                watchlist,
                ["Artifact Streaming"])
            .Should().BeNull();
    }

    [Fact]
    public void FindMatch_UnscopedWatch_MatchesAnyRegion()
    {
        var watchlist = new[]
        {
            new WatchlistItem { ServiceName = "Azure App Service" }
        };

        WatchlistRelevanceMatcher.FindMatch(
                watchlist,
                ["App Service"],
                ["Australia East"])
            .Should().BeSameAs(watchlist[0]);
    }

    [Fact]
    public void FindMatch_GlobalUpdate_MatchesScopedWatch()
    {
        var watchlist = new[]
        {
            new WatchlistItem { ServiceName = "Azure App Service", Regions = ["North Europe"] }
        };

        WatchlistRelevanceMatcher.FindMatch(
                watchlist,
                ["Azure App Service"],
                ["global"])
            .Should().BeSameAs(watchlist[0]);
    }

    [Fact]
    public void FindMatch_NonOverlappingRegion_DoesNotMatch()
    {
        var watchlist = new[]
        {
            new WatchlistItem { ServiceName = "Azure App Service", Regions = ["North Europe"] }
        };

        WatchlistRelevanceMatcher.FindMatch(
                watchlist,
                ["Azure App Service"],
                ["East US"])
            .Should().BeNull();
    }
}
