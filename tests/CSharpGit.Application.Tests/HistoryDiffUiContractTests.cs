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
        Assert.Contains("ItemContainerStyle=\"{StaticResource DiffRowStyle}\"", commitChangesSurface);
        Assert.Contains("ItemTemplate=\"{StaticResource DiffItemTemplate}\"", commitChangesSurface);
        Assert.DoesNotContain("<PivotItem Header=\"Diff\">", xaml);
        Assert.DoesNotContain("ItemsSource=\"{Binding SelectedCommit.Files}\"", xaml);
        Assert.Contains("ChangedFileTreeNode.Build", changes);
        Assert.Contains("CompactDiffLine.Build", changes);
        Assert.Contains("while (entry is null && children.Count == 1", tree);
        Assert.Contains("TryReadHunkStarts", diff);
        Assert.Contains("x:Key=\"DiffItemTemplate\"", workspace);
    }

    [Fact]
    public void CommitMetadataComesDirectlyFromSelectedHistoryRowWithoutCommitLoad()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.cs"));

        Assert.Contains("SelectedHistoryRow.Commit.Message", xaml);
        Assert.Contains("SelectedHistoryRow.Commit.References", xaml);
        Assert.Contains("SelectedHistoryRow.Commit.Author", xaml);
        Assert.Contains("SelectedHistoryRow.Commit.AuthoredAt", xaml);
        Assert.Contains("SelectedHistoryRow.Commit.Hash", xaml);
        Assert.Contains("SelectedHistoryRow.Commit.ParentsDisplay", xaml);
        Assert.DoesNotContain("SelectedCommit.Commit", xaml);
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
        Assert.Contains("ReferenceEquals(DetailsTabs.SelectedItem, FilesTab)", changes);
        Assert.DoesNotContain("ReadFileStatusesAsync", page);
        Assert.Contains("new CommitFileRow(file.Status, file)", page);
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
