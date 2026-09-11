namespace CSharpGit.Desktop.Tests;

public sealed class CurrentBranchIndicatorUiContractTests
{
    [Fact]
    public void RepositoryTreeUsesBoldCurrentBranchWithoutCheckmark()
    {
        var root = FindRepositoryRoot();
        var node = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "RepositoryTreeNode.cs"));
        var workspace = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Styles", "Workspace.xaml"));

        Assert.Contains("DisplayName => Name", node, StringComparison.Ordinal);
        Assert.Contains("NameFontWeight => IsCurrent", node, StringComparison.Ordinal);
        Assert.Contains("FontWeights.Bold", node, StringComparison.Ordinal);
        Assert.DoesNotContain("✓", node, StringComparison.Ordinal);
        Assert.Contains("FontWeight=\"{Binding NameFontWeight}\"", workspace, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate CSharpGit repository root.");
    }
}
