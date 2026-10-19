using CSharpGit.Domain;
using CSharpGit.Presentation.Core.History;

namespace CSharpGit.Desktop.Tests;

public sealed class HistoryReferenceBadgeContentTests
{
    [Theory]
    [InlineData(0, 0, "")]
    [InlineData(2, 0, "↑2")]
    [InlineData(0, 3, "↓3")]
    [InlineData(2, 3, "↑2 ↓3")]
    public void EquivalentUpstreamSuppressesNameButNeverTrackingCounters(int ahead, int behind, string expected)
    {
        var reference = new HistoryReferenceDecoration(
            "feature/login", HistoryReferenceKind.CurrentLocalBranch,
            Upstream: "team/feature/login", Ahead: ahead, Behind: behind)
        {
            IsSameNameRemoteUpstream = true
        };

        var content = HistoryReferenceBadgeContent.Create(reference);

        Assert.Equal(expected, content.Counters);
        Assert.Empty(content.Upstream);
    }

    [Fact]
    public void DifferentUpstreamIsOptionalAnnotationSeparateFromCounters()
    {
        var reference = new HistoryReferenceDecoration(
            "feature/login", HistoryReferenceKind.CurrentLocalBranch,
            Upstream: "origin/develop", Ahead: 1);

        var content = HistoryReferenceBadgeContent.Create(reference);

        Assert.Equal("↑1", content.Counters);
        Assert.Equal("· upstream origin/develop", content.Upstream);
    }

    [Theory]
    [InlineData(HistoryReferenceKind.LocalBranch)]
    [InlineData(HistoryReferenceKind.RemoteTrackingBranch)]
    [InlineData(HistoryReferenceKind.Tag)]
    [InlineData(HistoryReferenceKind.DetachedHead)]
    [InlineData(HistoryReferenceKind.Other)]
    public void OtherKindsNeverGetCurrentLocalTrackingAnnotation(HistoryReferenceKind kind)
    {
        var reference = new HistoryReferenceDecoration(
            "example", kind, Upstream: "origin/main", Ahead: 3, Behind: 2);
        var annotations = HistoryReferenceBadgeContent.Create(reference);

        Assert.Empty(annotations.Counters);
        Assert.Empty(annotations.Upstream);
    }

    [Fact]
    public void MissingUpstreamHasNoTrackingEvenIfCountsArePresent()
    {
        var reference = new HistoryReferenceDecoration(
            "main", HistoryReferenceKind.CurrentLocalBranch, Ahead: 2, Behind: 3);

        Assert.Equal(new HistoryReferenceBadgeContent.Annotations("", ""),
            HistoryReferenceBadgeContent.Create(reference));
    }
}
