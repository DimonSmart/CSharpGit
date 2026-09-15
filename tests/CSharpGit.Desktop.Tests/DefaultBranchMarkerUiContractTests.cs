namespace CSharpGit.Desktop.Tests;

public sealed class DefaultBranchMarkerUiContractTests
{
    [Fact]
    public void RepositoryTreeUsesSameAccentForCurrentAndDefaultHomeMarkers()
    {
        var root = FindRepositoryRoot();
        var node = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "RepositoryTreeNode.cs"));
        var repositoryTree = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Styles", "RepositoryTree.xaml"));

        Assert.Contains("Value is GitBranch { IsDefault: true }", node, StringComparison.Ordinal);
        Assert.Contains("CurrentLocalBranchIconVisibility", node, StringComparison.Ordinal);
        Assert.Contains("LocalDefaultBranchIconVisibility", node, StringComparison.Ordinal);
        Assert.Contains("RemoteDefaultBranchIconVisibility", node, StringComparison.Ordinal);
        Assert.Equal(3, CountOccurrences(repositoryTree, "Glyph=\"&#xE80F;\""));
        Assert.Contains("Visibility=\"{Binding CurrentLocalBranchIconVisibility}\"", repositoryTree, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding LocalDefaultBranchIconVisibility}\"", repositoryTree, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding RemoteDefaultBranchIconVisibility}\"", repositoryTree, StringComparison.Ordinal);
        Assert.Equal(3, CountOccurrences(repositoryTree, "Foreground=\"{ThemeResource AccentFillColorDefaultBrush}\""));
        Assert.DoesNotContain("Foreground=\"{ThemeResource TextFillColorSecondaryBrush}\"", repositoryTree, StringComparison.Ordinal);
    }

    [Fact]
    public void HistoryDefaultBranchMarkerUsesResolvedRemoteBranchAndKeepsCompactHeight()
    {
        var root = FindRepositoryRoot();
        var badgeCode = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "HistoryReferenceBadge.xaml.cs"));
        var badgeXaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "HistoryReferenceBadge.xaml"));
        var historyStyles = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Styles", "HistoryReferences.xaml"));
        var app = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "App.xaml"));
        var defaultBranchState = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Git", "DefaultBranchRepositoryStateService.cs"));

        Assert.Contains("branch.IsDefault", badgeCode, StringComparison.Ordinal);
        Assert.Contains("string.Equals(branch.Name, ReferenceName, StringComparison.Ordinal)", badgeCode, StringComparison.Ordinal);
        Assert.DoesNotContain("origin/main", badgeCode, StringComparison.Ordinal);
        Assert.Contains("IsDefault = defaultRemoteBranch is not null", defaultBranchState, StringComparison.Ordinal);
        Assert.Contains("Glyph=\"&#xE80F;\"", badgeXaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"10\"", badgeXaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"10\"", badgeXaml, StringComparison.Ordinal);
        Assert.Contains("<controls:HistoryReferenceBadge ReferenceName=\"{Binding}\" />", historyStyles, StringComparison.Ordinal);
        Assert.Contains("HistoryReferences.xaml", app, StringComparison.Ordinal);
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
