using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Desktop.Tests;

public sealed class ChangedFileTreeNodeTests
{
    [Fact]
    public void BuildsCompactHierarchyAndAggregatesStatistics()
    {
        var roots = ChangedFileTreeNode.Build(
        [
            Entry("M", ".idd/intent/spec.md", 3, 1),
            Entry("M", "src/CSharpGit.Presentation/Controls/Widget.cs", 5, 2),
            Entry("A", "src/CSharpGit.Presentation/ViewModels/NewNode.cs", 12, 0),
            Entry("M", "src/App.cs", 2, 4),
            Entry("M", "README.md", 1, 1)
        ]);

        Assert.Equal(3, roots.Count);

        var intent = Assert.Single(roots, node => node.DisplayName == ".idd/intent");
        var spec = Assert.Single(intent.Children);
        Assert.Equal("spec.md", spec.DisplayName);
        Assert.Equal("M", spec.Status);
        Assert.Equal("+3", intent.AddedDisplay);
        Assert.Equal("-1", intent.RemovedDisplay);

        var src = Assert.Single(roots, node => node.DisplayName == "src");
        Assert.Equal(19, src.AddedLines);
        Assert.Equal(6, src.RemovedLines);
        Assert.Contains(src.Children, node => node.DisplayName == "CSharpGit.Presentation");
        Assert.Contains(src.Children, node => node.DisplayName == "App.cs");

        var presentation = Assert.Single(src.Children, node => node.DisplayName == "CSharpGit.Presentation");
        Assert.Equal(2, presentation.Children.Count);
        Assert.Contains(presentation.Children, node => node.DisplayName == "Controls");
        Assert.Contains(presentation.Children, node => node.DisplayName == "ViewModels");
    }

    [Fact]
    public void KeepsRootFilesAndDeduplicatesSamePath()
    {
        var roots = ChangedFileTreeNode.Build(
        [
            Entry("M", "README.md", 4, 2),
            Entry("M", "README.md", 99, 99)
        ]);

        var readme = Assert.Single(roots);
        Assert.Equal("README.md", readme.DisplayName);
        Assert.Equal("+4", readme.AddedDisplay);
        Assert.Equal("-2", readme.RemovedDisplay);
        Assert.Empty(readme.Children);
    }

    private static ChangedFileTreeEntry Entry(string status, string path, int added, int removed) =>
        new(status, new ChangedFile(path, added, removed, false));
}
