namespace CSharpGit.Application.Tests;

public sealed class HistoryDiffUiContractTests
{
    [Fact]
    public void HistoryDetailsUseHierarchicalChangesBesideSharedDenseDiff()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var workspace = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Styles", "Workspace.xaml"));
        var changes = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.Changes.cs"));
        var tree = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "ChangedFileTreeNode.cs"));
        var diff = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "CompactDiffLine.cs"));
        var commitChangesSurface = ExtractCommitChangesSurface(xaml);

        Assert.Contains("x:Name=\"ChangedFilesTree\"", commitChangesSurface);
        Assert.Contains("x:Name=\"CompactDiffList\"", commitChangesSurface);
        Assert.Contains("Loaded=\"ChangesSurface_Loaded\"", commitChangesSurface);
        Assert.Contains("ItemContainerStyle=\"{StaticResource DiffRowStyle}\"", commitChangesSurface);
        Assert.Contains("ItemTemplate=\"{StaticResource DiffItemTemplate}\"", commitChangesSurface);
        Assert.DoesNotContain("<PivotItem Header=\"Diff\">", xaml);
        Assert.DoesNotContain("ItemsSource=\"{Binding SelectedCommit.Files}\"", xaml);

        Assert.Contains("x:Key=\"DiffRowStyle\"", workspace);
        Assert.Contains("<ControlTemplate TargetType=\"ListViewItem\">", workspace);
        Assert.Contains("Value=\"{StaticResource Height.DiffRow}\"", workspace);
        Assert.Contains("x:Key=\"DiffItemTemplate\"", workspace);
        Assert.Contains("ColumnDefinitions=\"38,38,*\"", workspace);
        Assert.Contains("OldLineNumber", workspace);
        Assert.Contains("NewLineNumber", workspace);
        Assert.Contains("DiffLineKindToBrushConverter", workspace);

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
    public void CommitAndWorkingTreeDiffsShareOneProductionTemplate()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var workspace = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Styles", "Workspace.xaml"));

        Assert.Equal(2, Count(xaml, "ItemTemplate=\"{StaticResource DiffItemTemplate}\""));
        Assert.Equal(2, Count(xaml, "ItemContainerStyle=\"{StaticResource DiffRowStyle}\""));
        Assert.Equal(1, Count(workspace, "x:Key=\"DiffItemTemplate\""));
        Assert.DoesNotContain("CompactDiffItemTemplate", xaml);
        Assert.DoesNotContain("CompactDiffItemTemplate", workspace);
    }

    [Fact]
    public void CommitAndDiffLoadsClearStaleStateAndKeepLoadingOwnedByCurrentGeneration()
    {
        var root = FindRepositoryRoot();
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.cs"));
        var commitLoad = ExtractBetween(viewModel, "private async Task LoadCommitAsync()", "private async Task LoadDiffAsync()");
        var diffLoad = ExtractBetween(viewModel, "private async Task LoadDiffAsync()", "private void Notify(");

        Assert.Contains("private bool _isCommitLoading;", viewModel);
        Assert.Contains("private bool _isDiffLoading;", viewModel);
        Assert.Contains("public bool IsCommitLoading", viewModel);
        Assert.Contains("public bool IsDiffLoading", viewModel);
        Assert.Contains("DetailsVisibility => SelectedHistoryRow is null ? Visibility.Collapsed : Visibility.Visible", viewModel);
        Assert.DoesNotContain("DetailsVisibility => SelectedCommit", viewModel);

        var commitLoading = commitLoad.IndexOf("IsCommitLoading = true;", StringComparison.Ordinal);
        var commitAwait = commitLoad.IndexOf("await _historyService.ReadCommitAsync", StringComparison.Ordinal);
        Assert.True(commitLoading >= 0 && commitAwait > commitLoading);
        AssertBefore(commitLoad, "SelectedCommit = null;", commitAwait, commitLoading);
        AssertBefore(commitLoad, "SelectedFile = null;", commitAwait, commitLoading);
        AssertBefore(commitLoad, "SelectedDiff = null;", commitAwait, commitLoading);
        Assert.Contains("finally", commitLoad);
        Assert.True(Count(commitLoad, "generation == Volatile.Read(ref _commitLoadGeneration)") >= 3);
        Assert.Contains("IsCommitLoading = false;", commitLoad);
        Assert.DoesNotContain("EnterBusy();", commitLoad);

        var diffLoading = diffLoad.IndexOf("IsDiffLoading = true;", StringComparison.Ordinal);
        var diffAwait = diffLoad.IndexOf("await _historyService.ReadDiffAsync", StringComparison.Ordinal);
        Assert.True(diffLoading >= 0 && diffAwait > diffLoading);
        AssertBefore(diffLoad, "SelectedDiff = null;", diffLoading, 0);
        Assert.Contains("finally", diffLoad);
        Assert.True(Count(diffLoad, "generation == Volatile.Read(ref _diffLoadGeneration)") >= 3);
        Assert.Contains("IsDiffLoading = false;", diffLoad);
        Assert.DoesNotContain("EnterBusy();", diffLoad);
    }

    [Fact]
    public void CommitAndDiffLoadingUseLocalProgressRingOverlaysWithoutHidingHistoryDetails()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.cs"));
        var overlays = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.LoadingOverlays.cs"));
        var commitChangesSurface = ExtractCommitChangesSurface(xaml);

        Assert.Contains("x:Name=\"DetailsTabs\"", xaml);
        Assert.Contains("Visibility=\"{Binding DetailsVisibility}\"", xaml);
        Assert.Contains("DetailsVisibility => SelectedHistoryRow is null ? Visibility.Collapsed : Visibility.Visible", viewModel);

        Assert.Contains("CreateLoadingOverlay(\"Loading commit…\"", overlays);
        Assert.Contains("CreateLoadingOverlay(\"Loading diff…\"", overlays);
        Assert.Contains("new ProgressRing", overlays);
        Assert.Contains("Grid.SetRow(_commitLoadingOverlay, 3);", overlays);
        Assert.Contains("HistoryPane.Children.Add(_commitLoadingOverlay);", overlays);
        Assert.Contains("CompactDiffList.Parent is not Grid diffViewer", overlays);
        Assert.Contains("diffViewer.Children.Add(_diffLoadingOverlay);", overlays);
        Assert.Contains("Background = RootLayout.Background", overlays);
        Assert.Contains("_commitLoadingOverlay!.Visibility = _viewModel.CommitLoadingVisibility;", overlays);
        Assert.Contains("_diffLoadingOverlay!.Visibility = _viewModel.DiffLoadingVisibility;", overlays);
        Assert.Contains("_commitLoadingRing!.IsActive = _viewModel.IsCommitLoading;", overlays);
        Assert.Contains("_diffLoadingRing!.IsActive = _viewModel.IsDiffLoading;", overlays);
        Assert.DoesNotContain("IsBusy", overlays);

        Assert.Contains("x:Name=\"ChangedFilesTree\"", commitChangesSurface);
        Assert.Contains("x:Name=\"CompactDiffList\"", commitChangesSurface);
    }

    private static void AssertBefore(string source, string fragment, int upperBound, int startIndex)
    {
        var index = source.IndexOf(fragment, startIndex, StringComparison.Ordinal);
        Assert.True(index >= startIndex && index < upperBound, $"Expected '{fragment}' before the asynchronous load.");
    }

    private static string ExtractBetween(string value, string startMarker, string endMarker)
    {
        var start = value.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = value.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start);
        return value[start..end];
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

    private static int Count(string value, string fragment) =>
        (value.Length - value.Replace(fragment, string.Empty, StringComparison.Ordinal).Length) / fragment.Length;

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
