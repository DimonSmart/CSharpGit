namespace CSharpGit.Application.Tests;

public sealed class PresentationLifecycleContractTests
{
    [Fact]
    public void SettingsAndMainPageCallbacksHaveThreadAndShutdownFences()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var openRepositoryViewModel = File.ReadAllText(Path.Combine(presentation, "ViewModels", "OpenRepositoryViewModel.cs"));
        var lifecycle = File.ReadAllText(Path.Combine(presentation, "MainPage.Lifecycle.cs"));
        var recent = File.ReadAllText(Path.Combine(presentation, "MainPage.RecentRepositories.cs"));
        var gitConsole = File.ReadAllText(Path.Combine(presentation, "MainPage.GitConsole.cs"));
        var refresh = File.ReadAllText(Path.Combine(presentation, "MainPage.RepositoryRefresh.cs"));

        Assert.Contains("_uiDispatcher.HasThreadAccess", openRepositoryViewModel, StringComparison.Ordinal);
        Assert.Contains("ApplySettingsChangeOnUiThread", openRepositoryViewModel, StringComparison.Ordinal);
        Assert.Contains("Volatile.Read(ref _disposed)", openRepositoryViewModel, StringComparison.Ordinal);

        Assert.Contains("private bool IsShuttingDown", lifecycle, StringComparison.Ordinal);
        Assert.Contains("Volatile.Read(ref _shutdownStarted)", lifecycle, StringComparison.Ordinal);

        Assert.Contains("DispatcherQueue.HasThreadAccess", recent, StringComparison.Ordinal);
        Assert.Contains("!IsShuttingDown && !_recentRepositoriesShutdown", recent, StringComparison.Ordinal);

        Assert.Contains("if (IsShuttingDown || !_gitConsoleInitialized", gitConsole, StringComparison.Ordinal);
        Assert.Contains("ApplyGitCommandLifecycleChange", gitConsole, StringComparison.Ordinal);

        Assert.Contains("if (IsShuttingDown || !_repositoryChangeMonitoringInitialized) return;", refresh, StringComparison.Ordinal);
        Assert.Contains("_repositoryProbePending = false;", refresh, StringComparison.Ordinal);
        Assert.Contains("_repositoryProbeToken", refresh, StringComparison.Ordinal);
        Assert.Contains("if (IsShuttingDown || !_repositoryChangeMonitoringInitialized) return;", refresh, StringComparison.Ordinal);
        Assert.Contains("!IsShuttingDown && DispatcherQueue.HasThreadAccess", refresh, StringComparison.Ordinal);
        Assert.Contains("ClearRepositoryPresentationRefreshQueue();", refresh, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryProbeRechecksLifecycleAfterAwaitAndBeforeRestart()
    {
        var refresh = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "CSharpGit.Presentation",
            "MainPage.RepositoryRefresh.cs"));

        var awaitIndex = refresh.IndexOf("await _repositoryRefreshProbe.ReadAsync", StringComparison.Ordinal);
        var postAwaitFenceIndex = refresh.IndexOf(
            "if (IsShuttingDown || !_repositoryChangeMonitoringInitialized) return;",
            awaitIndex,
            StringComparison.Ordinal);
        var finallyIndex = refresh.IndexOf("finally", awaitIndex, StringComparison.Ordinal);
        var restartFenceIndex = refresh.IndexOf(
            "!IsShuttingDown &&",
            finallyIndex,
            StringComparison.Ordinal);

        Assert.True(awaitIndex >= 0);
        Assert.True(postAwaitFenceIndex > awaitIndex);
        Assert.True(finallyIndex > postAwaitFenceIndex);
        Assert.True(restartFenceIndex > finallyIndex);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
