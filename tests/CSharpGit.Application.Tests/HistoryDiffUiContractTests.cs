namespace CSharpGit.Application.Tests;

public sealed class HistoryDiffUiContractTests
{
    [Fact]
    public void HistoryDetailsUseHierarchicalChangesBesideSharedDenseDiff()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var selectableDiff = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "SelectableDiffViewer.xaml"));
        var selectableDiffCode = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "SelectableDiffViewer.xaml.cs"));
        var changes = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.Changes.cs"));
        var tree = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "ChangedFileTreeNode.cs"));
        var pathTreeBuilder = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "PathTreeBuilder.cs"));
        var diff = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "CompactDiffLine.cs"));
        var commitChangesSurface = ExtractCommitChangesSurface(xaml);

        Assert.Contains("x:Name=\"ChangedFilesTree\"", commitChangesSurface);
        Assert.Contains("controls:SelectableDiffViewer x:Name=\"CompactDiffViewer\"", commitChangesSurface);
        Assert.Contains("x:Name=\"RowsRepeater\"", selectableDiff);
        Assert.Contains("ColumnDefinitions=\"38,38,*\"", selectableDiff);
        Assert.Contains("SelectableDiffTextStyle", selectableDiff);
        Assert.Contains("HorizontalScrollBarVisibility=\"Auto\"", selectableDiff);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", selectableDiff);
        Assert.Contains("new DiffLogicalText(snapshot)", selectableDiffCode);
        Assert.DoesNotContain("<PivotItem Header=\"Diff\">", xaml);
        Assert.DoesNotContain("ItemsSource=\"{Binding SelectedCommit.Files}\"", xaml);
        Assert.Contains("ChangedFileTreeSynchronizer.Reconcile", changes);
        Assert.Contains("CompactDiffLine.Build", changes);
        Assert.Contains("PathTreeBuilder.Build", tree);
        Assert.Contains("CollapseSingleChildFolderChains: true", tree);
        Assert.Contains("while (source.IsFolder && children.Count == 1 && children[0].IsFolder)", pathTreeBuilder);
        Assert.Contains("TryReadHunkStarts", diff);
    }

    [Fact]
    public void CommitMetadataComesDirectlyFromSelectedHistoryRowWithoutCommitLoad()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var details = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "CommitDetailsView.xaml"));
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.cs"));

        Assert.Contains("<controls:CommitDetailsView x:Name=\"CommitDetailsContent\" />", xaml);
        Assert.Contains("SelectedHistoryRow.Commit.Message", details);
        Assert.Contains("SelectedHistoryRow.Commit.References", details);
        Assert.Contains("SelectedHistoryRow.Commit.Author", details);
        Assert.Contains("SelectedHistoryRow.Commit.AuthoredAt", details);
        Assert.Contains("SelectedHistoryRow.Commit.Hash", details);
        Assert.Contains("SelectedHistoryRow.Commit.ParentsDisplay", details);
        Assert.DoesNotContain("SelectedCommit.Commit", details);
        Assert.DoesNotContain("LoadCommitAsync", viewModel);
        Assert.DoesNotContain("_historyService.ReadCommitAsync", viewModel);
        Assert.DoesNotContain("IsCommitLoading", viewModel);
    }

    [Fact]
    public void ChangesActivityUsesPivotSelectionAndNoSeparateStatusQuery()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var changes = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.Changes.cs"));
        var page = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml.cs"));

        Assert.Contains("SelectionChanged=\"DetailsTabs_SelectionChanged\"", xaml);
        Assert.Contains("SetChangesViewActive", changes);
        Assert.Contains("ReferenceEquals(DetailsTabs.SelectedItem, ChangesTabControl)", changes);
        Assert.DoesNotContain("ReadFileStatusesAsync", page);
        Assert.Contains("_viewModel.GetChangedFileDisplayStatus(file)", page);
    }

    [Fact]
    public void ChangedFilesAndDiffHaveIndependentLazyCancellationCacheAndLocalLoading()
    {
        var root = FindRepositoryRoot();
        var lazy = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.CommitChanges.cs"));
        var overlays = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.LoadingOverlays.cs"));

        Assert.Contains("ChangedFilesDebounceMilliseconds = 120", lazy);
        Assert.Contains("_changedFilesLoadCts", lazy);
        Assert.Contains("_diffLoadCts", lazy);
        Assert.Contains("BoundedLruCache", lazy);
        Assert.Contains("ReadChangedFilesAsync", lazy);
        Assert.Contains("ReadDiffAsync", lazy);
        Assert.Contains("IsCurrentChangedFilesRequest", lazy);
        Assert.Contains("IsCurrentDiffRequest", lazy);
        Assert.Contains("ResetCommitChangesSession", lazy);
        Assert.Contains("Loading changes…", overlays);
        Assert.Contains("Loading diff…", overlays);
        Assert.DoesNotContain("Loading commit…", overlays);
        Assert.DoesNotContain("IsBusy", overlays);
    }

    [Fact]
    public void HistoricalDiffHasExplicitEmptyErrorAndSelectionRestoreStates()
    {
        var root = FindRepositoryRoot();
        var changes = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.Changes.cs"));
        var presentation = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.DiffPresentation.cs"));
        var lazy = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.CommitChanges.cs"));

        Assert.Contains("DiffPresentationResolver.Resolve", changes);
        Assert.Contains("DiffPresentationState.NoTextualPatch", changes);
        Assert.Contains("DiffPresentationState.Error", changes);
        Assert.Contains("DiffLoadErrorMessage", lazy);
        Assert.Contains("ChangedFileSelectionKey", lazy);
        Assert.Contains("previous.Commit.Hash", lazy);
        Assert.Contains("Select a changed file to view its diff.", presentation);
        Assert.Contains("Git reports this file as changed, but there is no textual patch to display.", presentation);
        Assert.Contains("Could not load diff", presentation);
    }

    [Fact]
    public void HistoricalLargeDiffPreviewUsesStatsAndBoundedStreaming()
    {
        var root = FindRepositoryRoot();
        var policy = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Application", "Abstractions", "DiffPreviewPolicy.cs"));
        var lazy = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.CommitChanges.cs"));
        var changes = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.Changes.cs"));
        var executor = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Git", "GitCommandExecutor.cs"));

        Assert.Contains("LargeChangedLines = 10_000", policy);
        Assert.Contains("AutomaticOutputBytes = 1024 * 1024", policy);
        Assert.Contains("TryDeferLargeHistoricalDiff", lazy);
        Assert.Contains("LoadSelectedDiffAnyway", lazy);
        Assert.Contains("DiffLoadMode.Full", lazy);
        Assert.Contains("DiffPresentationState.LargeDiff", changes);
        Assert.Contains("maxStandardOutputBytes", executor);
        Assert.Contains("GitCommandOutputLimitExceededException", executor);
    }

    [Fact]
    public void GitHotPathUsesOneCombinedChangedFilesProcessAndNoHardCopySearch()
    {
        var root = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Git", "GitFileAwareHistoryService.cs"));
        var executor = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Git", "GitCommandExecutor.cs"));

        Assert.Contains("\"--raw\", \"--numstat\"", service);
        Assert.Contains("\"--find-renames\", \"--find-copies\"", service);
        Assert.DoesNotContain("--find-copies-harder", service);
        Assert.Contains("string? parentHash", service);
        Assert.Contains("ChangedFile file", service);
        Assert.Contains("process.Kill(entireProcessTree: true)", executor);
        Assert.Contains("Task.WhenAll(outputTask, errorTask)", executor);
    }

    [Fact]
    public void HistoricalFileActionStateRemainsLazy()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.FileOpening.cs"));
        var stateMethod = ExtractBetween(source, "private Task RefreshCommitFileActionStateAsync()", "private async Task RefreshWorkingTreeFileActionStateAsync()");

        Assert.DoesNotContain("ResolveCommitAsync", stateMethod);
        Assert.Contains("SelectedFile", stateMethod);
        Assert.Contains("TryResolveReveal", stateMethod);
    }

    [Fact]
    public void HistoricalExternalDiffUsesTheSameResolvedPairAsFileVersionActions()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.FileOpening.cs"));
        var action = ExtractBetween(source, "private async Task OpenSelectedCommitExternalDiffAsync()", "private async Task OpenSelectedWorkingTreeExternalDiffAsync()");

        Assert.Contains("ResolveCommitAsync(repository, commit.Hash, file.Path)", action);
        Assert.Contains("RunExternalDiffAsync(repository, pair)", action);
        Assert.DoesNotContain("Parents", action);
        Assert.DoesNotContain("HEAD", action);
        Assert.Contains("SelectedHistoryRow", source);
        Assert.DoesNotContain("_viewModel.SelectedCommit", source);
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
        const string startMarker = "<PivotItem x:Name=\"ChangesTab\" Header=\"Changes\">";
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
