namespace CSharpGit.Desktop.Tests;

public sealed class DefaultBranchMarkerUiContractTests
{
    [Fact]
    public void RepositoryTreeUsesSameAccentForCurrentAndDefaultHomeMarkers()
    {
        var root = FindRepositoryRoot();
        var node = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "RepositoryTreeNode.cs"));
        var workspace = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Styles", "Workspace.xaml"));

        Assert.Contains("Value is GitBranch { IsDefault: true }", node, StringComparison.Ordinal);
        Assert.Contains("CurrentLocalBranchIconVisibility", node, StringComparison.Ordinal);
        Assert.Contains("LocalDefaultBranchIconVisibility", node, StringComparison.Ordinal);
        Assert.Contains("RemoteDefaultBranchIconVisibility", node, StringComparison.Ordinal);
        Assert.Equal(3, CountOccurrences(workspace, "Glyph=\"&#xE80F;\""));
        Assert.Contains("Visibility=\"{Binding CurrentLocalBranchIconVisibility}\"", workspace, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding LocalDefaultBranchIconVisibility}\"", workspace, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding RemoteDefaultBranchIconVisibility}\"", workspace, StringComparison.Ordinal);
        Assert.Equal(3, CountOccurrences(workspace, "Foreground=\"{ThemeResource AccentFillColorDefaultBrush}\""));
        Assert.DoesNotContain("Foreground=\"{ThemeResource TextFillColorSecondaryBrush}\"", workspace, StringComparison.Ordinal);
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
