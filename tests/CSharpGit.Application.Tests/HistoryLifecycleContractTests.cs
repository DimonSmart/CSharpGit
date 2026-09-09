namespace CSharpGit.Application.Tests;

public sealed class HistoryLifecycleContractTests
{
    [Fact]
    public void HistorySelectionAndLoadingHaveStableIdentityAndStaleRequestGuards()
    {
        var root = FindRepositoryRoot();
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.cs"));
        var page = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml.cs"));

        Assert.Contains("ReferenceEquals(_selectedHistoryRow, value)", viewModel);
        Assert.Contains("SelectedHistoryRow?.Commit.Hash", viewModel);
        Assert.Contains("_historyLoadGeneration", viewModel);
        Assert.Contains("CancellationTokenSource? _historyLoadCts", viewModel);
        Assert.Contains("generation != Volatile.Read(ref _historyLoadGeneration)", viewModel);
        Assert.Contains("ReferenceEquals(selectedRow, SelectedHistoryRow)", viewModel);
        Assert.DoesNotContain("HistoryList.SelectedItem = first", page);
        Assert.Contains("_scopedHistory.Any(row => ReferenceEquals(row, _viewModel.SelectedHistoryRow))", page);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
