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
        Assert.Contains("History.Invalidate();", viewModel);
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
        Assert.Contains("x:Name=\"RepositorySelectorList\"", xaml);
        Assert.Contains("ItemClick=\"RepositorySelectorList_ItemClick\"", xaml);
        Assert.Contains("UseMiddleEllipsis=\"True\"", xaml);
        Assert.Contains("Text=\"{Binding Branch}\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Current repository\"", xaml);
        Assert.Contains("Text=\"Open repository…\"", xaml);
        Assert.Contains("Text=\"Repositories…\"", xaml);
        Assert.Contains("Text=\"Close repository\"", xaml);
        Assert.Contains("IsEnabled=\"{Binding CanChangeRepository}\"", xaml);

        Assert.Contains("QuickRepositoryLimit = 8", switching);
        Assert.Contains("BuildQuickList(", switching);
        Assert.Contains("RepositorySelectorEntry", switching);
        Assert.Contains("repository.Path,", switching);
        Assert.Contains("repository.LastBranchName", switching);
        Assert.Contains("_viewModel.CurrentBranchName", switching);
        Assert.Contains("_desktopShellService.OpenFolderDescription", switching);
        Assert.Contains("Content=\"Copy repository path\"", xaml);
        Assert.Contains("OpenFolderInDesktopShellAsync", switching);
        Assert.Contains("ShowCreateRepositoryAsync", switching);
        Assert.Contains("ShowUnavailableRepositoryAsync", switching);

        Assert.Contains("e.Key == VirtualKey.O", page);
        Assert.Contains("await OpenRepositoryPickerAsync();", page);
    }


    [Fact]
    public void RepositorySelectorLivesInSidebarHeaderAndToolbarContainsActionsOnly()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var xaml = File.ReadAllText(Path.Combine(presentation, "MainPage.xaml"));
        var page = File.ReadAllText(Path.Combine(presentation, "MainPage.xaml.cs"));
        var switching = File.ReadAllText(Path.Combine(
            presentation,
            "MainPage.RepositorySwitching.cs"));

        Assert.Contains("x:Name=\"RepositoryHeader\"", xaml);
        Assert.Contains("<ColumnDefinition Width=\"270\" MinWidth=\"180\" MaxWidth=\"430\" />", xaml);
        Assert.Contains("<controls:GridSplitter Grid.Row=\"2\"", xaml);
        Assert.Contains("<TreeView x:Name=\"RepositoryTree\"", xaml);
        Assert.Contains("AutomationProperties.HelpText=\"{Binding Repository.WorkingDirectory}\"", xaml);
        Assert.Contains("Grid.ColumnSpan=\"3\" Visibility=\"{Binding HasActiveOperation", xaml);
        Assert.Contains("x:Name=\"StatusBar\" Grid.Row=\"3\" Grid.ColumnSpan=\"3\"", xaml);

        var headerStart = xaml.IndexOf("<Border x:Name=\"RepositoryHeader\"", StringComparison.Ordinal);
        var toolbarStart = xaml.IndexOf("<Border x:Name=\"MainToolbar\"", StringComparison.Ordinal);
        var operationStart = xaml.IndexOf("<controls:OperationBanner", toolbarStart, StringComparison.Ordinal);
        var selectorStart = xaml.IndexOf("x:Name=\"RepositorySelectorButton\"", StringComparison.Ordinal);
        var treeStart = xaml.IndexOf("x:Name=\"RepositoryTree\"", StringComparison.Ordinal);
        Assert.True(headerStart >= 0 && selectorStart > headerStart && toolbarStart > selectorStart);
        Assert.True(operationStart > toolbarStart && treeStart > operationStart);

        var toolbar = xaml[toolbarStart..operationStart];
        Assert.DoesNotContain("RepositorySelectorButton", toolbar);
        Assert.DoesNotContain("ToolbarBranchText", toolbar);
        Assert.DoesNotContain("ToolbarProductTextStyle", toolbar);
        Assert.Contains("CommitNavigationButton", toolbar);
        Assert.Contains("Text=\"Fetch\"", toolbar);
        Assert.Contains("ConverterParameter=Pull", toolbar);
        Assert.Contains("ConverterParameter=Push", toolbar);
        Assert.Contains("x:Name=\"RefreshButton\"", toolbar);
        Assert.Contains("AutomationProperties.Name=\"Application menu\"", toolbar);

        Assert.DoesNotContain("RepositorySelectorPath", xaml);
        Assert.DoesNotContain("ToolbarBranchText", xaml);
        Assert.Contains("x:Name=\"StatusBranchText\"", xaml);
        Assert.DoesNotContain("ToolbarBranchText.Text", page);
        Assert.Contains("StatusBranchText.Text = branch;", page);
        Assert.DoesNotContain("RepositorySelectorPath", switching);
        Assert.Contains("QuickRepositoryLimit = 8", switching);
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
