namespace CSharpGit.Application.Tests;

public sealed class SelectedCommitActionsContractTests
{
    [Fact]
    public void RefreshKeepsRepositoryIdentityAndWorkingTreeMutationsSkipHistory()
    {
        var root = FindRepositoryRoot();
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.cs"));

        Assert.DoesNotContain("_repositoryService.OpenAsync(Repository.WorkingDirectory)", viewModel);
        Assert.Contains("RefreshStateAsync(bool includeHistory)", viewModel);
        Assert.Contains("if (includeHistory)", viewModel);
        Assert.Contains("includeHistory: false", ExtractMethod(viewModel, "StageActiveAsync"));
        Assert.Contains("includeHistory: false", ExtractMethod(viewModel, "UnstageActiveAsync"));
        Assert.Contains("includeHistory: false", ExtractMethod(viewModel, "StageSelectedAsync"));
        Assert.Contains("includeHistory: false", ExtractMethod(viewModel, "UnstageSelectedAsync"));
        Assert.DoesNotContain("ClearWorkingTreePresentationSelection", ExtractMethod(viewModel, "StageActiveAsync"));
        Assert.DoesNotContain("ClearWorkingTreePresentationSelection", ExtractMethod(viewModel, "UnstageActiveAsync"));
    }

    [Fact]
    public void RepositoryStateApplicationIsBulkAndPresentationRebuildIsCoalesced()
    {
        var root = FindRepositoryRoot();
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.cs"));
        var bulk = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "BulkObservableCollection.cs"));
        var page = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml.cs"));
        var coalescing = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.RepositoryRefresh.cs"));

        Assert.Contains("Replace(Changes, state.Changes)", viewModel);
        Assert.Contains("bulk.ReplaceAll(snapshot)", viewModel);
        Assert.Contains("NotifyCollectionChangedAction.Reset", bulk);
        Assert.Contains("QueueRepositoryPresentationRefresh(workingTreeChanged: true)", page);
        Assert.Contains("DispatcherQueue.TryEnqueue(FlushRepositoryPresentationRefresh)", coalescing);
    }

    [Fact]
    public void CommitMenuExposesRequiredActionsAndMainlineChoice()
    {
        var root = FindRepositoryRoot();
        var actions = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.CommitActions.cs"));

        foreach (var label in new[] { "Copy hash", "Create branch here…", "Checkout this commit", "Cherry-pick", "Revert", "Reset current branch to here", "Soft…", "Mixed…", "Hard…" })
            Assert.Contains(label, actions);

        Assert.Contains("HistoryList.RightTapped", actions);
        Assert.Contains("commit.Parents.Count > 1", actions);
        Assert.Contains("Mainline parent", actions);
        Assert.Contains("Uncommitted tracked changes will be lost", actions);
        Assert.Contains("Untracked files will not be deleted", actions);
    }

    [Fact]
    public void CancelCommitRemainsPresentationOnly()
    {
        var root = FindRepositoryRoot();
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.cs"));
        var method = ExtractMethod(viewModel, "CancelCommitAsync");

        Assert.Contains("IsEmptyIndexChoiceOpen = false", method);
        Assert.DoesNotContain("Refresh", method);
        Assert.DoesNotContain("_workingTreeService", method);
        Assert.DoesNotContain("_repositoryService", method);
        Assert.DoesNotContain("CommitMessage =", method);
    }

    private static string ExtractMethod(string source, string methodName)
    {
        var start = source.IndexOf($" {methodName}(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Method {methodName} was not found.");
        start = source.LastIndexOf('\n', start) + 1;
        var next = source.IndexOf("\n    private ", start + 1, StringComparison.Ordinal);
        if (next < 0) next = source.IndexOf("\n    internal ", start + 1, StringComparison.Ordinal);
        if (next < 0) next = source.Length;
        return source[start..next];
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
