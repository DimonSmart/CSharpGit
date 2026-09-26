namespace CSharpGit.Desktop.Tests;

public sealed class DefaultBranchMarkerUiContractTests
{
    [Fact]
    public void RepositoryTreeUsesHomeMarkerOnlyForDefaultBranches()
    {
        var root = FindRepositoryRoot();
        var node = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "RepositoryTreeNode.cs"));
        var repositoryTree = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Styles", "RepositoryTree.xaml"));

        Assert.Contains("Value is GitBranch { IsDefault: true }", node, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentLocalBranchIconVisibility", node, StringComparison.Ordinal);
        Assert.Contains("LocalDefaultBranchIconVisibility", node, StringComparison.Ordinal);
        Assert.Contains("RemoteDefaultBranchIconVisibility", node, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalBranch && !IsCurrent && Value is GitBranch { IsDefault: true }", node, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(repositoryTree, "Glyph=\"&#xE80F;\""));
        Assert.DoesNotContain("Visibility=\"{Binding CurrentLocalBranchIconVisibility}\"", repositoryTree, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding LocalDefaultBranchIconVisibility}\"", repositoryTree, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding RemoteDefaultBranchIconVisibility}\"", repositoryTree, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(repositoryTree, " Foreground=\"{ThemeResource AccentFillColorDefaultBrush}\""));
        Assert.DoesNotContain("Foreground=\"{ThemeResource TextFillColorSecondaryBrush}\"", repositoryTree, StringComparison.Ordinal);
    }

    [Fact]
    public void HistoryDefaultBranchMarkerUsesResolvedRemoteBranchAndKeepsCompactHeight()
    {
        var root = FindRepositoryRoot();
        var presenter = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "HistoryReferencesPresenter.cs"));
        var presentationContext = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "HistoryReferencePresentationContext.cs"));
        var historyStyles = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Styles", "HistoryReferences.xaml"));
        var app = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "App.xaml"));
        var defaultBranchState = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Git", "DefaultBranchRepositoryStateService.cs"));

        Assert.Contains("HistoryReferencePresentationContext.IsDefaultRemoteBranch(referenceName)", presenter, StringComparison.Ordinal);
        Assert.Contains(".Where(branch => branch.IsDefault)", presentationContext, StringComparison.Ordinal);
        Assert.Contains(".ToHashSet(StringComparer.Ordinal)", presentationContext, StringComparison.Ordinal);
        Assert.DoesNotContain("RemoteBranches.Any", presenter, StringComparison.Ordinal);
        Assert.DoesNotContain("VisualTreeHelper", presenter, StringComparison.Ordinal);
        Assert.DoesNotContain("origin/main", presenter, StringComparison.Ordinal);
        Assert.Contains("IsDefault = defaultRemoteBranch is not null", defaultBranchState, StringComparison.Ordinal);
        Assert.Contains("<Style x:Key=\"HistoryReferenceDefaultBranchIconStyle\" TargetType=\"FontIcon\">", historyStyles, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Glyph\" Value=\"&#xE80F;\" />", historyStyles, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Width\" Value=\"10\" />", historyStyles, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Height\" Value=\"10\" />", historyStyles, StringComparison.Ordinal);
        Assert.Contains("HistoryReferenceDefaultBranchIconStyle", presenter, StringComparison.Ordinal);
        Assert.Contains("<controls:HistoryReferencesPresenter", historyStyles, StringComparison.Ordinal);
        Assert.DoesNotContain("HistoryReferenceBadge", historyStyles, StringComparison.Ordinal);
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
