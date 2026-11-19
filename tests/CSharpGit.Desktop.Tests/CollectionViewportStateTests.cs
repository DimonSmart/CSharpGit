using CSharpGit.Presentation.Controls;

namespace CSharpGit.Desktop.Tests;

public sealed class CollectionViewportStateTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(120)]
    public void FirstContentStartsAtTop(int count)
    {
        var state = new CollectionViewportState();

        Assert.Equal(CollectionViewportAdjustment.ResetToTop, state.Observe("commit:a", count));
    }

    [Fact]
    public void SwitchingLogicalContentResetsEvenWhenCountsMatch()
    {
        var state = new CollectionViewportState();
        state.Observe("commit:a", 200);

        Assert.Equal(CollectionViewportAdjustment.ResetToTop, state.Observe("commit:b", 200));
        Assert.Equal(CollectionViewportAdjustment.ResetToTop, state.Observe("stash:b", 200));
    }

    [Fact]
    public void ClearingListResetsAndLaterContentsDoNotUndoReset()
    {
        var state = new CollectionViewportState();
        state.Observe("commit:a", 100);

        Assert.Equal(CollectionViewportAdjustment.ResetToTop, state.Observe("commit:a", 0));
        Assert.Equal(CollectionViewportAdjustment.None, state.Observe("commit:a", 2));
    }

    [Fact]
    public void ShorteningSameContentClampsInsteadOfJumpingToStart()
    {
        var state = new CollectionViewportState();
        state.Observe("repo:a", 100);

        Assert.Equal(CollectionViewportAdjustment.ClampToContent, state.Observe("repo:a", 25));
        Assert.Equal(CollectionViewportAdjustment.ClampToContent, state.Observe("repo:a", 2));
    }

    [Fact]
    public void AppendingOrRefreshingSameContentPreservesPosition()
    {
        var state = new CollectionViewportState();
        state.Observe("repo:a", 25);

        Assert.Equal(CollectionViewportAdjustment.None, state.Observe("repo:a", 26));
        Assert.Equal(CollectionViewportAdjustment.None, state.Observe("repo:a", 26));
        Assert.Equal(CollectionViewportAdjustment.None, state.Observe("repo:a", 100));
    }

    [Fact]
    public void RepositorySwitchResetsViewportEvenWithSameItemCount()
    {
        var state = new CollectionViewportState();
        state.Observe("repo:a", 100);
        state.Observe("repo:a", 120);

        Assert.Equal(CollectionViewportAdjustment.ResetToTop, state.Observe("repo:b", 120));
        Assert.Equal(CollectionViewportAdjustment.ResetToTop, state.Observe("repo:a", 120));
    }

    [Fact]
    public void InvalidCountIsRejectedWithoutCorruptingPreviousState()
    {
        var state = new CollectionViewportState();
        state.Observe("repo:a", 25);

        Assert.Throws<ArgumentOutOfRangeException>(() => state.Observe("repo:a", -1));
        Assert.Equal(CollectionViewportAdjustment.ClampToContent, state.Observe("repo:a", 3));
    }
}
