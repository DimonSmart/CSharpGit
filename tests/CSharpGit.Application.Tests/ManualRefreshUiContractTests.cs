namespace CSharpGit.Application.Tests;

public sealed class ManualRefreshUiContractTests
{
    [Fact]
    public void ActivationExternalMonitoringAndIndicatorKeepRefreshExplicit()
    {
        var root = FindRepositoryRoot();
        var app = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "App.xaml.cs"));
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var refresh = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.RepositoryRefresh.cs"));
        var refreshAction = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.RefreshIndicator.cs"));
        var monitor = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "RepositoryChangeMonitor.cs"));
        var lifecycle = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.Lifecycle.cs"));
        var intentIndex = File.ReadAllText(Path.Combine(root, ".idd", "intent", "INDEX.md"));

        Assert.Contains("_window.Activated +=", app);
        Assert.DoesNotContain("RefreshWhenActivatedAsync", app);
        Assert.Contains("InitializeRepositoryChangeMonitoring()", app);

        Assert.Contains("x:Name=\"RefreshButton\"", xaml);
        Assert.Contains("Click=\"RefreshIndicator_Click\"", xaml);
        Assert.Contains("x:Name=\"RefreshIcon\"", xaml);
        Assert.Contains("x:Name=\"RefreshProgressRing\"", xaml);
        Assert.DoesNotContain("x:Name=\"BusyIndicator\"", xaml);
        Assert.DoesNotContain("Text=\"Refresh\"", xaml);

        Assert.Contains("IsRefreshRequired", refresh);
        Assert.Contains("Repository has changed externally. Refresh to see the latest state.", refresh);
        Assert.Contains("Microsoft.UI.Colors.LimeGreen", refresh);
        Assert.Contains("Microsoft.UI.Colors.Red", refresh);
        Assert.Contains("Microsoft.UI.Colors.Goldenrod", refresh);
        Assert.Contains("RefreshProgressRing.IsActive = isRefreshing", refresh);
        Assert.Contains("_repositoryChangeMonitor.Acknowledge()", refresh);
        Assert.DoesNotContain("RefreshAsyncForDesktopCheck", refresh);
        Assert.DoesNotContain("FindRefreshButton", refresh);

        Assert.Contains("SetRefreshInProgress(true)", refreshAction);
        Assert.Contains("RefreshAsyncForDesktopCheck", refreshAction);
        Assert.Contains("RefreshPresentationCollections", refreshAction);
        Assert.Contains("SetRefreshInProgress(false)", refreshAction);

        Assert.Contains("FileSystemWatcher", monitor);
        Assert.Contains("repository.WorkingDirectory", monitor);
        Assert.Contains("repository.GitDirectory", monitor);
        Assert.Contains("RepositoryChanged", monitor);
        Assert.Contains("DebounceDelay", monitor);
        Assert.DoesNotContain("RefreshAllAsync", monitor);
        Assert.DoesNotContain("RefreshStateAsync", monitor);

        Assert.Contains("ShutdownRepositoryChangeMonitoring();", lifecycle);
        Assert.Contains("IDD-0018", intentIndex);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
