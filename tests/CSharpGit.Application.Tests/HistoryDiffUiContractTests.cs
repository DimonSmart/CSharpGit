namespace CSharpGit.Application.Tests;

public sealed class HistoryDiffUiContractTests
{
    [Fact]
    public void HistoryDetailsUseHierarchicalChangesBesideCompactDiff()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var changes = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.Changes.cs"));
        var tree = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "ChangedFileTreeNode.cs"));
        var diff = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "CompactDiffLine.cs"));

        Assert.Contains("x:Name=\"ChangedFilesTree\"", xaml);
        Assert.Contains("x:Name=\"CompactDiffList\"", xaml);
        Assert.Contains("Loaded=\"ChangesSurface_Loaded\"", xaml);
        Assert.Contains("OldLineNumber", xaml);
        Assert.Contains("NewLineNumber", xaml);
        Assert.Contains("DiffLineKindToBrushConverter", xaml);
        Assert.DoesNotContain("<PivotItem Header=\"Diff\">", xaml);
        Assert.DoesNotContain("ItemsSource=\"{Binding SelectedCommit.Files}\"", xaml);

        Assert.Contains("ChangedFileTreeNode.Build", changes);
        Assert.Contains("CompactDiffLine.Build", changes);
        Assert.Contains("_viewModel.SelectedFile = node.Entry.File", changes);
        Assert.Contains("while (entry is null && children.Count == 1", tree);
        Assert.Contains("children.Sum(child => child.AddedLines)", tree);
        Assert.Contains("children.Sum(child => child.RemovedLines)", tree);
        Assert.Contains("IsNoiseHeader", diff);
        Assert.Contains("TryReadHunkStarts", diff);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
