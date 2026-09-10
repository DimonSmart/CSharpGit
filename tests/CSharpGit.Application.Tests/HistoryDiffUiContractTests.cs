namespace CSharpGit.Application.Tests;

public sealed class HistoryDiffUiContractTests
{
    [Fact]
    public void HistoryDetailsUseHierarchicalChangesBesideCompactDiff()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var compactResources = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "CompactWorkspaceResources.xaml"));
        var changes = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.Changes.cs"));
        var tree = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "ChangedFileTreeNode.cs"));
        var diff = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "CompactDiffLine.cs"));
        var commitChangesSurface = ExtractCommitChangesSurface(xaml);

        Assert.Contains("x:Name=\"ChangedFilesTree\"", commitChangesSurface);
        Assert.Contains("x:Name=\"CompactDiffList\"", commitChangesSurface);
        Assert.Contains("Loaded=\"ChangesSurface_Loaded\"", commitChangesSurface);
        Assert.Contains("OldLineNumber", commitChangesSurface);
        Assert.Contains("NewLineNumber", commitChangesSurface);
        Assert.Contains("DiffLineKindToBrushConverter", xaml);
        Assert.DoesNotContain("<PivotItem Header=\"Diff\">", xaml);
        Assert.DoesNotContain("ItemsSource=\"{Binding SelectedCommit.Files}\"", xaml);

        Assert.Contains("x:Key=\"CompactDiffItemContainerStyle\"", compactResources);
        Assert.Contains("<ControlTemplate TargetType=\"ListViewItem\">", compactResources);
        Assert.Contains("<Setter Property=\"MinHeight\" Value=\"0\" />", compactResources);
        Assert.DoesNotContain("<Setter Property=\"Height\" Value=\"20\" />", compactResources);
        Assert.Contains("<Grid ColumnDefinitions=\"38,38,*\" Height=\"20\">", compactResources);

        Assert.Contains("ChangedFileTreeNode.Build", changes);
        Assert.Contains("CompactDiffLine.Build", changes);
        Assert.Contains("_viewModel.SelectedFile = node.Entry.File", changes);
        Assert.Contains("while (entry is null && children.Count == 1", tree);
        Assert.Contains("children.Sum(child => child.AddedLines)", tree);
        Assert.Contains("children.Sum(child => child.RemovedLines)", tree);
        Assert.Contains("IsNoiseHeader", diff);
        Assert.Contains("TryReadHunkStarts", diff);
    }

    [Fact]
    public void CommitChangesDiffUsesVerticalSpaceForDiffInsteadOfFilenameHeader()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var commitChangesSurface = ExtractCommitChangesSurface(xaml);

        Assert.Contains("<Grid Grid.Column=\"2\" RowDefinitions=\"Auto,*\">", commitChangesSurface);
        Assert.DoesNotContain("Text=\"{Binding SelectedFile.Path}\"", commitChangesSurface);
        Assert.DoesNotContain("RowDefinitions=\"36,22,*\"", commitChangesSurface);

        var oldHeader = commitChangesSurface.IndexOf("Text=\"OLD\"", StringComparison.Ordinal);
        var newHeader = commitChangesSurface.IndexOf("Text=\"NEW\"", StringComparison.Ordinal);
        var contentRow = commitChangesSurface.IndexOf("<Grid Grid.Row=\"1\">", StringComparison.Ordinal);
        var binaryState = commitChangesSurface.IndexOf("Title=\"Binary file\"", StringComparison.Ordinal);
        var compactDiff = commitChangesSurface.IndexOf("x:Name=\"CompactDiffList\"", StringComparison.Ordinal);

        Assert.True(oldHeader >= 0);
        Assert.True(newHeader > oldHeader);
        Assert.True(contentRow > newHeader);
        Assert.True(binaryState > contentRow);
        Assert.True(compactDiff > contentRow);
    }

    [Fact]
    public void CompactLayoutKeepsCommitDiffContentRowExpandable()
    {
        var root = FindRepositoryRoot();
        var compactLayout = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.CompactLayout.cs"));

        Assert.Contains("diffPane.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);", compactLayout);
        Assert.DoesNotContain("diffPane.RowDefinitions[1].Height = new GridLength(18);", compactLayout);
    }

    private static string ExtractCommitChangesSurface(string xaml)
    {
        const string startMarker = "<PivotItem x:Name=\"FilesTab\" Header=\"Changes\">";
        const string endMarker = "</PivotItem>";

        var start = xaml.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = xaml.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start);

        return xaml[start..(end + endMarker.Length)];
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
