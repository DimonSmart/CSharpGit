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
            "CSharpGit.Presentation.Core",
            "ViewModels",
            "RepositorySnapshotTreeNode.cs"));
        var workingTreeNode = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CSharpGit.Presentation.Core",
            "ViewModels",
            "WorkingTreeTreeNode.cs"));

        Assert.Contains("var guideWidth = segments.Count > 0", guides, StringComparison.Ordinal);
        Assert.Contains("if (HasChildren && RowHeight > 0)", guides, StringComparison.Ordinal);
        Assert.DoesNotContain("ExpandTarget?.ItemsSource", guides, StringComparison.Ordinal);
        Assert.Equal(4, CountOccurrences(styles, "HasChildren=\"{Binding HasChildren}\""));
        Assert.Contains("public bool HasChildren => Children.Count > 0;", snapshotNode, StringComparison.Ordinal);
        Assert.Contains("public bool HasChildren => Children.Count > 0;", workingTreeNode, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryTreeCompensatesTheRootInsetWithoutChangingItemIndentation()
    {
        var root = FindRepositoryRoot();
        var page = System.Xml.Linq.XDocument.Load(Path.Combine(
            root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var tree = Assert.Single(page.Descendants().Where(element =>
            element.Name.LocalName == "TreeView"
            && (string?)element.Attribute(System.Xml.Linq.XName.Get(
                "Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "RepositoryTree"));

        Assert.Equal("-16,0,0,0", (string?)tree.Attribute("Margin"));
        Assert.Equal("{StaticResource DenseTreeViewStyle}", (string?)tree.Attribute("Style"));
        Assert.Equal("{StaticResource DenseTreeItemStyle}", (string?)tree.Attribute("ItemContainerStyle"));
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
