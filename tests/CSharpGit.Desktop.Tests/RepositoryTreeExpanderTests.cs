namespace CSharpGit.Desktop.Tests;

public sealed class RepositoryTreeExpanderTests
{
    [Fact]
    public void RepositoryTreeNodePublishesDerivedHasChildrenState()
    {
        var root = FindRepositoryRoot();
        var node = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CSharpGit.Presentation",
            "ViewModels",
            "RepositoryTreeNode.cs"));

        Assert.Contains("public bool HasChildren => Children.Count > 0;", node, StringComparison.Ordinal);
        Assert.Contains(
            "Children.CollectionChanged += (_, _) => Notify(nameof(HasChildren));",
            node,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CustomExpanderVisibilityUsesModelStateInsteadOfTreeViewItemsSource()
    {
        var root = FindRepositoryRoot();
        var guides = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CSharpGit.Presentation",
            "Controls",
            "RepositoryTreeGuides.cs"));
        var styles = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CSharpGit.Presentation",
            "Styles",
            "RepositoryTree.xaml"));
        var snapshotNode = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CSharpGit.Presentation",
            "ViewModels",
            "RepositorySnapshotTreeNode.cs"));
        var workingTreeNode = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CSharpGit.Presentation",
            "ViewModels",
            "WorkingTreeTreeNode.cs"));

        Assert.Contains("var guideWidth = segments.Count > 0", guides, StringComparison.Ordinal);
        Assert.Contains("if (HasChildren && RowHeight > 0)", guides, StringComparison.Ordinal);
        Assert.DoesNotContain("ExpandTarget?.ItemsSource", guides, StringComparison.Ordinal);
        Assert.Equal(4, CountOccurrences(styles, "HasChildren=\"{Binding HasChildren}\""));
        Assert.Contains("public bool HasChildren => Children.Count > 0;", snapshotNode, StringComparison.Ordinal);
        Assert.Contains("public bool HasChildren => Children.Count > 0;", workingTreeNode, StringComparison.Ordinal);
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
