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

    [Fact]
    public void ResolveRemoteForLocalUsesConfiguredUpstream()
    {
        var local = new GitBranch("feature/foo", "1", Upstream: "origin/feature/foo");

        var target = BranchDeletionResolver.ResolveRemoteForLocal(local, [], [Origin]);

        Assert.NotNull(target);
        Assert.Equal("origin", target.Remote.Name);
        Assert.Equal("feature/foo", target.BranchName);
    }

    [Fact]
    public void ResolveRemoteForLocalUsesLongestConfiguredRemotePrefix()
    {
        var nestedRemote = new GitRemote("company/origin", "fetch", "push");
        var local = new GitBranch(
            "add/db-schema-review-skill",
            "1",
            Upstream: "company/origin/add/db-schema-review-skill");

        var target = BranchDeletionResolver.ResolveRemoteForLocal(
            local,
            [],
            [Origin, nestedRemote]);

        Assert.NotNull(target);
        Assert.Equal("company/origin", target.Remote.Name);
        Assert.Equal("add/db-schema-review-skill", target.BranchName);
    }

    [Fact]
    public void ResolveRemoteForLocalAcceptsStaleConfiguredUpstream()
    {
        var local = new GitBranch("feature/foo/bar", "1", Upstream: "origin/feature/foo/bar");

        var target = BranchDeletionResolver.ResolveRemoteForLocal(
            local,
            [new GitBranch("origin/unrelated", "2")],
            [Origin]);

        Assert.NotNull(target);
        Assert.Equal("origin", target.Remote.Name);
        Assert.Equal("feature/foo/bar", target.BranchName);
    }

    [Fact]
    public void ResolveRemoteForLocalDoesNotFallbackWhenUpstreamRemoteIsNotConfigured()
    {
        var local = new GitBranch("feature/foo", "1", Upstream: "old-origin/feature/foo");

        var target = BranchDeletionResolver.ResolveRemoteForLocal(
            local,
            [new GitBranch("origin/feature/foo", "2")],
            [Origin]);

        Assert.Null(target);
    }

    [Fact]
    public void ResolveRemoteForLocalFallsBackToUniqueRemoteTrackingName()
    {
        var local = new GitBranch("feature/foo/bar", "1");

        var target = BranchDeletionResolver.ResolveRemoteForLocal(
            local,
            [new GitBranch("origin/feature/foo/bar", "2")],
            [Origin]);

        Assert.NotNull(target);
        Assert.Equal("origin", target.Remote.Name);
        Assert.Equal("feature/foo/bar", target.BranchName);
    }

    [Fact]
    public void ResolveRemoteForLocalReturnsNullWhenFallbackHasNoMatch()
    {
        var target = BranchDeletionResolver.ResolveRemoteForLocal(
            new GitBranch("feature/foo", "1"),
            [new GitBranch("origin/other", "2")],
            [Origin]);

        Assert.Null(target);
    }

    [Fact]
    public void ResolveRemoteForLocalReturnsNullWhenFallbackIsAmbiguous()
    {
        var upstream = new GitRemote("upstream", "fetch", "push");

        var target = BranchDeletionResolver.ResolveRemoteForLocal(
            new GitBranch("feature/foo", "1"),
            [
                new GitBranch("origin/feature/foo", "2"),
                new GitBranch("upstream/feature/foo", "3")
            ],
            [Origin, upstream]);

        Assert.Null(target);
    }

    [Fact]
    public void ResolveRemoteForLocalUpstreamWinsOverFallbackCandidates()
    {
        var upstream = new GitRemote("upstream", "fetch", "push");
        var local = new GitBranch("feature/foo", "1", Upstream: "upstream/feature/foo");

        var target = BranchDeletionResolver.ResolveRemoteForLocal(
            local,
            [
                new GitBranch("origin/feature/foo", "2"),
                new GitBranch("upstream/feature/foo", "3")
            ],
            [Origin, upstream]);

        Assert.NotNull(target);
        Assert.Equal("upstream", target.Remote.Name);
        Assert.Equal("feature/foo", target.BranchName);
    }

    [Fact]
    public void ResolveRemoteForLocalIgnoresFallbackWithUnconfiguredRemotePrefix()
    {
        var target = BranchDeletionResolver.ResolveRemoteForLocal(
            new GitBranch("feature/foo", "1"),
            [new GitBranch("old-origin/feature/foo", "2")],
            [Origin]);

        Assert.Null(target);
    }

    [Fact]
    public void ResolveRemoteForLocalRejectsEmptyRelativeBranchName()
    {
        var local = new GitBranch("feature/foo", "1", Upstream: "origin/");

        var target = BranchDeletionResolver.ResolveRemoteForLocal(local, [], [Origin]);

        Assert.Null(target);
    }
}
