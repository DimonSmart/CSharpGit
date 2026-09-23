using CSharpGit.Application.Abstractions;

namespace CSharpGit.Git.Tests;

public sealed class GitProgressPolicyTests
{
    [Theory]
    [InlineData("fetch", true)]
    [InlineData("pull", true)]
    [InlineData("push", true)]
    [InlineData("clone", true)]
    [InlineData("checkout", true)]
    [InlineData("status", false)]
    [InlineData("log", false)]
    public void ProgressIsForcedOnlyForSupportedUserCommands(string subcommand, bool expected)
    {
        var result = GitProgressPolicy.Apply([subcommand, "origin"], GitCommandKind.User);

        Assert.Equal(expected, result.Contains("--progress"));
    }

    [Theory]
    [InlineData("--progress")]
    [InlineData("--no-progress")]
    [InlineData("--quiet")]
    [InlineData("-q")]
    public void ExplicitProgressOrQuietOptionPreventsForcedProgress(string option)
    {
        var arguments = new[] { "fetch", option, "origin" };

        var result = GitProgressPolicy.Apply(arguments, GitCommandKind.User);

        Assert.Equal(arguments, result);
        Assert.Equal(1, result.Count(value => value == option));
    }

    [Fact]
    public void NestedActionNamedLikeSupportedCommandDoesNotReceiveForcedProgress()
    {
        var arguments = new[] { "stash", "push" };

        var result = GitProgressPolicy.Apply(arguments, GitCommandKind.User);

        Assert.Equal(arguments, result);
        Assert.DoesNotContain("--progress", result);
    }

    [Fact]
    public void InternalCommandDoesNotReceiveForcedProgress()
    {
        var arguments = new[] { "fetch", "origin" };

        var result = GitProgressPolicy.Apply(arguments, GitCommandKind.Internal);

        Assert.Equal(arguments, result);
        Assert.DoesNotContain("--progress", result);
    }

    [Fact]
    public void ProgressIsInsertedImmediatelyAfterSubcommand()
    {
        var result = GitProgressPolicy.Apply(
            ["-c", "protocol.version=2", "fetch", "origin"],
            GitCommandKind.User);

        Assert.Equal(["-c", "protocol.version=2", "fetch", "--progress", "origin"], result);
    }
}
