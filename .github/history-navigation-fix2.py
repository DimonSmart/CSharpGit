from pathlib import Path


def replace_once(path, old, new):
    p = Path(path)
    text = p.read_text(encoding="utf-8")
    if text.count(old) != 1:
        raise RuntimeError(f"Expected exactly one occurrence in {path}: {old!r}, found {text.count(old)}")
    p.write_text(text.replace(old, new, 1), encoding="utf-8", newline="\n")

replace_once(
    "tests/CSharpGit.Desktop.Tests/ReferenceBadgeUiContractTests.cs",
    'ItemsSource=\\"{Binding SelectedCommit.Commit.References}\\"',
    'ItemsSource=\\"{Binding SelectedHistoryRow.Commit.References}\\"')

replace_once(
    "tests/CSharpGit.Application.Tests/HistoryDiffUiContractTests.cs",
    'Assert.DoesNotContain("SelectedCommit", source);',
    'Assert.DoesNotContain("_viewModel.SelectedCommit", source);')

Path("tests/CSharpGit.Application.Tests/HistoryLifecycleContractTests.cs").write_text(r'''namespace CSharpGit.Application.Tests;

public sealed class HistoryLifecycleContractTests
{
    [Fact]
    public void HistorySelectionAndLoadingHaveStableIdentityAndStaleRequestGuards()
    {
        var root = FindRepositoryRoot();
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.cs"));
        var lazyChanges = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.CommitChanges.cs"));
        var page = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml.cs"));

        Assert.Contains("ReferenceEquals(_selectedHistoryRow, value)", viewModel);
        Assert.Contains("_historyLoadGeneration", viewModel);
        Assert.Contains("CancellationTokenSource? _historyLoadCts", viewModel);
        Assert.Contains("generation != Volatile.Read(ref _historyLoadGeneration)", viewModel);
        Assert.Contains("internal void InvalidateHistoryLoad()", viewModel);
        Assert.Contains("OnSelectedHistoryRowChanged();", viewModel);

        Assert.Contains("_changedFilesLoadGeneration", lazyChanges);
        Assert.Contains("_diffLoadGeneration", lazyChanges);
        Assert.Contains("ReferenceEquals(row, SelectedHistoryRow)", lazyChanges);
        Assert.Contains("ReferenceEquals(file, SelectedFile)", lazyChanges);
        Assert.Contains("InvalidateChangedFilesLoad", lazyChanges);
        Assert.Contains("InvalidateDiffLoad", lazyChanges);

        Assert.Contains("_viewModel.InvalidateHistoryLoad();", page);
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
''', encoding="utf-8", newline="\n")
