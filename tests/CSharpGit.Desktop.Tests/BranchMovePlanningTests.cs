using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Desktop.Tests;

public sealed class BranchMovePlanningTests
{
    [Theory]
    [InlineData("feature/login", "bugfix", "bugfix/login")]
    [InlineData("feature/backend/login", "bugfix/ui", "bugfix/ui/login")]
    [InlineData("feature/backend/login", "", "login")]
    [InlineData("bugfix/login", "bugfix", "bugfix/login")]
    [InlineData("login", "", "login")]
    public void TargetNameReplacesOnlyFolderPrefix(string sourceName, string destinationPrefix, string expected)
    {
        var branch = new GitBranch(sourceName, "commit");

        var result = BranchMovePlanning.BuildTargetName(branch, destinationPrefix);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void OnlyLocalBranchWithGitBranchValueIsValidSource()
    {
        var branch = new GitBranch("feature/login", "commit");

        Assert.Same(
            branch,
            BranchMovePlanning.GetSourceBranch(RepositoryTreeNodeKind.LocalBranch, branch));
        Assert.Null(
            BranchMovePlanning.GetSourceBranch(RepositoryTreeNodeKind.RemoteBranch, branch));
        Assert.Null(
            BranchMovePlanning.GetSourceBranch(RepositoryTreeNodeKind.Tag, branch));
        Assert.Null(
            BranchMovePlanning.GetSourceBranch(RepositoryTreeNodeKind.LocalBranch, "feature/login"));
    }

    [Fact]
    public void BranchesRootAndLocalFolderAreValidDestinations()
    {
        Assert.True(BranchMovePlanning.TryGetDestinationPrefix(
            RepositoryTreeNodeKind.Group,
            RepositoryTreeDescriptorBuilder.BranchesRootKey,
            null,
            out var rootPrefix));
        Assert.Equal(string.Empty, rootPrefix);

        var localFolder = new BranchFolderInfo(BranchFolderScope.Local, "bugfix/ui", null);
        Assert.True(BranchMovePlanning.TryGetDestinationPrefix(
            RepositoryTreeNodeKind.BranchFolder,
            "branch-folder:local:bugfix/ui",
            localFolder,
            out var folderPrefix));
        Assert.Equal("bugfix/ui", folderPrefix);
    }

    [Fact]
    public void RemoteFolderAndOtherTreeNodesAreInvalidDestinations()
    {
        var remoteFolder = new BranchFolderInfo(BranchFolderScope.Remote, "bugfix/ui", "origin");

        Assert.False(BranchMovePlanning.TryGetDestinationPrefix(
            RepositoryTreeNodeKind.BranchFolder,
            "branch-folder:remote:origin:bugfix/ui",
            remoteFolder,
            out _));
        Assert.False(BranchMovePlanning.TryGetDestinationPrefix(
            RepositoryTreeNodeKind.Group,
            RepositoryTreeDescriptorBuilder.RemotesRootKey,
            null,
            out _));
        Assert.False(BranchMovePlanning.TryGetDestinationPrefix(
            RepositoryTreeNodeKind.LocalBranch,
            "local-branch:bugfix/login",
            new GitBranch("bugfix/login", "commit"),
            out _));
    }

    [Fact]
    public void OnlyExactBranchesRootIdentityIsAccepted()
    {
        Assert.False(BranchMovePlanning.TryGetDestinationPrefix(
            RepositoryTreeNodeKind.Group,
            "group:not-branches",
            null,
            out _));
        Assert.False(BranchMovePlanning.TryGetDestinationPrefix(
            RepositoryTreeNodeKind.BranchFolder,
            RepositoryTreeDescriptorBuilder.BranchesRootKey,
            new BranchFolderInfo(BranchFolderScope.Remote, "Branches", "origin"),
            out _));
    }
}
