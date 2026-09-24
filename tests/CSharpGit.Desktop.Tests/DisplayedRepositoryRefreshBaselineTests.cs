using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Desktop.Tests;

public sealed class DisplayedRepositoryRefreshBaselineTests
{
    [Fact]
    public void PublishingIdenticalFingerprintStillAdvancesRevision()
    {
        var baseline = new DisplayedRepositoryRefreshBaseline();
        var fingerprint = new RepositoryRefreshFingerprint("A");

        Assert.True(baseline.Publish(fingerprint));
        Assert.Equal(1, baseline.Revision);

        Assert.False(baseline.Publish(new RepositoryRefreshFingerprint("A")));

        Assert.Equal(2, baseline.Revision);
        Assert.Equal("A", baseline.Fingerprint?.Value);
    }

    [Fact]
    public void ClearDoesNotAdvanceRevision()
    {
        var baseline = new DisplayedRepositoryRefreshBaseline();
        baseline.Publish(new RepositoryRefreshFingerprint("A"));

        Assert.True(baseline.Clear());

        Assert.Null(baseline.Fingerprint);
        Assert.Equal(1, baseline.Revision);

        baseline.Publish(new RepositoryRefreshFingerprint("A"));
        Assert.Equal(2, baseline.Revision);
    }
}
