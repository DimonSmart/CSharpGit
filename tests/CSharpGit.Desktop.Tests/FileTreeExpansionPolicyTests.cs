using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Desktop.Tests;

public sealed class FileTreeExpansionPolicyTests
{
    [Theory]
    [InlineData(99, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void UsesInclusiveAutoExpandLimit(int fileCount, bool expected)
    {
        Assert.Equal(expected, FileTreeExpansionPolicy.ShouldExpandByDefault(fileCount));
    }
}
