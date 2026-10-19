using CSharpGit.Presentation.Core.History;

namespace CSharpGit.Desktop.Tests;

public sealed class HistoryReferenceBadgeLayoutTests
{
    private static readonly HistoryReferenceBadgeLayout.Metrics Long =
        new(32, 45, 145, 180, 25, 125);

    private static double Overflow(int hidden) => 12 + $"+{hidden}".Length * 8;

    [Fact]
    public void LongSingleBadgeUsesEllipsisInsteadOfPlusOne()
    {
        var plan = HistoryReferenceBadgeLayout.Calculate([Long], 78, Overflow);

        Assert.Single(plan.Visible);
        Assert.Equal(0, plan.HiddenCount);
        Assert.InRange(plan.Visible[0].NameWidth, 25, 125);
        Assert.True(plan.Visible[0].NameWidth < Long.FullName);
        Assert.False(plan.Visible[0].ShowUpstream);
    }

    [Fact]
    public void SufficientWidthShowsFullAnnotationAndCounters()
    {
        var plan = HistoryReferenceBadgeLayout.Calculate([Long], Long.Full, Overflow);

        Assert.Single(plan.Visible);
        Assert.True(plan.Visible[0].ShowCounters);
        Assert.True(plan.Visible[0].ShowUpstream);
        Assert.Equal(Long.FullName, plan.Visible[0].NameWidth);
        Assert.Equal(0, plan.HiddenCount);
    }

    [Fact]
    public void CountersAndReadableNameTakePrecedenceOverDifferentUpstreamName()
    {
        var plan = HistoryReferenceBadgeLayout.Calculate([Long], Long.Compact, Overflow);

        Assert.Single(plan.Visible);
        Assert.True(plan.Visible[0].ShowCounters);
        Assert.False(plan.Visible[0].ShowUpstream);
        Assert.Equal(Long.FullName, plan.Visible[0].NameWidth);
    }

    [Fact]
    public void HiddenCountOnlyIncludesCompletelyHiddenSuffix()
    {
        var badges = new[] { Long, Long, Long };
        var plan = HistoryReferenceBadgeLayout.Calculate(badges, 80, Overflow);

        Assert.Single(plan.Visible);
        Assert.Equal(2, plan.HiddenCount);
        Assert.Equal(Overflow(2), plan.OverflowWidth);
        Assert.True(plan.Visible[0].NameWidth >= Long.MinimumName);
        Assert.True(plan.Visible[0].Width + plan.OverflowWidth <= 80.01);
    }

    [Fact]
    public void OnlyEntireFittingOverflowIsDisplayed()
    {
        var badges = new[] { Long, Long, Long };
        var fallback = HistoryReferenceBadgeLayout.Calculate(badges, Overflow(3), Overflow);
        var noRoom = HistoryReferenceBadgeLayout.Calculate(badges, Overflow(3) - 1, Overflow);

        Assert.Empty(fallback.Visible);
        Assert.Equal(3, fallback.HiddenCount);
        Assert.Equal(Overflow(3), fallback.OverflowWidth);
        Assert.Empty(noRoom.Visible);
        Assert.Equal(3, noRoom.HiddenCount);
        Assert.Equal(0, noRoom.OverflowWidth);
    }

    [Fact]
    public void FullListNeverProducesPlusZero()
    {
        var plan = HistoryReferenceBadgeLayout.Calculate([Long, Long], 500, Overflow);

        Assert.Equal(2, plan.Visible.Count);
        Assert.Equal(0, plan.HiddenCount);
        Assert.Equal(0, plan.OverflowWidth);
    }

    [Fact]
    public void RepeatedLayoutIsDeterministicAndWiderLayoutNeverHidesMoreRefs()
    {
        var badges = new[] { Long, Long, Long, Long };
        var previousCount = 0;
        for (var width = 0; width <= 800; width += 5)
        {
            var plan = HistoryReferenceBadgeLayout.Calculate(badges, width, Overflow);
            var again = HistoryReferenceBadgeLayout.Calculate(badges, width, Overflow);
            Assert.Equal(plan.Visible.ToArray(), again.Visible.ToArray());
            Assert.Equal(plan.HiddenCount, again.HiddenCount);
            Assert.Equal(plan.OverflowWidth, again.OverflowWidth);
            Assert.True(plan.Visible.Count >= previousCount);
            previousCount = plan.Visible.Count;
        }
    }

    [Fact]
    public void EmptyReferencesHaveNeitherBadgesNorOverflow()
    {
        var plan = HistoryReferenceBadgeLayout.Calculate([], 0, Overflow);

        Assert.Empty(plan.Visible);
        Assert.Equal(0, plan.HiddenCount);
        Assert.Equal(0, plan.OverflowWidth);
    }

    [Fact]
    public void MinimumWidthsPreserveDifferentTypedBadgesInOriginalOrder()
    {
        var badges = new[]
        {
            new HistoryReferenceBadgeLayout.Metrics(18, 18, 18, 18, 8, 8),
            Long,
            new HistoryReferenceBadgeLayout.Metrics(60, 60, 75, 75, 12, 27)
        };
        var plan = HistoryReferenceBadgeLayout.Calculate(badges, 80, Overflow);

        Assert.Equal(2, plan.Visible.Count);
        Assert.Equal(1, plan.HiddenCount);
        Assert.Equal(8, plan.Visible[0].NameWidth);
        Assert.True(plan.Visible[1].NameWidth >= Long.MinimumName);
    }
}
