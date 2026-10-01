using AzRadar.Shared.Models;
using AzRadar.Shared.Services;
using FluentAssertions;

namespace AzRadar.Shared.Tests;

public class LifecycleDeadlineResolverTests
{
    [Theory]
    [InlineData("2028-09-30", "2028-09-30")]
    [InlineData("2028-09-30T00:00:00Z", "2028-09-30")]
    [InlineData("September 30, 2028", "2028-09-30")]
    [InlineData("30 September 2028", "2028-09-30")]
    [InlineData("Sep 30, 2028", "2028-09-30")]
    [InlineData("09/30/2028", "2028-09-30")]
    [InlineData("  2027-3-31 ", "2027-03-31")]
    public void Normalize_SupportedFormats_ReturnsIsoDate(string raw, string expected) =>
        LifecycleDeadlineResolver.Normalize(raw).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("TBD")]
    [InlineData("Q3 2027")]
    [InlineData("2027")]
    [InlineData("February 30, 2027")]
    public void Normalize_UnusableValues_ReturnsNull(string? raw) =>
        LifecycleDeadlineResolver.Normalize(raw).Should().BeNull();

    [Fact]
    public void ExtractFromText_ReturnsFirstMentionedDate()
    {
        var text = "Retirement: Basic SKU will be retired on 30 September 2028. Migrate before March 31, 2028.";

        LifecycleDeadlineResolver.ExtractFromText(text).Should().Be("2028-09-30");
    }

    [Fact]
    public void Resolve_PrefersLlmDeadline()
    {
        var analysis = new LlmAnalysis { ChangeType = ChangeTypes.Retirement, Deadline = "2029-03-31" };

        LifecycleDeadlineResolver.Resolve(analysis, "Retirement: X on September 30, 2028", null)
            .Should().Be(("2029-03-31", LifecycleDeadlineResolver.SourceLlm));
    }

    [Fact]
    public void Resolve_LifecycleChangeWithoutLlmDeadline_ExtractsFromTitleThenSummary()
    {
        var analysis = new LlmAnalysis { ChangeType = ChangeTypes.Retirement, Deadline = null };

        LifecycleDeadlineResolver.Resolve(analysis, "Retirement: Standard HDD", "Retires on 31 March 2028.")
            .Should().Be(("2028-03-31", LifecycleDeadlineResolver.SourceExtracted));
    }

    [Fact]
    public void Resolve_NonLifecycleChange_DoesNotExtract()
    {
        var analysis = new LlmAnalysis { ChangeType = ChangeTypes.GeneralAvailability, Deadline = null };

        LifecycleDeadlineResolver.Resolve(analysis, "Generally available: feature X", "Available since May 1, 2026.")
            .Should().Be(((string?)null, (string?)null));
    }

    [Fact]
    public void Resolve_RetirementWordingInTitle_ExtractsEvenWhenChangeTypeDiffers()
    {
        var analysis = new LlmAnalysis { ChangeType = ChangeTypes.Update, Deadline = "TBD" };

        LifecycleDeadlineResolver.Resolve(analysis, "Azure X will be retired on June 30, 2027", null)
            .Should().Be(("2027-06-30", LifecycleDeadlineResolver.SourceExtracted));
    }

    [Fact]
    public void Stamp_SetsAllDeadlineFields()
    {
        var item = new FeedItem
        {
            Title = "Retirement: Example",
            LlmAnalysis = new LlmAnalysis { ChangeType = ChangeTypes.Retirement, Deadline = "March 31, 2029" },
        };

        LifecycleDeadlineResolver.Stamp(item);

        item.LifecycleDeadline.Should().Be("2029-03-31");
        item.DeadlineSource.Should().Be(LifecycleDeadlineResolver.SourceLlm);
        item.DeadlineResolverVersion.Should().Be(LifecycleDeadlineResolver.Version);
    }

    [Fact]
    public void Stamp_WithoutAnalysis_ClearsDeadline()
    {
        var insight = new DocInsight { Title = "Retirement on May 1, 2027", LifecycleDeadline = "2020-01-01" };

        LifecycleDeadlineResolver.Stamp(insight);

        insight.LifecycleDeadline.Should().BeNull();
        insight.DeadlineResolverVersion.Should().Be(LifecycleDeadlineResolver.Version);
    }
}

public class LifecycleCalendarWindowTests
{
    private static readonly DateOnly Today = new(2026, 9, 26);

    [Fact]
    public void TryCreate_Defaults_CoverPast90DaysAndNext60Months()
    {
        LifecycleCalendarWindow.TryCreate(null, null, Today, out var window, out var error).Should().BeTrue();

        error.Should().BeNull();
        window.From.Should().Be("2026-06-28");
        window.To.Should().Be("2031-09-26");
    }

    [Theory]
    [InlineData("2026-13-01", null)]
    [InlineData(null, "next year")]
    [InlineData("2027-01-01", "2026-01-01")]
    [InlineData("2020-01-01", "2035-01-01")]
    public void TryCreate_InvalidInput_ReturnsError(string? from, string? to)
    {
        LifecycleCalendarWindow.TryCreate(from, to, Today, out _, out var error).Should().BeFalse();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TryCreate_ExplicitRange_IsUsed()
    {
        LifecycleCalendarWindow.TryCreate("2027-01-01", "2029-12-31", Today, out var window, out _).Should().BeTrue();

        window.Should().Be(new LifecycleCalendarWindow("2027-01-01", "2029-12-31"));
    }
}
