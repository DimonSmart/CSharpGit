namespace CSharpGit.Desktop.Tests;

public sealed class RepositoryMaintenanceUiContractTests
{
    [Fact]
    public void MainMenuExposesOptimizationBeforeSettingsWithoutLegacyOperations()
    {
        var source = ReadSource("src/CSharpGit.Presentation/MainPage.xaml");

        var optimize = source.IndexOf("Optimize repository…", StringComparison.Ordinal);
        var settings = source.IndexOf("Settings…", optimize, StringComparison.Ordinal);
        var legacyMenu = "More Git " + "operations…";

        Assert.True(optimize >= 0);
        Assert.True(settings > optimize);
        Assert.DoesNotContain(legacyMenu, source, StringComparison.Ordinal);
    }

    [Fact]
    public void MaintenanceUsesTypedServiceAndExistingMutationGate()
    {
        var source = ReadSource("src/CSharpGit.Presentation/MainPage.RepositoryMaintenance.cs");
        var viewModel = ReadSource(
            "src/CSharpGit.Presentation/ViewModels/OpenRepositoryViewModel.RepositoryMaintenance.cs");

        var feature = ReadSource("src/CSharpGit.Presentation.Core/ViewModels/RepositoryMaintenanceViewModel.cs");
        Assert.DoesNotContain("IRepositoryMaintenanceService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GarbageCollectAsync", source, StringComparison.Ordinal);
        Assert.Contains("IRepositoryMaintenanceService", feature, StringComparison.Ordinal);
        Assert.Contains("_service.GarbageCollectAsync(repository, options)", feature, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start", feature, StringComparison.Ordinal);

        Assert.Contains("_mutationGate.WaitAsync(0)", viewModel, StringComparison.Ordinal);
        Assert.Contains("IsMaintenanceSessionCurrent(expectedRepository)", viewModel, StringComparison.Ordinal);
        Assert.Contains("_mutationGate.Release()", viewModel, StringComparison.Ordinal);
        Assert.Contains("RefreshStateLocalOnlyAsync(includeHistory)", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("new SemaphoreSlim", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void AvailabilityUsesAnyRepositoryOperationAndRunningState()
    {
        var source = ReadSource("src/CSharpGit.Presentation/MainPage.RepositoryMaintenance.cs");

        var context = ReadSource("src/CSharpGit.Presentation/ViewModels/OpenRepositoryViewModel.RepositoryMaintenance.cs");
        Assert.Contains("RepositoryOperations.CurrentOperation == RepositoryOperation.None", context, StringComparison.Ordinal);
        Assert.Contains("!IsBusy", context, StringComparison.Ordinal);
        Assert.Contains("_repositoryMaintenance.CanStart", source, StringComparison.Ordinal);
        Assert.Contains("_repositoryMaintenance.IsRunning", source, StringComparison.Ordinal);
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

        var dialog = ReadSource("src/CSharpGit.Presentation/RepositoryMaintenanceDialog.cs");
        Assert.Contains("args.Cancel = true", dialog, StringComparison.Ordinal);
        Assert.Contains("args.GetDeferral()", dialog, StringComparison.Ordinal);
        Assert.Contains("if (workflow.IsRunning)", dialog, StringComparison.Ordinal);
        Assert.Contains("=> _repositoryMaintenance.IsRunning", source, StringComparison.Ordinal);
        Assert.Contains("page?.IsRepositoryMaintenanceInProgress == true", app, StringComparison.Ordinal);
        Assert.Contains("eventArgs.Cancel = true", app, StringComparison.Ordinal);
    }

    [Fact]
    public void PostGcRefreshIsTargetedAndUsesReflogAndWorktreeState()
    {
        var source = ReadSource("src/CSharpGit.Presentation/MainPage.RepositoryMaintenance.cs");
        var feature = ReadSource("src/CSharpGit.Presentation.Core/ViewModels/RepositoryMaintenanceViewModel.cs");

        Assert.Contains("includeHistory: _context.ShowMaintenanceReflog", feature, StringComparison.Ordinal);
        Assert.Contains("_worktreesViewModel.RefreshAsync(throwOnError: true)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AcknowledgeRepositoryRefresh()", feature, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshAllAsync", feature, StringComparison.Ordinal);
    }

    [Fact]
    public void StatisticsContentFitsWithinContentDialog()
    {
        var source = ReadSource("src/CSharpGit.Presentation/RepositoryMaintenanceDialog.cs");

        Assert.Contains("RepositoryMaintenanceContentWidth = 460", source, StringComparison.Ordinal);
        Assert.Contains("Width = RepositoryMaintenanceContentWidth", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MinWidth = 520", source, StringComparison.Ordinal);
        Assert.Contains("ColumnSpacing = 16", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FailureUsesExistingGitConsole()
    {
        var source = ReadSource("src/CSharpGit.Presentation/MainPage.RepositoryMaintenance.cs");
        var dialog = ReadSource("src/CSharpGit.Presentation/RepositoryMaintenanceDialog.cs");

        Assert.Contains("View Git console", dialog, StringComparison.Ordinal);
        Assert.Contains("_openConsole()", dialog, StringComparison.Ordinal);
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
