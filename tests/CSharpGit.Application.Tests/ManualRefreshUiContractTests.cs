namespace CSharpGit.Application.Tests;

public sealed class ManualRefreshUiContractTests
{
    [Fact]
    public void ActivationExternalMonitoringAndIndicatorKeepRefreshExplicit()
    {
        var root = FindRepositoryRoot();
        var app = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "App.xaml.cs"));
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Views", "RepositoryToolbarView.xaml"));
        var refresh = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Workflows", "RepositoryRefreshController.cs"))
            + File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Workflows", "RepositoryPresentationRefreshController.cs"))
            + File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Views", "RepositoryToolbarView.xaml.cs"));
        var composition = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Composition/WindowWorkspaceComposition.Repository.cs"));
        var refreshAction = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Workflows", "RepositoryManualRefreshController.cs"));
        var monitor = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation.Core", "RepositoryChangeMonitor.cs"));
        var coordinator = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation.Core", "RepositoryChangeCoordinator.cs"));
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.cs"));
        var reader = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Git", "GitWorkingTreeStatusReader.cs"));
        var application = Path.Combine(root, "src", "CSharpGit.Application");
        var lifecycle = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Composition/WindowWorkspaceComposition.Lifetime.cs"));
        var intentIndex = File.ReadAllText(Path.Combine(root, ".idd", "intent", "INDEX.md"));

        Assert.DoesNotContain("_window.Activated +=", app);
        Assert.Contains("mainPage.Loaded += RunDesktopCloseCheckWhenLoaded", app);
        Assert.DoesNotContain("RefreshWhenActivatedAsync", app);
        Assert.DoesNotContain("InitializeRepositoryChangeMonitoring()", app);
        Assert.Contains("_repositoryRefresh.InitializeRepositoryChangeMonitoring();", composition);

        Assert.Contains("x:Name=\"RefreshButton\"", xaml);
        Assert.Contains("Click=\"RefreshIndicator_Click\"", xaml);
        Assert.Contains("x:Name=\"RefreshIcon\"", xaml);
        Assert.Contains("x:Name=\"RefreshRequiredIcon\"", xaml);
        Assert.Contains("SystemFillColorSuccessBrush", xaml);
        Assert.Contains("SystemFillColorCriticalBrush", xaml);
        Assert.Contains("FontSize=\"{StaticResource Icon.Normal}\"", xaml);
        Assert.Contains("x:Name=\"RefreshProgressRing\"", xaml);
        Assert.DoesNotContain("x:Name=\"BusyIndicator\"", xaml);
        Assert.DoesNotContain("Text=\"Refresh\"", xaml);

        Assert.Contains("IsRefreshRequired", refresh);
        Assert.Contains("_repositoryChangeCoordinator.PublishBaseline(", refresh);
        Assert.Contains("_repositoryChangeCoordinator.ChangeRepository(", refresh);
        Assert.Contains("DisplayedRefreshBaselineRevision", refresh);
        Assert.Contains("OwnedFollowupRefreshRequested", refresh);
        Assert.Contains("TryConsumeRacedRefresh", refresh);
        Assert.Contains("RepositoryMutationCompleted", refresh);
        Assert.Contains("RefreshAfterOwnedMutationAsync", refresh);
        Assert.Contains("IWorkingTreeStatusReader", coordinator);
        Assert.Contains("RunStatusLoopAsync", coordinator);
        Assert.Contains("TryConsumeRacedRefresh", coordinator);
        Assert.Contains("OwnedMutationCompleted", coordinator);
        Assert.Contains("RequestOwnedFollowupIfNeeded", coordinator);
        Assert.Contains("GetInvalidationsSince", coordinator);
        Assert.Contains("StatusProbeFailed", coordinator);
        Assert.Contains("WatcherOverflow", coordinator);
        Assert.DoesNotContain("IRepositoryRefreshProbe", refresh + coordinator);
        Assert.DoesNotContain("_repositoryChangeMonitor.Suspend()", refresh);
        Assert.DoesNotContain("_repositoryChangeMonitor.Resume()", refresh);
        Assert.Contains("Repository has changed externally. Refresh to see the latest state.", refresh);
        Assert.Contains("RefreshRequiredIcon.Visibility", refresh);
        Assert.Contains("Microsoft.UI.Colors.Goldenrod", refresh);
        Assert.Contains("_toolbar.SetRefreshPresentation(", refresh);
        Assert.Contains("RefreshProgressRing.IsActive = refreshing", refresh);

        Assert.Contains("_setRefreshInProgress(true)", refreshAction);
        Assert.DoesNotContain("MainPage", refreshAction);
        Assert.False(File.Exists(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.RefreshIndicator.cs")));
        Assert.Contains("RefreshAsyncForDesktopCheck", refreshAction);
        Assert.Contains("_refreshPresentation()", refreshAction);
        Assert.Contains("_setRefreshInProgress(false)", refreshAction);

        Assert.Contains("FileSystemWatcher", monitor);
        Assert.Contains("repository.WorkingDirectory", monitor);
        Assert.Contains("repository.GitDirectory", monitor);
        Assert.Contains("repository.GitCommonDirectory", monitor);
        Assert.Contains("RepositoryInvalidationBatch", monitor);
        Assert.Contains("GetInvalidationsSince", monitor);
        Assert.Contains("args.OldFullPath", monitor);
        Assert.Contains("objects/", monitor);
        Assert.DoesNotContain("public void Suspend()", monitor);
        Assert.DoesNotContain("public void Resume()", monitor);
        Assert.DoesNotContain("RefreshAllAsync", monitor);
        Assert.DoesNotContain("RefreshStateAsync", monitor);

        Assert.Contains("DisplayedWorkingTreeStatusSnapshot", viewModel);
        Assert.Contains("DisplayedRefreshBaselineRevision", viewModel);
        Assert.Contains("PublishDisplayedWorkingTreeBaseline", viewModel);
        Assert.Contains("ReadWithWorkingTreeStatusAsync", viewModel);
        Assert.DoesNotContain("ReadWithRefreshFingerprintAsync", viewModel);

        Assert.Contains("WorkingTreeChangeProbe", reader);
        Assert.Contains("\"status\", \"--porcelain=v2\", \"-z\", \"--untracked-files=all\"", reader);
        Assert.DoesNotContain("hash-object", reader);
        Assert.DoesNotContain("ls-files", reader);

        Assert.False(File.Exists(Path.Combine(application, "RepositoryStateSession.cs")));
        Assert.False(File.Exists(Path.Combine(application, "Abstractions", "IRepositoryStateSession.cs")));
        Assert.DoesNotContain("IRepositoryStateSession", app, StringComparison.Ordinal);

        Assert.Contains("_repositoryRefresh.ShutdownRepositoryChangeMonitoring()", lifecycle);
        Assert.Contains("IDD-0018", intentIndex);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;
        return directory?.FullName ??
            throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
