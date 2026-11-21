using CSharpGit.Presentation.Controls;

namespace CSharpGit.Application.Tests;

public sealed class GitOutputFollowTailPolicyTests
{
    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(92, 100, true)]
    [InlineData(91.99, 100, false)]
    [InlineData(100, 100, true)]
    [InlineData(0, 100, false)]
    public void FollowTailRequiresViewportNearBottom(double offset, double height, bool expected) =>
        Assert.Equal(expected, GitOutputFollowTailPolicy.IsNearBottom(offset, height));

    [Theory]
    [InlineData(true, 5, true)]
    [InlineData(true, 0, false)]
    [InlineData(false, 5, false)]
    public void OnlyFocusedTextSelectionDefersUpdates(bool focused, int length, bool expected) =>
        Assert.Equal(expected, GitOutputFollowTailPolicy.ShouldDeferUpdate(focused, length));
}
