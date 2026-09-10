using CSharpGit.Application;
using CSharpGit.Domain;

namespace CSharpGit.Application.Tests;

public sealed class BranchDeletionResolverTests
{
    private static readonly GitRemote Origin = new("origin", "fetch", "push");

    [Fact]
    public void TrackingBranchWinsOverNameFallback()
    {
        var tracked = new GitBranch("local-name", "1", Upstream: "origin/feature/a");
        var sameName = new GitBranch("feature/a", "2");

        var target = BranchDeletionResolver.Resolve(
            new GitBranch("origin/feature/a", "3"),
            [sameName, tracked],
            [Origin]);

        Assert.Equal("origin", target.Remote.Name);
        Assert.Equal("feature/a", target.BranchName);
        Assert.Same(tracked, target.LocalBranch);
    }

    [Fact]
    public void FallsBackToMatchingLocalBranchName()
    {
        var local = new GitBranch("feature/a", "1");

        var target = BranchDeletionResolver.Resolve(
            new GitBranch("origin/feature/a", "2"),
            [local],
            [Origin]);

        Assert.Same(local, target.LocalBranch);
    }

    [Fact]
    public void UsesConfiguredRemotePrefixAndPreservesSlashes()
    {
        var nestedRemote = new GitRemote("origin/team", "fetch", "push");
        var local = new GitBranch("feature/test/delete", "1");

        var target = BranchDeletionResolver.Resolve(
            new GitBranch("origin/team/feature/test/delete", "2"),
            [local],
            [Origin, nestedRemote]);

        Assert.Equal("origin/team", target.Remote.Name);
        Assert.Equal("feature/test/delete", target.BranchName);
        Assert.Same(local, target.LocalBranch);
    }

    [Fact]
    public void ReturnsNullWhenCorrespondingLocalBranchDoesNotExist()
    {
        var target = BranchDeletionResolver.Resolve(
            new GitBranch("origin/feature/missing", "1"),
            [new GitBranch("other", "2")],
            [Origin]);

        Assert.Null(target.LocalBranch);
    }

    [Fact]
    public void KeepsCurrentFlagForUiToDisableLocalDeletion()
    {
        var current = new GitBranch("feature/a", "1", IsCurrent: true, Upstream: "origin/feature/a");

        var target = BranchDeletionResolver.Resolve(
            new GitBranch("origin/feature/a", "2"),
            [current],
            [Origin]);

        Assert.Same(current, target.LocalBranch);
        Assert.True(target.LocalBranch!.IsCurrent);
    }
}
