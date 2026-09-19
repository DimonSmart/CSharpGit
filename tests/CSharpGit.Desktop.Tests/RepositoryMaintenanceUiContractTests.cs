namespace CSharpGit.Desktop.Tests;

public sealed class RepositoryMaintenanceUiContractTests
{
    [Fact]
    public void MainMenuExposesOptimizationBetweenMoreOperationsAndSettings()
    {
        var source = ReadSource("src/CSharpGit.Presentation/MainPage.xaml");

        var moreOperations = source.IndexOf("More Git operations…", StringComparison.Ordinal);
        var optimize = source.IndexOf("Optimize repository…", StringComparison.Ordinal);
        var settings = source.IndexOf("Settings…", StringComparison.Ordinal);

        Assert.True(moreOperations >= 0);
        Assert.True(optimize > moreOperations);
        Assert.True(settings > optimize);
    }

    [Fact]
    public void MaintenanceUsesTypedServiceAndExistingMutationGate()
    {
        var source = ReadSource("src/CSharpGit.Presentation/MainPage.RepositoryMaintenance.cs");
        var viewModel = ReadSource(
            "src/CSharpGit.Presentation/ViewModels/OpenRepositoryViewModel.RepositoryMaintenance.cs");

        Assert.Contains("IRepositoryMaintenanceService", source, StringComparison.Ordinal);
        Assert.Contains("service.GarbageCollectAsync(repository, options)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start", source, StringComparison.Ordinal);

        Assert.Contains("_mutationGate.WaitAsync(0)", viewModel, StringComparison.Ordinal);
        Assert.Contains("RefreshStateLocalOnlyAsync(includeHistory)", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("new SemaphoreSlim", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void AvailabilityUsesAnyRepositoryOperationAndRunningState()
    {
        var source = ReadSource("src/CSharpGit.Presentation/MainPage.RepositoryMaintenance.cs");

        Assert.Contains("_viewModel.CurrentOperation == RepositoryOperation.None", source, StringComparison.Ordinal);
        Assert.Contains("!_viewModel.IsBusy", source, StringComparison.Ordinal);
        Assert.Contains("!_repositoryMaintenanceInProgress", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RepositoryOperation.Merge", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RepositoryOperation.Rebase", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RepositoryOperation.CherryPick", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RepositoryOperation.Revert", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RunningDialogStaysOpenAndBlocksRepeatAndWindowClose()
    {
        var source = ReadSource("src/CSharpGit.Presentation/MainPage.RepositoryMaintenance.cs");
        var app = ReadSource("src/CSharpGit.Presentation/App.xaml.cs");

        Assert.Contains("args.Cancel = true", source, StringComparison.Ordinal);
        Assert.Contains("args.GetDeferral()", source, StringComparison.Ordinal);
        Assert.Contains("if (_repositoryMaintenanceInProgress)", source, StringComparison.Ordinal);
        Assert.Contains("page?.IsRepositoryMaintenanceInProgress == true", app, StringComparison.Ordinal);
        Assert.Contains("eventArgs.Cancel = true", app, StringComparison.Ordinal);
    }

    [Fact]
    public void PostGcRefreshIsTargetedAndUsesReflogAndWorktreeState()
    {
        var source = ReadSource("src/CSharpGit.Presentation/MainPage.RepositoryMaintenance.cs");

        Assert.Contains("includeHistory: _viewModel.ShowReflog", source, StringComparison.Ordinal);
        Assert.Contains("RefreshWorktreePresentationAsync(throwOnError: true)", source, StringComparison.Ordinal);
        Assert.Contains("AcknowledgeRepositoryRefresh()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshAllAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StatisticsContentFitsWithinContentDialog()
    {
        var source = ReadSource("src/CSharpGit.Presentation/MainPage.RepositoryMaintenance.cs");

        Assert.Contains("RepositoryMaintenanceContentWidth = 460", source, StringComparison.Ordinal);
        Assert.Contains("Width = RepositoryMaintenanceContentWidth", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MinWidth = 520", source, StringComparison.Ordinal);
        Assert.Contains("ColumnSpacing = 16", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FailureUsesExistingGitConsole()
    {
        var source = ReadSource("src/CSharpGit.Presentation/MainPage.RepositoryMaintenance.cs");

        Assert.Contains("View Git console", source, StringComparison.Ordinal);
        Assert.Contains("OpenGitConsole(null, manualOpen: true)", source, StringComparison.Ordinal);
    }

    private static string ReadSource(string relativePath) =>
        File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "CSharpGit.slnx")))
                return current.FullName;
            current = current.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the repository root from the test output directory.");
    }
}
