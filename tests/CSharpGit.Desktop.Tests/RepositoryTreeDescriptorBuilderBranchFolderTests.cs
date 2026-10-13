using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Desktop.Tests;

public sealed class RepositoryTreeDescriptorBuilderBranchFolderTests
{
    [Fact]
    public void LocalBranchFoldersCarryStronglyTypedRelativePrefixesAndKeepStableKeys()
    {
        var roots = RepositoryTreeDescriptorBuilder.Build(
            [
                new GitBranch("feature/a", "1"),
                new GitBranch("feature/auth/login", "2"),
                new GitBranch("feature/auth/logout", "3")
            ],
            [],
            [],
            [],
            [],
            []);

        var branches = Assert.Single(roots, node => node.Key == RepositoryTreeDescriptorBuilder.BranchesRootKey);
        var feature = Assert.Single(branches.Children, node => node.Key == "branch-folder:local:feature");
        var featureInfo = Assert.IsType<BranchFolderInfo>(feature.Value);
        Assert.Equal(BranchFolderScope.Local, featureInfo.Scope);
        Assert.Equal("feature", featureInfo.Prefix);
        Assert.Null(featureInfo.RemoteName);
        Assert.Equal(3, CountBranchLeaves(feature));

        var auth = Assert.Single(feature.Children, node => node.Key == "branch-folder:local:feature/auth");
        var authInfo = Assert.IsType<BranchFolderInfo>(auth.Value);
        Assert.Equal(BranchFolderScope.Local, authInfo.Scope);
        Assert.Equal("feature/auth", authInfo.Prefix);
        Assert.Null(authInfo.RemoteName);
        Assert.Equal(2, CountBranchLeaves(auth));
    }

    [Fact]
    public void RemoteBranchFoldersCarryConfiguredRemoteSeparatelyFromRelativePrefix()
    {
        var roots = RepositoryTreeDescriptorBuilder.Build(
            [],
            [
                new GitBranch("origin/feature/a", "1"),
                new GitBranch("origin/feature/auth/login", "2")
            ],
            [new GitRemote("origin", "fetch", "push")],
            [],
            [],
            []);

        var remotes = Assert.Single(roots, node => node.Key == RepositoryTreeDescriptorBuilder.RemotesRootKey);
        var origin = Assert.Single(remotes.Children, node => node.Key == "remote:origin");
        var feature = Assert.Single(origin.Children, node => node.Key == "branch-folder:remote:origin:feature");
        var featureInfo = Assert.IsType<BranchFolderInfo>(feature.Value);
        Assert.Equal(BranchFolderScope.Remote, featureInfo.Scope);
        Assert.Equal("feature", featureInfo.Prefix);
        Assert.Equal("origin", featureInfo.RemoteName);
        Assert.DoesNotContain("origin/", featureInfo.Prefix, StringComparison.Ordinal);

        var auth = Assert.Single(feature.Children, node => node.Key == "branch-folder:remote:origin:feature/auth");
        var authInfo = Assert.IsType<BranchFolderInfo>(auth.Value);
        Assert.Equal(BranchFolderScope.Remote, authInfo.Scope);
        Assert.Equal("feature/auth", authInfo.Prefix);
        Assert.Equal("origin", authInfo.RemoteName);
        Assert.Equal(1, CountBranchLeaves(auth));
    }

    [Fact]
    public void LocalDefaultBranchAlwaysPrecedesAlphabeticalBranches()
    {
        var roots = RepositoryTreeDescriptorBuilder.Build(
            [
                new GitBranch("zebra", "1"),
                new GitBranch("develop", "2", IsCurrent: true),
                new GitBranch("production", "3") { IsDefault = true },
                new GitBranch("feature", "4"),
                new GitBranch("tools/extra", "5")
            ],
            [],
            [],
            [],
            [],
            []);

        var branches = Assert.Single(roots, node => node.Key == RepositoryTreeDescriptorBuilder.BranchesRootKey);
        Assert.Equal(
            ["production", "develop", "feature", "zebra", "tools"],
            branches.Children.Select(node => node.Name).ToArray());
    }

    [Fact]
    public void DefaultBranchTakesPriorityInsideLocalAndRemoteFolders()
    {
        var roots = RepositoryTreeDescriptorBuilder.Build(
            [
                new GitBranch("release/alpha", "1"),
                new GitBranch("release/stable", "2") { IsDefault = true },
                new GitBranch("release/beta", "3")
            ],
            [
                new GitBranch("origin/develop", "4"),
                new GitBranch("origin/trunk", "5") { IsDefault = true },
                new GitBranch("upstream/team/alpha", "6"),
                new GitBranch("upstream/team/stable", "7") { IsDefault = true }
            ],
            [
                new GitRemote("origin", "fetch", "push"),
                new GitRemote("upstream", "fetch", "push")
            ],
            [],
            [],
            []);

        var local = Assert.Single(roots, node => node.Key == RepositoryTreeDescriptorBuilder.BranchesRootKey);
        var release = Assert.Single(local.Children);
        Assert.Equal(
            ["stable", "alpha", "beta"],
            release.Children.Select(node => node.Name).ToArray());

        var remotes = Assert.Single(roots, node => node.Key == RepositoryTreeDescriptorBuilder.RemotesRootKey);
        var origin = Assert.Single(remotes.Children, node => node.Key == "remote:origin");
        Assert.Equal(
            ["trunk", "develop"],
            origin.Children.Select(node => node.Name).ToArray());

        var upstream = Assert.Single(remotes.Children, node => node.Key == "remote:upstream");
        var team = Assert.Single(upstream.Children);
        Assert.Equal(
            ["stable", "alpha"],
            team.Children.Select(node => node.Name).ToArray());
    }

    private static int CountBranchLeaves(RepositoryTreeDescriptor node) =>
        node.Children.Sum(child => child.Kind is RepositoryTreeNodeKind.LocalBranch or RepositoryTreeNodeKind.RemoteBranch
            ? 1
            : CountBranchLeaves(child));
}
