using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Desktop.Tests;

public sealed class RepositoryTreeExpanderTests
{
    [Fact]
    public void HasChildrenTracksIncrementalChildCollectionChanges()
    {
        var parent = new RepositoryTreeNode(new RepositoryTreeDescriptor(
            "parent",
            RepositoryTreeNodeKind.Group,
            "Parent"));
        var child = new RepositoryTreeNode(new RepositoryTreeDescriptor(
            "child",
            RepositoryTreeNodeKind.LocalBranch,
            "Child"));
        var notifications = 0;
        parent.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(RepositoryTreeNode.HasChildren))
                notifications++;
        };

        Assert.False(parent.HasChildren);

        parent.Children.Add(child);

        Assert.True(parent.HasChildren);
        Assert.Equal(1, notifications);

        parent.Children.Remove(child);

        Assert.False(parent.HasChildren);
        Assert.Equal(2, notifications);
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

        Assert.Contains("var guideWidth = segments.Count > 0", guides, StringComparison.Ordinal);
        Assert.Contains("if (HasChildren && RowHeight > 0)", guides, StringComparison.Ordinal);
        Assert.DoesNotContain("ExpandTarget?.ItemsSource", guides, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(styles, "HasChildren=\"{Binding HasChildren}\""));
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
