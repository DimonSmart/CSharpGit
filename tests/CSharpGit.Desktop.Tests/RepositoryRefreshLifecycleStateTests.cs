using CSharpGit.Presentation;

namespace CSharpGit.Desktop.Tests;

public sealed class RepositoryRefreshLifecycleStateTests
{
    [Fact]
    public void UnchangedProbeKeepsMonitoringArmed()
    {
        var state = new RepositoryRefreshLifecycleState();
        state.Reset(10);

        var result = state.ApplyProbeResult(10, changed: false);

        Assert.Equal(RepositoryProbeResultDisposition.Unchanged, result);
        Assert.False(state.IsRefreshRequired);
        Assert.True(state.CanQueueProbe);
    }

    [Fact]
    public void ChangedProbeLatchesUntilNewBaseline()
    {
        var state = new RepositoryRefreshLifecycleState();
        state.Reset(10);

        var changed = state.ApplyProbeResult(10, changed: true);
        var repeated = state.ApplyProbeResult(10, changed: false);

        Assert.Equal(RepositoryProbeResultDisposition.RefreshRequired, changed);
        Assert.Equal(RepositoryProbeResultDisposition.IgnoredWhileLatched, repeated);
        Assert.True(state.IsRefreshRequired);
        Assert.False(state.CanQueueProbe);

        Assert.True(state.PublishBaseline(11));
        Assert.False(state.IsRefreshRequired);
        Assert.True(state.CanQueueProbe);
        Assert.Equal(11, state.BaselineRevision);
    }

    [Fact]
    public void OldProbeResultIsRejectedAfterBaselineRevisionAdvances()
    {
        var state = new RepositoryRefreshLifecycleState();
        state.Reset(10);

        Assert.True(state.PublishBaseline(11));

        var stale = state.ApplyProbeResult(10, changed: true);

        Assert.Equal(RepositoryProbeResultDisposition.StaleRevision, stale);
        Assert.False(state.IsRefreshRequired);
        Assert.Equal(11, state.BaselineRevision);
    }

    [Fact]
    public void DuplicateOrOlderBaselinePublicationIsIgnored()
    {
        var state = new RepositoryRefreshLifecycleState();
        state.Reset(10);

        Assert.False(state.PublishBaseline(10));
        Assert.False(state.PublishBaseline(9));
        Assert.Equal(10, state.BaselineRevision);
    }
}
