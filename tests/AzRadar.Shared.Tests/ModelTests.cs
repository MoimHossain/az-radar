using AzRadar.Shared.Models;
using FluentAssertions;

namespace AzRadar.Shared.Tests;

public class ModelTests
{
    [Fact]
    public void CrawlJob_DefaultValues_AreCorrect()
    {
        var job = new CrawlJob();

        job.Id.Should().NotBeNullOrEmpty();
        job.Status.Should().Be(CrawlJobStatus.Pending);
        job.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        job.AttemptCount.Should().Be(0);
        job.StartedAt.Should().BeNull();
        job.CompletedAt.Should().BeNull();
        job.Result.Should().BeNull();
        job.Error.Should().BeNull();
    }

    [Fact]
    public void FeedItem_DefaultValues_AreCorrect()
    {
        var item = new FeedItem();

        item.Source.Should().BeEmpty();
        item.Categories.Should().BeEmpty();
        item.LlmAnalysis.Should().BeNull();
        item.FirstSeenAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void LlmAnalysis_DefaultValues_AreCorrect()
    {
        var analysis = new LlmAnalysis();

        analysis.AffectedServices.Should().BeEmpty();
        analysis.AffectedResourceTypes.Should().BeEmpty();
        analysis.MicrosoftDocLinks.Should().BeEmpty();
        analysis.AiConfidence.Should().Be(0.0);
    }

    [Fact]
    public void CrawlJobStatus_Constants_AreCorrect()
    {
        CrawlJobStatus.Pending.Should().Be("pending");
        CrawlJobStatus.Processing.Should().Be("processing");
        CrawlJobStatus.Completed.Should().Be("completed");
        CrawlJobStatus.Failed.Should().Be("failed");
    }

    [Fact]
    public void CrawlJobTypes_AzureUpdates_IsCorrect()
    {
        CrawlJobTypes.AzureUpdates.Should().Be("azure-updates");
    }

    [Fact]
    public void ChangeTypes_AllValues_AreLowercaseWithHyphens()
    {
        var types = new[]
        {
            ChangeTypes.Retirement, ChangeTypes.Deprecation, ChangeTypes.BreakingChange,
            ChangeTypes.SecurityAdvisory, ChangeTypes.NewFeature, ChangeTypes.MigrationRequired,
            ChangeTypes.Preview, ChangeTypes.GeneralAvailability, ChangeTypes.Update
        };

        foreach (var type in types)
        {
            type.Should().MatchRegex(@"^[a-z-]+$", "all change types should be lowercase with hyphens");
        }
    }

    [Fact]
    public void SeverityLevels_AllValues_AreLowercase()
    {
        var levels = new[]
        {
            SeverityLevels.Critical, SeverityLevels.High,
            SeverityLevels.Medium, SeverityLevels.Low, SeverityLevels.Informational
        };

        foreach (var level in levels)
        {
            level.Should().MatchRegex(@"^[a-z]+$", "all severity levels should be lowercase");
        }
    }
}
