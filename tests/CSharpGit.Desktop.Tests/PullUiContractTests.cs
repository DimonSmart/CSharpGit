namespace CSharpGit.Desktop.Tests;

public sealed class PullUiContractTests
{
    [Fact]
    public void PullToolbarHasDynamicDefaultAndThreeOneOffOperations()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var ui = File.ReadAllText(Path.Combine(presentation, "MainPage.xaml"));
        var view = File.ReadAllText(Path.Combine(presentation, "MainPage.xaml.cs"));
        var syncVm = File.ReadAllText(Path.Combine(presentation, "ViewModels", "RepositorySyncViewModel.cs"));

        Assert.Contains("x:Name=\"PullPrimaryButton\"", ui, StringComparison.Ordinal);
        Assert.Contains("Click=\"Pull_Click\"", ui, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PullMenuButton\"", ui, StringComparison.Ordinal);
        Assert.Contains("RepositorySync.PullButtonText", ui, StringComparison.Ordinal);
        Assert.Contains("RepositorySync.PullAccessibleName", ui, StringComparison.Ordinal);
        Assert.Contains("RepositorySync.PullTooltip", ui, StringComparison.Ordinal);
        Assert.Contains("RepositorySync.CanPull", ui, StringComparison.Ordinal);
        Assert.Contains("nameof(PullButtonText)", syncVm, StringComparison.Ordinal);
        foreach (var action in new[] { "PullFastForwardOnly_Click", "PullMerge_Click", "PullRebase_Click" })
        {
            Assert.Contains($"Click=\"{action}\"", ui, StringComparison.Ordinal);
            Assert.Contains($"void {action}(", view, StringComparison.Ordinal);
        }
        Assert.Contains("RepositorySync.FastForwardPullAccessibleName", ui, StringComparison.Ordinal);
        Assert.Contains("RepositorySync.MergePullAccessibleName", ui, StringComparison.Ordinal);
        Assert.Contains("RepositorySync.RebasePullAccessibleName", ui, StringComparison.Ordinal);
        Assert.Contains("RepositorySync.IsFastForwardOnlyDefault", ui, StringComparison.Ordinal);
        Assert.Contains("RepositorySync.IsMergeDefault", ui, StringComparison.Ordinal);
        Assert.Contains("RepositorySync.IsRebaseDefault", ui, StringComparison.Ordinal);
        Assert.Contains("MenuFlyoutItem Text=\"Pull settings…\"", ui, StringComparison.Ordinal);
        Assert.Contains("Click=\"PullSettings_Click\"", ui, StringComparison.Ordinal);
        Assert.DoesNotContain("PullGitConfiguration_Click", ui, StringComparison.Ordinal);
        Assert.DoesNotContain("MenuFlyoutSubItem Text=\"Default strategy\"", ui, StringComparison.Ordinal);
        Assert.DoesNotContain("ToggleMenuFlyoutItem Text=\"Force Git autostash\"", ui, StringComparison.Ordinal);
        Assert.DoesNotContain("Pull once:", ui, StringComparison.Ordinal);
        Assert.DoesNotContain("IRepositorySyncService", view, StringComparison.Ordinal);
    }

    [Fact]
    public void PullSettingsNavigateToExistingGeneralPageEvenWhenPullUnavailable()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var ui = File.ReadAllText(Path.Combine(presentation, "MainPage.xaml"));
        var mainSettings = File.ReadAllText(Path.Combine(presentation, "MainPage.Settings.cs"));
        var controller = File.ReadAllText(Path.Combine(presentation, "SettingsWindowController.cs"));
        var settingsPage = File.ReadAllText(Path.Combine(presentation, "SettingsPage.xaml.cs"));
        var settings = File.ReadAllText(Path.Combine(presentation, "SettingsPage.xaml"));

        Assert.Contains("PullStrategyComboBox", settings, StringComparison.Ordinal);
        Assert.Contains("ForcePullAutoStashToggle", settings, StringComparison.Ordinal);
        Assert.Contains("OpenSettingsWindow(SettingsSection.General, focusPullStrategy: true)", mainSettings, StringComparison.Ordinal);
        Assert.Contains("_page?.SelectSection(section, focusPullStrategy)", controller, StringComparison.Ordinal);
        Assert.Contains("PullStrategyComboBox.StartBringIntoView()", settingsPage, StringComparison.Ordinal);
        Assert.Contains("PullStrategyComboBox.Focus(FocusState.Programmatic)", settingsPage, StringComparison.Ordinal);
        var menuSettings = ui.IndexOf("MenuFlyoutItem Text=\"Pull settings…\"", StringComparison.Ordinal);
        var menuEnd = ui.IndexOf("</MenuFlyout>", menuSettings, StringComparison.Ordinal);
        Assert.True(menuSettings >= 0 && menuEnd > menuSettings);
        Assert.DoesNotContain("IsEnabled=\"{Binding RepositorySync.CanPull}\"",
            ui[menuSettings..menuEnd], StringComparison.Ordinal);
    }

    [Fact]
    public void PullRemainsOwnedByRepositorySyncViewModel()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var syncVm = File.ReadAllText(Path.Combine(presentation, "ViewModels", "RepositorySyncViewModel.cs"));
        var syncContext = File.ReadAllText(Path.Combine(presentation, "ViewModels", "OpenRepositoryViewModel.RepositorySync.cs"));
        var lifecycle = File.ReadAllText(Path.Combine(presentation, "ViewModels", "OpenRepositoryViewModel.cs"));

        Assert.Contains("_settings.Changed += PullSettings_Changed", syncVm, StringComparison.Ordinal);
        Assert.Contains("_settings.Changed -= PullSettings_Changed", syncVm, StringComparison.Ordinal);
        Assert.Contains("HasUnmergedPaths", syncVm, StringComparison.Ordinal);
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
