using CSharpGit.Presentation;

namespace CSharpGit.Desktop.Tests;

public sealed class RepositoryRefreshLifecycleStateTests
{
    [Fact]
    public void UnchangedStatusCheckKeepsMonitoringArmed()
    {
        var state = new RepositoryRefreshLifecycleState();
        state.Reset(10);

        var result = state.ApplyStatusCheckResult(10, changed: false);

        Assert.Equal(RepositoryStatusCheckDisposition.UpToDate, result);
        Assert.False(state.IsRefreshRequired);
        Assert.True(state.CanRunBackgroundStatusCheck);
    }

    [Fact]
    public void ChangedStatusCheckLatchesUntilNewBaseline()
    {
        var state = new RepositoryRefreshLifecycleState();
        state.Reset(10);

        var changed = state.ApplyStatusCheckResult(10, changed: true);
        var repeated = state.ApplyStatusCheckResult(10, changed: false);

        Assert.Equal(RepositoryStatusCheckDisposition.RefreshRequired, changed);
        Assert.Equal(RepositoryStatusCheckDisposition.IgnoredWhileLatched, repeated);
        Assert.True(state.IsRefreshRequired);
        Assert.False(state.CanRunBackgroundStatusCheck);

        Assert.True(state.PublishBaseline(11));
        Assert.False(state.IsRefreshRequired);
        Assert.True(state.CanRunBackgroundStatusCheck);
        Assert.Equal(11, state.BaselineRevision);
    }

    [Fact]
    public void MetadataStyleLatchUsesSameStickyLifecycle()
    {
        var state = new RepositoryRefreshLifecycleState();
        state.Reset(3);

        var result = state.Latch(3);

        Assert.Equal(RepositoryStatusCheckDisposition.RefreshRequired, result);
        Assert.True(state.IsRefreshRequired);
        Assert.False(state.CanRunBackgroundStatusCheck);
    }

    [Fact]
    public void OldStatusResultIsRejectedAfterBaselineRevisionAdvances()
    {
        var state = new RepositoryRefreshLifecycleState();
        state.Reset(10);
        Assert.True(state.PublishBaseline(11));

        var stale = state.ApplyStatusCheckResult(10, changed: true);

        Assert.Equal(RepositoryStatusCheckDisposition.StaleRevision, stale);
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
