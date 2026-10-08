namespace CSharpGit.Desktop.Tests;

public sealed class PullUiContractTests
{
    [Fact]
    public void PullToolbarPreservesOneClickAndHasSeparateAccessibleStrategyMenu()
    {
        var root = FindRepositoryRoot();
        var ui = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var view = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml.cs"));

        Assert.Contains("x:Name=\"PullPrimaryButton\"", ui, StringComparison.Ordinal);
        Assert.Contains("Click=\"Pull_Click\"", ui, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PullMenuButton\"", ui, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Pull using default strategy\"", ui, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Pull strategies and preferences\"", ui, StringComparison.Ordinal);
        Assert.Contains("MenuFlyoutSubItem Text=\"Default strategy\"", ui, StringComparison.Ordinal);
        Assert.Contains("ToggleMenuFlyoutItem Text=\"Force Git autostash\"", ui, StringComparison.Ordinal);
        foreach (var action in new[] {
            "PullGitConfiguration_Click", "PullMerge_Click",
            "PullRebase_Click", "PullFastForwardOnly_Click",
            "DefaultPullGitConfiguration_Click", "DefaultPullMerge_Click",
            "DefaultPullRebase_Click", "DefaultPullFastForwardOnly_Click"
        })
        {
            Assert.Contains($"Click=\"{action}\"", ui, StringComparison.Ordinal);
            Assert.Contains($"void {action}(", view, StringComparison.Ordinal);
        }
        Assert.Contains("RepositorySync.CurrentPullBranch", ui, StringComparison.Ordinal);
        Assert.Contains("ConverterParameter=Pull", ui, StringComparison.Ordinal);
        Assert.Contains("RepositorySync.PullTooltip", ui, StringComparison.Ordinal);
        Assert.DoesNotContain("IRepositorySyncService", view, StringComparison.Ordinal);
    }

    [Fact]
    public void PreferencesAreGlobalAndSharedWithSettingsScreen()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var settings = File.ReadAllText(Path.Combine(presentation, "SettingsPage.xaml"));
        var syncVm = File.ReadAllText(Path.Combine(presentation, "ViewModels", "RepositorySyncViewModel.cs"));
        var syncContext = File.ReadAllText(Path.Combine(presentation, "ViewModels", "OpenRepositoryViewModel.RepositorySync.cs"));
        var lifecycle = File.ReadAllText(Path.Combine(presentation, "ViewModels", "OpenRepositoryViewModel.cs"));

        Assert.Contains("PullStrategyComboBox", settings, StringComparison.Ordinal);
        Assert.Contains("ForcePullAutoStashToggle", settings, StringComparison.Ordinal);
        Assert.Contains("_settings.SetDefaultPullStrategyAsync", syncVm, StringComparison.Ordinal);
        Assert.Contains("_settings.SetForcePullAutoStashAsync", syncVm, StringComparison.Ordinal);
        Assert.Contains("_settings.Changed += PullSettings_Changed", syncVm, StringComparison.Ordinal);
        Assert.Contains("_settings.Changed -= PullSettings_Changed", syncVm, StringComparison.Ordinal);
        Assert.Contains("RunSyncMutationAsync", syncVm, StringComparison.Ordinal);
        Assert.Contains("MutateAsync(", syncContext, StringComparison.Ordinal);
        Assert.Contains("catch (OperationCanceledException exception) { cancellation = exception; }", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("_syncService.PullAsync", lifecycle, StringComparison.Ordinal);
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
