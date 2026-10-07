namespace CSharpGit.Application.Tests;

public sealed class HistoryLifecycleContractTests
{
    [Fact]
    public void HistorySelectionAndLoadingHaveOneOwnerAndStaleRequestGuards()
    {
        var root = FindRepositoryRoot();
        var history = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "HistoryViewModel.cs"));
        var adapter = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.History.cs"));
        var lazyChanges = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "CommitDetailsViewModel.cs"));
        var page = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml.cs"));
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));

        Assert.Contains("ReferenceEquals(_selectedRow, value)", history);
        Assert.Contains("_loadGeneration", history);
        Assert.Contains("CancellationTokenSource? _loadCts", history);
        Assert.Contains("request.Generation == Volatile.Read(ref _loadGeneration)", history);
        Assert.Contains("ReferenceEquals(request.Repository, _context?.Repository)", history);
        Assert.Contains("public void Invalidate()", history);
        Assert.Contains("SelectedRowChanged?.Invoke(previous);", history);
        Assert.Contains("CommitDetails.ShowCommit(selected);", adapter);

        Assert.Contains("_changedFilesLoadGeneration", lazyChanges);
        Assert.Contains("_diffLoadGeneration", lazyChanges);
        Assert.Contains("ReferenceEquals(row, _selectedHistoryRow)", lazyChanges);
        Assert.Contains("ReferenceEquals(file, SelectedFile)", lazyChanges);
        Assert.Contains("InvalidateChangedFilesLoad", lazyChanges);
        Assert.Contains("InvalidateDiffLoad", lazyChanges);

        Assert.DoesNotContain("_scopedHistory", page);
        Assert.DoesNotContain("_referenceHistoryCts", page);
        Assert.DoesNotContain("LoadScopedHistoryAsync", page);
        Assert.Contains("ItemsSource=\"{Binding History.Rows}\"", xaml);
        Assert.Contains("SelectedItem=\"{Binding History.SelectedRow, Mode=TwoWay}\"", xaml);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
