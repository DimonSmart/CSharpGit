namespace CSharpGit.Desktop.Tests;

public sealed class CurrentBranchIndicatorUiContractTests
{
    [Fact]
    public void RepositoryTreeUsesAccentRowForCurrentBranchWithoutSelectionCoupling()
    {
        var root = FindRepositoryRoot();
        var node = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "RepositoryTreeNode.cs"));
        var workspace = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Styles", "Workspace.xaml"));
        var mainPage = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));

        Assert.Contains("DisplayName => Name", node, StringComparison.Ordinal);
        Assert.Contains("NameFontWeight => IsCurrent", node, StringComparison.Ordinal);
        Assert.Contains("FontWeights.Bold", node, StringComparison.Ordinal);
        Assert.Contains("CurrentBranchAccentVisibility => IsCurrent", node, StringComparison.Ordinal);
        Assert.Contains("CurrentLocalBranchIconVisibility", node, StringComparison.Ordinal);
        Assert.DoesNotContain("✓", node, StringComparison.Ordinal);

        Assert.Contains("FontWeight=\"{Binding NameFontWeight}\"", workspace, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(workspace, "Visibility=\"{Binding CurrentBranchAccentVisibility}\""));
        Assert.Contains("Background=\"{ThemeResource AccentFillColorDefaultBrush}\"", workspace, StringComparison.Ordinal);
        Assert.Contains("Opacity=\"0.12\"", workspace, StringComparison.Ordinal);
        Assert.Contains("Width=\"2\"", workspace, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding CurrentLocalBranchIconVisibility}\"", workspace, StringComparison.Ordinal);
        Assert.Contains("IsHitTestVisible=\"False\"", workspace, StringComparison.Ordinal);

        Assert.Contains("SelectionMode=\"Single\"", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("IsSelected=\"{Binding IsCurrent", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedItem=\"{Binding IsCurrent", mainPage, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
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
