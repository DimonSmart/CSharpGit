using CSharpGit.Application;

namespace CSharpGit.Application.Tests;

public sealed class RepositoryNameResolverTests
{
    [Theory]
    [InlineData("https://github.com/DimonSmart/TocCreator.git", "TocCreator")]
    [InlineData("https://github.com/DimonSmart/TocCreator", "TocCreator")]
    [InlineData("git@github.com:DimonSmart/TocCreator.git", "TocCreator")]
    [InlineData("ssh://git@github.com/DimonSmart/TocCreator.git", "TocCreator")]
    [InlineData("https://github.com/DimonSmart/TocCreator.git?x=1#fragment", "TocCreator")]
    public void ResolvesRepositoryNameFromSupportedRemoteSyntax(
        string source,
        string expected)
    {
        Assert.Equal(expected, RepositoryNameResolver.Resolve(source));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://github.com/owner/.git")]
    public void ReturnsNullWhenSafeDirectoryNameCannotBeDerived(string? source)
    {
        Assert.Null(RepositoryNameResolver.Resolve(source));
    }
}
