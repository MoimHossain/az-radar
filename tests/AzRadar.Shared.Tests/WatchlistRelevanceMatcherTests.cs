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

    [Theory]
    [InlineData("Cosmos", "Azure Cosmos DB")]
    [InlineData("Virtual Machines", "Azure Virtual Machines Dv2-series")]
    [InlineData("Virtual Machines", "Azure Disk Encryption")]
    [InlineData("Virtual Machines", "Standard HDD")]
    [InlineData("Key Vault", "Azure Key Vault Managed HSM")]
    [InlineData("Network Watcher", "NSG flow logs")]
    [InlineData("Azure Front Door", "Azure CDN from Microsoft (classic)")]
    [InlineData("App Service", "Azure Functions")]
    [InlineData("Azure Monitor", "Dependency Agent")]
    [InlineData("Azure Kubernetes Service", "AKS")]
    [InlineData("Azure Kubernetes Service", "Kubernetes")]
    [InlineData("Azure SQL Database", "Azure SQL Database Basic tier")]
    [InlineData("Azure Storage", "Azure Files")]
    [InlineData("Virtual Machines", "OS disks on Standard HDD")]
    [InlineData("Azure Monitor", "VM Insights Map and Dependency Agent")]
    [InlineData("Redis", "Azure Managed Redis")]
    [InlineData("Cosmos", "Azure Cosmos DB for PostgreSQL")]
    public void FindMatch_ContainmentOrFamily_Matches(string watched, string affected)
    {
        var watchlist = new[] { new WatchlistItem { ServiceName = watched } };

        WatchlistRelevanceMatcher.FindMatch(watchlist, [affected]).Should().BeSameAs(watchlist[0]);
    }

    [Theory]
    [InlineData("Azure SQL Database", "Database")]
    [InlineData("Virtual Machines", "Azure Virtual Network")]
    [InlineData("API Management", "Azure OpenAI")]
    [InlineData("Azure Storage", "Azure Service Bus")]
    [InlineData("Key Vault", "Azure Virtual Machines")]
    [InlineData("App Service", "Azure Service Bus")]
    [InlineData("Azure Data Factory", "Azure Data Explorer")]
    [InlineData("Azure Monitor", "Azure")]
    [InlineData("Azure Storage", "Azure NetApp Files")]
    [InlineData("Azure Files", "Azure NetApp Files")]
    public void FindMatch_GenericOrUnrelatedTokens_DoNotMatch(string watched, string affected)
    {
        var watchlist = new[] { new WatchlistItem { ServiceName = watched } };

        WatchlistRelevanceMatcher.FindMatch(watchlist, [affected]).Should().BeNull();
    }

    [Fact]
    public void FindMatch_FamilyMember_RespectsRegionScope()
    {
        var watchlist = new[]
        {
            new WatchlistItem { ServiceName = "Virtual Machines", Regions = ["West Europe"] }
        };

        WatchlistRelevanceMatcher.FindMatch(watchlist, ["Azure Disk Encryption"], ["East US"])
            .Should().BeNull();
        WatchlistRelevanceMatcher.FindMatch(watchlist, ["Azure Disk Encryption"], ["West Europe"])
            .Should().BeSameAs(watchlist[0]);
    }
}
