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

    private static int CountBranchLeaves(RepositoryTreeDescriptor node) =>
        node.Children.Sum(child => child.Kind is RepositoryTreeNodeKind.LocalBranch or RepositoryTreeNodeKind.RemoteBranch
            ? 1
            : CountBranchLeaves(child));
}
