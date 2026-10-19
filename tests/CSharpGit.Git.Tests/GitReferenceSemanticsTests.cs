using CSharpGit.Domain;

namespace CSharpGit.Git.Tests;

public sealed class GitReferenceSemanticsTests
{
    private static readonly GitRemote[] Remotes =
    [
        new("origin", "", ""),
        new("team", "", ""),
        new("team/nested", "", "")
    ];

    [Theory]
    [InlineData("main", "refs/remotes/origin/main", true)]
    [InlineData("feature/login", "refs/remotes/team/feature/login", true)]
    [InlineData("feature/login", "refs/remotes/team/nested/feature/login", true)]
    [InlineData("feature/login", "refs/remotes/origin/login", false)]
    [InlineData("feature/login", "refs/remotes/origin/feature/Login", false)]
    [InlineData("feature/login", "refs/heads/feature/login", false)]
    [InlineData("feature/login", "refs/remotes/other/feature/login", false)]
    [InlineData("feature/login", "team/feature/login", false)]
    [InlineData("feature/login", "", false)]
    public void OnlyExactlyConfiguredRemoteTrackingUpstreamIsRedundant(
        string name, string fullUpstreamRef, bool redundant)
    {
        var branch = new GitBranch(name, "commit", Upstream: "presentation-only")
        {
            UpstreamReference = fullUpstreamRef
        };

        Assert.Equal(redundant,
            GitReferenceSemantics.IsSameNameRemoteUpstream(branch, Remotes));
    }

    [Fact]
    public void MissingFullReferenceIsNotGuessedFromShortUpstream()
    {
        var branch = new GitBranch("feature/login", "commit", Upstream: "team/feature/login");

        Assert.False(GitReferenceSemantics.IsSameNameRemoteUpstream(branch, Remotes));
    }

    [Fact]
    public void SimilarRemoteNameCannotBeTreatedAsAnIdenticalBranch()
    {
        var branch = new GitBranch("feature/login", "commit")
        {
            UpstreamReference = "refs/remotes/team/nested/other"
        };

        Assert.False(GitReferenceSemantics.IsSameNameRemoteUpstream(branch, Remotes));
    }
}
