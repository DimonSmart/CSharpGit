using CSharpGit.Domain;

namespace CSharpGit.Desktop.Tests;

public sealed class MainWindowTitleFormatterTests
{
    [Fact]
    public void WithoutRepositoryUsesApplicationTitle()
    {
        Assert.Equal("CSharpGit", MainWindowTitleFormatter.Format(null, null, null, false));
    }

    [Fact]
    public void BranchUsesRepositoryRootName()
    {
        var repository = CreateRepository(Path.Combine(Path.GetTempPath(), "CSharpGit"));

        Assert.Equal(
            "CSharpGit / main — CSharpGit",
            MainWindowTitleFormatter.Format(repository, "main", null, false));
    }

    [Fact]
    public void TrailingDirectorySeparatorIsIgnored()
    {
        var root = Path.Combine(Path.GetTempPath(), "MyRepo") + Path.DirectorySeparatorChar;
        var repository = CreateRepository(root);

        Assert.Equal(
            "MyRepo / feature/foo — CSharpGit",
            MainWindowTitleFormatter.Format(repository, "feature/foo", null, false));
    }

    [Fact]
    public void DetachedHeadUsesTenCharacterCommit()
    {
        var repository = CreateRepository(Path.Combine(Path.GetTempPath(), "MyRepo"));

        Assert.Equal(
            "MyRepo / detached @ a13f794c21 — CSharpGit",
            MainWindowTitleFormatter.Format(repository, null, "a13f794c21c3", true));
    }

    [Fact]
    public void DetachedHeadWithShortCommitIsSafe()
    {
        var repository = CreateRepository(Path.Combine(Path.GetTempPath(), "MyRepo"));

        Assert.Equal(
            "MyRepo / detached @ abc123 — CSharpGit",
            MainWindowTitleFormatter.Format(repository, null, "abc123", true));
    }

    [Fact]
    public void DetachedHeadWithoutCommitFallsBackToRepositoryName()
    {
        var repository = CreateRepository(Path.Combine(Path.GetTempPath(), "MyRepo"));

        Assert.Equal(
            "MyRepo — CSharpGit",
            MainWindowTitleFormatter.Format(repository, null, null, true));
    }

    [Fact]
    public void NormalHeadWithoutBranchFallsBackToRepositoryName()
    {
        var repository = CreateRepository(Path.Combine(Path.GetTempPath(), "MyRepo"));

        Assert.Equal(
            "MyRepo — CSharpGit",
            MainWindowTitleFormatter.Format(repository, null, "a13f794c21c3", false));
    }

    [Fact]
    public void UnbornBranchIsStillShown()
    {
        var repository = CreateRepository(Path.Combine(Path.GetTempPath(), "MyRepo"));

        Assert.Equal(
            "MyRepo / main — CSharpGit",
            MainWindowTitleFormatter.Format(repository, "main", null, false));
    }

    private static Repository CreateRepository(string root) =>
        new(root, root, Path.Combine(root, ".git"), false);
}
