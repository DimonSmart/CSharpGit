using CSharpGit.Domain;

namespace CSharpGit.Git.Tests;

public sealed class GitConfigGetParserTests
{
    [Fact]
    public void ParsesEmptyValueAndPreservesOriginAndWhitespace()
    {
        var empty = GitConfigGetParser.ParseOne("local\0file:/a path\0\0");
        Assert.Equal(GitConfigSource.Repository, empty.Scope);
        Assert.Equal("file:/a path", empty.Origin);
        Assert.Equal(string.Empty, empty.Value);

        var value = GitConfigGetParser.ParseOne("command\0command line:\0  first\r\nsecond\n\t \0");
        Assert.Equal(GitConfigSource.Command, value.Scope);
        Assert.Equal("  first\r\nsecond\n\t ", value.Value);
    }

    [Fact]
    public void ParsesMultipleValuesInOriginalOrderIncludingDuplicates()
    {
        var entries = GitConfigGetParser.ParseAll(
            "local\0file:/one\0first\0" +
            "global\0file:/two\0second\ncontinued\0" +
            "global\0file:/two\0second\ncontinued\0");

        Assert.Equal(3, entries.Count);
        Assert.Equal(new[] { "first", "second\ncontinued", "second\ncontinued" },
            entries.Select(entry => entry.Value));
        Assert.Equal(new[] { GitConfigSource.Repository, GitConfigSource.Global, GitConfigSource.Global },
            entries.Select(entry => entry.Scope));
    }

    [Theory]
    [InlineData("")]
    [InlineData("local\0file:/path\0secret")]
    [InlineData("local\0file:/path\0")]
    [InlineData("local\0file:/path\0secret\0extra\0")]
    [InlineData("local\0file:/path\0secret\0unknown\0")]
    [InlineData("\0file:/path\0secret\0")]
    [InlineData("unknown\0file:/path\0secret\0")]
    [InlineData("local\0\0secret\0")]
    public void RejectsMalformedAllWithoutExposingSensitiveInput(string output)
    {
        const string secret = "secret-unique-8976";
        var exception = Assert.Throws<FormatException>(
            () => GitConfigGetParser.ParseAll(output.Replace("secret", secret, StringComparison.Ordinal)));
        Assert.DoesNotContain(secret, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("file:/path", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void SingleReadRejectsMultipleRecordsWithoutLeakingValues()
    {
        const string secret = "secret-unique-8976";
        var exception = Assert.Throws<FormatException>(() => GitConfigGetParser.ParseOne(
            "local\0file:/first\0ok\0local\0file:/second\0" + secret + "\0"));
        Assert.DoesNotContain(secret, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void EmptySuccessfulSingleReadIsInvalid()
    {
        Assert.Throws<FormatException>(() => GitConfigGetParser.ParseOne(""));
        Assert.Throws<FormatException>(() => GitConfigGetParser.ParseAll(""));
    }
}
