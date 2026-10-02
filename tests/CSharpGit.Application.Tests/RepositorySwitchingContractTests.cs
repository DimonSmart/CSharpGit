namespace CSharpGit.Application.Tests;

public sealed class RepositorySwitchingContractTests
{
    [Fact]
    public void RepositorySwitchingUsesOnePathBasedLifecycleAndExplicitClose()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var viewModel = File.ReadAllText(Path.Combine(
            presentation,
            "ViewModels",
            "OpenRepositoryViewModel.cs"));
        var switching = File.ReadAllText(Path.Combine(
            presentation,
            "MainPage.RepositorySwitching.cs"));
        var recent = File.ReadAllText(Path.Combine(
            presentation,
            "MainPage.RecentRepositories.cs"));
        var creation = File.ReadAllText(Path.Combine(
            presentation,
            "MainPage.RepositoryCreation.cs"));
        var refresh = File.ReadAllText(Path.Combine(
            presentation,
            "MainPage.RepositoryRefresh.cs"));

        Assert.Contains("internal async Task<bool> OpenRepositoryPathAsync", viewModel);
        Assert.Contains("internal bool CloseRepository()", viewModel);
        Assert.Contains("InvalidateHistoryLoad();", viewModel);
        Assert.Contains("ClearRepositoryPresentation();", viewModel);
        Assert.Contains("public bool CanChangeRepository", viewModel);
        Assert.Contains("_isMutating", viewModel);
        Assert.Contains("_repositoryChangeInProgress", viewModel);

        Assert.Contains("TrySwitchRepositoryAsync", switching);
        Assert.Contains("SwitchRepositoryCoreAsync", switching);
        Assert.Contains("TryCloseRepositoryAsync", switching);
        Assert.Contains("ConfirmDiscardCommitMessageAsync", switching);
        Assert.Contains("if (!opened) return false;", switching);
        Assert.Contains("_viewModel.CommitMessage = string.Empty;", switching);

        Assert.Contains("await TrySwitchRepositoryAsync(item.Path);", recent);
        Assert.Contains("await _recentRepositoryFolderPicker.PickFolderAsync()", recent);
        Assert.DoesNotContain("QueuePath(item.Path)", recent);

        Assert.Contains("SwitchRepositoryCoreAsync(path, discardDraft)", creation);
        Assert.DoesNotContain("ConfirmDiscardCommitMessageForRepositorySwitchAsync", creation);

        Assert.Contains("_repositoryChangeMonitor.Stop();", refresh);
        Assert.Contains("_repositoryChangeMonitor.Start(repository);", refresh);
    }

    [Fact]
    public void RepositorySelectorAndKeyboardExposeTheSameWorkflow()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var xaml = File.ReadAllText(Path.Combine(presentation, "MainPage.xaml"));
        var page = File.ReadAllText(Path.Combine(presentation, "MainPage.xaml.cs"));
        var switching = File.ReadAllText(Path.Combine(
            presentation,
            "MainPage.RepositorySwitching.cs"));

        Assert.Contains("x:Name=\"RepositorySelectorButton\"", xaml);
        Assert.Contains("Opening=\"RepositorySelectorFlyout_Opening\"", xaml);
        Assert.Contains("Text=\"Open repository…\"", xaml);
        Assert.Contains("Text=\"Repositories…\"", xaml);
        Assert.Contains("Text=\"Close repository\"", xaml);
        Assert.Contains("IsEnabled=\"{Binding CanChangeRepository}\"", xaml);

        Assert.Contains("QuickRepositoryLimit = 8", switching);
        Assert.Contains("BuildQuickList(", switching);
        Assert.Contains("Current repository", switching);
        Assert.Contains("Create repository…", switching);
        Assert.Contains("ShowUnavailableRepositoryAsync", switching);

        Assert.Contains("e.Key == VirtualKey.O", page);
        Assert.Contains("await OpenRepositoryPickerAsync();", page);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException(
                "CSharpGit repository root was not found.");
    }
}
