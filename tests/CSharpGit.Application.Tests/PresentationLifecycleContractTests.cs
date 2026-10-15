namespace CSharpGit.Application.Tests;

public sealed class PresentationLifecycleContractTests
{
    [Fact]
    public void DesktopCloseCheckWaitsForReadinessAndAnExplicitCloseCommand()
    {
        var presentation = Path.Combine(FindRepositoryRoot(), "src", "CSharpGit.Presentation");
        var app = File.ReadAllText(Path.Combine(presentation, "App.xaml.cs"));
        var check = File.ReadAllText(Path.Combine(presentation, "App.DesktopCloseCheck.cs"));

        Assert.Contains("mainPage.Loaded += RunDesktopCloseCheckWhenLoaded", app, StringComparison.Ordinal);
        Assert.DoesNotContain("_window.Activated +=", app, StringComparison.Ordinal);
        Assert.Contains("page.Loaded -= RunDesktopCloseCheckWhenLoaded", check, StringComparison.Ordinal);
        Assert.Contains("if (_closeCheckStarted) return;", check, StringComparison.Ordinal);
        Assert.Contains("if (!await viewModel.OpenRepositoryPathAsync", check, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenRepositoryAsyncForDesktopCheck", check, StringComparison.Ordinal);

        var opened = check.IndexOf("await viewModel.OpenRepositoryPathAsync", StringComparison.Ordinal);
        var ready = check.IndexOf("State = \"Ready\"", StringComparison.Ordinal);
        var command = check.IndexOf("await reader.ReadLineAsync(timeout.Token) != \"Close\"", StringComparison.Ordinal);
        var dispatch = check.IndexOf("if (!_window!.DispatcherQueue.TryEnqueue", StringComparison.Ordinal);
        var close = check.IndexOf("RequestNormalWindowCloseForDesktopCheck();", StringComparison.Ordinal);
        Assert.True(opened >= 0 && ready > opened && command > ready && dispatch > command && close > dispatch);
        Assert.Contains("PostMessageW(handle, 0x0010, 0, 0)", check, StringComparison.Ordinal);
        Assert.Contains("else if (TryBeginNormalShutdown", check, StringComparison.Ordinal);
        Assert.Contains("if (!TryBeginNormalShutdown", app, StringComparison.Ordinal);
        Assert.DoesNotContain("Environment.Exit", check, StringComparison.Ordinal);
        Assert.DoesNotContain("_closeConfirmed = true", check, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopCloseCheckReportsNormalLifetimeMilestones()
    {
        var app = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "CSharpGit.Presentation", "App.xaml.cs"));
        foreach (var milestone in new[] { "Closing", "CloseCanceled", "CloseAccepted", "WindowClosed",
                     "PageShutdownCompleted", "HostStopping", "HostStopped" })
            Assert.Contains($"ReportDesktopCloseCheck(\"{milestone}", app, StringComparison.Ordinal);
    }

    [Fact]
    public void CloseChecksActiveOperationsAndRechecksAfterConfirmation()
    {
        var presentation = Path.Combine(FindRepositoryRoot(), "src", "CSharpGit.Presentation");
        var lifecycle = File.ReadAllText(Path.Combine(presentation, "MainPage.Lifecycle.cs"));
        var app = File.ReadAllText(Path.Combine(presentation, "App.xaml.cs"));
        Assert.Contains("_viewModel.IsMutating", lifecycle, StringComparison.Ordinal);
        Assert.Contains("_cloneRepositoryViewModel.IsBusy", lifecycle, StringComparison.Ordinal);
        Assert.Contains("_createRepositoryViewModel.IsBusy", lifecycle, StringComparison.Ordinal);
        Assert.Contains("page?.IsUnsafeToClose == true", app, StringComparison.Ordinal);
        var recheck = app.IndexOf("|| page.IsUnsafeToClose", StringComparison.Ordinal);
        var shutdown = app.IndexOf("BeginShutdown(page);", recheck, StringComparison.Ordinal);
        Assert.True(recheck >= 0 && shutdown > recheck);
    }

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
        Assert.Contains("_workingTreeStatusPending = null;", refresh, StringComparison.Ordinal);
        Assert.Contains("_workingTreeStatusToken", refresh, StringComparison.Ordinal);
        Assert.Contains("if (IsShuttingDown || !_repositoryChangeMonitoringInitialized) return;", refresh, StringComparison.Ordinal);
        Assert.Contains("!IsShuttingDown && DispatcherQueue.HasThreadAccess", refresh, StringComparison.Ordinal);
        Assert.Contains("ClearRepositoryPresentationRefreshQueue();", refresh, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkingTreeStatusCheckRechecksLifecycleAfterAwaitAndBeforeRestart()
    {
        var refresh = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "CSharpGit.Presentation",
            "MainPage.RepositoryRefresh.cs"));

        var awaitIndex = refresh.IndexOf("await _workingTreeStatusReader.ReadAsync", StringComparison.Ordinal);
        var postAwaitFenceIndex = refresh.IndexOf(
            "if (IsShuttingDown || !_repositoryChangeMonitoringInitialized) return;",
            awaitIndex,
            StringComparison.Ordinal);
        var finallyIndex = refresh.IndexOf("finally", awaitIndex, StringComparison.Ordinal);
        var restartFenceIndex = refresh.IndexOf(
            "StartWorkingTreeStatusLoopIfNeeded();",
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
