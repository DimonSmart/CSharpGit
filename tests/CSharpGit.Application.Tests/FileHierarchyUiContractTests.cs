namespace CSharpGit.Application.Tests;

public sealed class FileHierarchyUiContractTests
{
    [Fact]
    public void FileTreesUseSharedHierarchyChrome()
    {
        var root = FindRepositoryRoot();
        var main = Read(root, "src", "CSharpGit.Presentation", "MainPage.xaml");
        var files = Read(root, "src", "CSharpGit.Presentation", "MainPage.RepositoryFiles.cs");
        var repositoryTree = Read(root, "src", "CSharpGit.Presentation", "Styles", "RepositoryTree.xaml");
        var workspace = Read(root, "src", "CSharpGit.Presentation", "Styles", "Workspace.xaml");

        foreach (var key in new[]
                 {
                     "RepositoryFilesTreeItemTemplate",
                     "WorkingTreeTreeItemTemplate",
                     "ChangedFileTreeItemTemplate"
                 })
        {
            var template = Template(repositoryTree, key);
            Assert.Contains("controls:RepositoryTreeGuides", template);
            Assert.Contains("Segments=\"{Binding HierarchyGuideSegments}\"", template);
            Assert.Contains("TemplateSettings.Indentation.Left", template);
            Assert.Contains("HasChildren=\"{Binding HasChildren}\"", template);
            Assert.Contains("IsExpanded=\"{Binding IsExpanded, ElementName=", template);
            Assert.Contains("ExpandTarget=\"{Binding ElementName=", template);
        }

        var changedTemplate = Template(repositoryTree, "ChangedFileTreeItemTemplate");
        Assert.Contains("ColumnDefinitions=\"*,26,46,46\"", changedTemplate);
        Assert.Contains("Text=\"{Binding Status}\"", changedTemplate);
        Assert.Contains("Text=\"{Binding AddedDisplay}\"", changedTemplate);
        Assert.Contains("Text=\"{Binding RemovedDisplay}\"", changedTemplate);
        Assert.DoesNotContain("x:Key=\"ChangedFileTreeItemTemplate\"", workspace);

        foreach (var treeName in new[] { "ChangedFilesTree", "UnstagedChangesTree", "StagedChangesTree" })
        {
            var tree = TreeViewStartTag(main, treeName);
            Assert.Contains("ItemContainerStyle=\"{StaticResource DenseTreeItemStyle}\"", tree);
        }

        Assert.Contains(
            "ItemContainerStyle = (Style)Microsoft.UI.Xaml.Application.Current.Resources[\"DenseTreeItemStyle\"]",
            files);
        Assert.Contains(
            "ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Application.Current.Resources[\"RepositoryFilesTreeItemTemplate\"]",
            files);

        var denseStyle = Style(repositoryTree, "DenseTreeItemStyle");
        Assert.Contains("<Setter Property=\"CollapsedGlyph\" Value=\"\" />", denseStyle);
        Assert.Contains("<Setter Property=\"ExpandedGlyph\" Value=\"\" />", denseStyle);
        Assert.DoesNotContain("ChangedFileRowStyle", main);
        Assert.DoesNotContain("ChangedFileRowStyle", workspace);
    }

    private static string Template(string xaml, string key) =>
        Slice(xaml, $"<DataTemplate x:Key=\"{key}\">", "</DataTemplate>");

    private static string Style(string xaml, string key) =>
        Slice(xaml, $"<Style x:Key=\"{key}\"", "</Style>");

    private static string TreeViewStartTag(string xaml, string name)
    {
        var startMarker = $"<TreeView x:Name=\"{name}\"";
        var start = xaml.IndexOf(startMarker, StringComparison.Ordinal);
        var end = xaml.IndexOf("/>", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return xaml[start..(end + 2)];
    }

    private static string Slice(string value, string startMarker, string endMarker)
    {
        var start = value.IndexOf(startMarker, StringComparison.Ordinal);
        var end = value.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return value[start..(end + endMarker.Length)];
    }

    private static string Read(string root, params string[] parts)
    {
        var path = root;
        foreach (var part in parts) path = Path.Combine(path, part);
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
