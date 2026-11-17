using System.Xml.Linq;

namespace CSharpGit.Application.Tests;

public sealed class PresentationLifecycleContractTests
{
    [Fact]
    public void FetchToolbarAvailabilityIsBoundToRepositorySync()
    {
        var xaml = XDocument.Load(Path.Combine(FindRepositoryRoot(),
            "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var fetchButton = Assert.Single(xaml.Descendants(), element =>
            element.Name.LocalName == "Button"
            && (string?)element.Attribute("Click") == "Fetch_Click");

        Assert.Equal("{Binding RepositorySync.CanFetch}",
            (string?)fetchButton.Attribute("IsEnabled"));
    }

    [Fact]
    public void RepositoryToolbarAndLauncherActionsDisableDuringGitActivity()
    {
        var xaml = XDocument.Load(Path.Combine(FindRepositoryRoot(),
            "src", "CSharpGit.Presentation", "MainPage.xaml"));

        foreach (var action in new[] { "RefreshIndicator_Click", "OpenRepository_Click",
                     "CloneRepository_Click", "CreateRepository_Click" })
        {
            var actions = xaml.Descendants()
                .Where(element => (string?)element.Attribute("Click") == action)
                .ToArray();
            Assert.NotEmpty(actions);
            Assert.All(actions, element =>
                Assert.Equal("{Binding CanChangeRepository}",
                    (string?)element.Attribute("IsEnabled")));
        }

        var mainPage = File.ReadAllText(Path.Combine(FindRepositoryRoot(),
            "src", "CSharpGit.Presentation", "MainPage.xaml.cs"));
        Assert.Contains("_viewModel.History.LoadMoreCommand.CanExecute(null)", mainPage);
        Assert.Contains("nameof(OpenRepositoryViewModel.CanChangeRepository)", mainPage);
        Assert.Contains("UpdateLoadMoreHistoryAvailability();", mainPage);
        Assert.DoesNotContain("LoadMoreHistoryButton.IsEnabled = history.HasMore", mainPage);
    }

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
        Assert.Contains("RequestMacOsNativeClose();", check, StringComparison.Ordinal);
        Assert.Contains("ObjcSelector(\"performClose:\")", check, StringComparison.Ordinal);
        Assert.Contains("if (OperatingSystem.IsMacOS())", app, StringComparison.Ordinal);
        Assert.Contains("else if (TryBeginNormalShutdown", check, StringComparison.Ordinal);
        Assert.Contains("if (!TryBeginNormalShutdown", app, StringComparison.Ordinal);
        Assert.DoesNotContain("Environment.Exit", check, StringComparison.Ordinal);
        Assert.DoesNotContain("_closeConfirmed = true", check, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopCloseCheckReportsNormalLifetimeMilestones()
    {
        var app = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "CSharpGit.Presentation", "App.xaml.cs"));
        var close = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "CSharpGit.Presentation", "Windowing", "WindowCloseOrchestrator.cs"));
        foreach (var milestone in new[] { "Closing", "CloseCanceled", "CloseAccepted", "WindowClosed", "PageShutdownCompleted", "HostStopping", "HostStopped" })
            Assert.Contains(milestone, app + close, StringComparison.Ordinal);
    }

    [Fact]
    public void CloseChecksActiveOperationsAndRechecksAfterConfirmation()
    {
        var safety = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "CSharpGit.Presentation", "Windowing", "WindowCloseSafetyProvider.cs"));
        var close = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "CSharpGit.Presentation", "Windowing", "WindowCloseOrchestrator.cs"));
        foreach (var flag in new[] { "repository.IsMutating", "repository.IsChangingRepository",
                     "repository.RepositoryHistoryRewrite.IsInProgress", "maintenance.IsRunning",
                     "isUpdateRunning()", "clone.IsBusy", "create.IsBusy" })
            Assert.Contains(flag, safety, StringComparison.Ordinal);
        Assert.Contains("session.IsUnsafeToClose", close, StringComparison.Ordinal);
        var recheck = close.IndexOf("session.IsUnsafeToClose || !ReferenceEquals(window.Content, content)", StringComparison.Ordinal);
        var scheduling = close.IndexOf("ScheduleShutdown();", recheck, StringComparison.Ordinal);
        Assert.True(recheck >= 0 && scheduling > recheck);
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
        var coordinator = File.ReadAllText(Path.Combine(
            root, "src", "CSharpGit.Presentation.Core", "RepositoryChangeCoordinator.cs"));

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
        Assert.Contains("_pendingStatus = null;", coordinator, StringComparison.Ordinal);
        Assert.Contains("_repositoryStatusStop?.Cancel();", coordinator, StringComparison.Ordinal);
        Assert.Contains("_lifetimeStop.Cancel();", coordinator, StringComparison.Ordinal);
        Assert.Contains("_dispatcher.HasThreadAccess", coordinator, StringComparison.Ordinal);
        Assert.Contains("if (IsShuttingDown || !_repositoryChangeMonitoringInitialized) return;", refresh, StringComparison.Ordinal);
        Assert.Contains("!IsShuttingDown && DispatcherQueue.HasThreadAccess", refresh, StringComparison.Ordinal);
        Assert.Contains("ClearRepositoryPresentationRefreshQueue();", refresh, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkingTreeStatusCheckRechecksLifecycleAfterAwaitAndBeforeRestart()
    {
        var coordinator = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "CSharpGit.Presentation.Core",
            "RepositoryChangeCoordinator.cs"));

        var awaitIndex = coordinator.IndexOf("await _statusReader.ReadAsync", StringComparison.Ordinal);
        var postAwaitFenceIndex = coordinator.IndexOf(
            "if (!_initialized || _disposed) return;",
            awaitIndex,
            StringComparison.Ordinal);
        var finallyIndex = coordinator.IndexOf("finally", awaitIndex, StringComparison.Ordinal);
        var restartFenceIndex = coordinator.IndexOf(
            "StartStatusLoopIfNeeded();",
            finallyIndex,
            StringComparison.Ordinal);

        Assert.True(awaitIndex >= 0);
        Assert.True(postAwaitFenceIndex > awaitIndex);
        Assert.True(finallyIndex > postAwaitFenceIndex);
        Assert.True(restartFenceIndex > finallyIndex);
    }

    [Fact]
    public void MacOsDoesNotSynchronouslyStopHostInWindowClosed()
    {
        var app = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "CSharpGit.Presentation", "App.xaml.cs"));
        var close = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "CSharpGit.Presentation", "Windowing", "WindowCloseOrchestrator.cs"));
        Assert.Contains("await host.StopHostAsync();", close, StringComparison.Ordinal);
        Assert.Contains("_hostStopTask ??= StopHostCoreAsync()", app, StringComparison.Ordinal);
        Assert.Contains("await _host.StopAsync(timeout.Token)", app, StringComparison.Ordinal);
        var closed = app.IndexOf("_window.Closed +=", StringComparison.Ordinal);
        var next = app.IndexOf("if (Environment.GetEnvironmentVariable", closed, StringComparison.Ordinal);
        Assert.True(closed >= 0 && next > closed);
        Assert.DoesNotContain("StopHost()", app[closed..next], StringComparison.Ordinal);
    }

    [Fact]
    public void AsyncShutdownIsBoundedAndIdempotent()
    {
        var close = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "CSharpGit.Presentation", "Windowing", "WindowCloseOrchestrator.cs"));
        var session = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "CSharpGit.Presentation", "Windowing", "WindowSession.cs"));
        var lifecycle = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "CSharpGit.Presentation", "MainPage.Lifecycle.cs"));
        var perf = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "CSharpGit.Presentation", "MainPage.HistoryPerformanceDiagnostics.cs"));
        Assert.Contains("await UiBoundedTaskWait.WaitAsync(", close, StringComparison.Ordinal);
        Assert.Contains("DiagnosticsShutdownBudget,", close, StringComparison.Ordinal);
        Assert.Contains("WindowCloseStage.FinalClose", close, StringComparison.Ordinal);
        Assert.Contains("_coordinator.AuthorizeFinalClose();", close, StringComparison.Ordinal);
        Assert.Contains("window.DispatcherQueue.TryEnqueue(BeginShutdown)", close, StringComparison.Ordinal);
        Assert.Contains("CloseSchedulingRejected", close, StringComparison.Ordinal);
        Assert.Contains("lock (_gate)", session, StringComparison.Ordinal);
        Assert.Contains("scope.Dispose()", session, StringComparison.Ordinal);
        Assert.Contains("Interlocked.Exchange(ref _shutdownFinalized, 1)", lifecycle, StringComparison.Ordinal);
        var prepare = lifecycle.IndexOf("public Task PrepareForShutdown()", StringComparison.Ordinal);
        var stopMonitoring = lifecycle.IndexOf("ShutdownRepositoryChangeMonitoring();", prepare, StringComparison.Ordinal);
        var beginShutdown = lifecycle.IndexOf("public void BeginShutdown()", prepare, StringComparison.Ordinal);
        Assert.True(prepare >= 0 && stopMonitoring > prepare && stopMonitoring < beginShutdown);
        Assert.DoesNotContain(".GetAwaiter()\\n            .GetResult()", perf, StringComparison.Ordinal);
    }

    [Fact]
    public void MacOsShutdownIsScheduledBeforeAnyResourceCleanup()
    {
        var close = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "CSharpGit.Presentation", "Windowing", "WindowCloseOrchestrator.cs"));
        var scheduleStart = close.IndexOf("private void ScheduleShutdown", StringComparison.Ordinal);
        var queue = close.IndexOf("window.DispatcherQueue.TryEnqueue(BeginShutdown)", scheduleStart, StringComparison.Ordinal);
        var shutdown = close.IndexOf("private void BeginShutdown", queue, StringComparison.Ordinal);
        var cleanup = close.IndexOf("private async Task FinalizeWindowShutdownAsync", shutdown, StringComparison.Ordinal);
        Assert.True(scheduleStart >= 0 && queue > scheduleStart && shutdown > queue && cleanup > shutdown);
        Assert.Contains("_coordinator.TrySchedule", close, StringComparison.Ordinal);
        Assert.Contains("if (!_coordinator.BeginShutdown()) return;", close, StringComparison.Ordinal);
        Assert.DoesNotContain("if (window.DispatcherQueue.HasThreadAccess) CloseAuthorizedWindow();", close, StringComparison.Ordinal);
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
