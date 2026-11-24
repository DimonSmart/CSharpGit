using System.Xml.Linq;

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
        var lifecycleOwner = File.ReadAllText(Path.Combine(presentation, "Workflows", "RepositorySwitchController.cs"));
        var recent = File.ReadAllText(Path.Combine(
            presentation,
            "Workflows", "RecentRepositoryPickerWorkflow.cs"));
        var creation = File.ReadAllText(Path.Combine(
            presentation,
            "Workflows", "RepositoryCreationWorkflow.cs"));
        var refresh = File.ReadAllText(Path.Combine(
            presentation,
            "Workflows", "RepositoryRefreshController.cs"));
        var coordinator = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CSharpGit.Presentation.Core",
            "RepositoryChangeCoordinator.cs"));

        Assert.Contains("internal Task<bool> OpenRepositoryPathAsync", viewModel);
        Assert.Contains("OpenRepositoryPathFromLifecycleAsync", viewModel);
        Assert.Contains("internal bool CloseRepository()", viewModel);
        Assert.Contains("History.Invalidate();", viewModel);
        Assert.Contains("ClearRepositoryPresentation();", viewModel);
        Assert.Contains("public bool CanChangeRepository", viewModel);
        Assert.Contains("_isMutating", viewModel);
        Assert.Contains("_repositoryChangeInProgress", viewModel);

        Assert.False(File.Exists(Path.Combine(presentation, "MainPage.RepositorySwitching.cs")));
        Assert.Contains("internal async Task<bool> TrySwitchAsync", lifecycleOwner);
        Assert.Contains("private bool CloseCore", lifecycleOwner);
        Assert.Contains("if (!opened || _isShuttingDown()) return false;", lifecycleOwner);
        Assert.Contains("_viewModel.CommitCreation.CommitMessage = string.Empty;", lifecycleOwner);

        var launcher = File.ReadAllText(Path.Combine(presentation, "Views", "StartScreenView.xaml.cs"));
        Assert.Contains("await _actions.SwitchRepository(item.Path);", launcher);
        Assert.Contains("await folderPicker.PickFolderAsync()", recent);
        Assert.Contains("if (!string.IsNullOrEmpty(path) && canChangeRepository())", recent);
        Assert.DoesNotContain("QueuePath(item.Path)", recent);

        Assert.Contains("_switchRepository(path, discardDraft, admission)", creation);
        Assert.Contains("TryBeginRepositoryLifecycle()", creation);
        Assert.DoesNotContain("ConfirmDiscardCommitMessageForRepositorySwitchAsync", creation);

        Assert.Contains("_repositoryChangeCoordinator.ChangeRepository(", refresh);
        Assert.Contains("_monitor.Stop();", coordinator);
        Assert.Contains("_monitor.Start(repository);", coordinator);
    }

    [Fact]
    public void RepositorySelectorAndKeyboardExposeTheSameWorkflow()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var xaml = File.ReadAllText(Path.Combine(presentation, "MainPage.xaml"))
            + File.ReadAllText(Path.Combine(presentation, "Views", "RepositorySelectorView.xaml"))
            + File.ReadAllText(Path.Combine(presentation, "Views", "RepositoryToolbarView.xaml"));
        var page = File.ReadAllText(Path.Combine(presentation, "Composition/WindowWorkspaceComposition.cs"));
        var switching = File.ReadAllText(Path.Combine(
            presentation,
            "Views", "RepositorySelectorView.xaml.cs"));
        var switchingWorkflow =
            File.ReadAllText(Path.Combine(presentation, "Composition/WindowWorkspaceComposition.Repository.cs"))
            + File.ReadAllText(Path.Combine(presentation, "Workflows", "RepositorySwitchController.cs"));

        Assert.Contains("x:Name=\"RepositorySelectorButton\"", xaml);
        Assert.Contains("Opening=\"RepositorySelectorFlyout_Opening\"", xaml);
        Assert.Contains("x:Name=\"RepositorySelectorList\"", xaml);
        Assert.Contains("ItemClick=\"RepositorySelectorList_ItemClick\"", xaml);
        Assert.Contains("UseMiddleEllipsis=\"True\"", xaml);
        Assert.Contains("Text=\"{Binding Branch}\"", xaml);
        Assert.Contains("Text=\"{Binding DisplayPath}\"", xaml);
        Assert.Contains("Visibility=\"{Binding HeaderVisibility}\"", xaml);
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
        Assert.Contains("Text=\"Copy repository path\"", xaml);
        Assert.Contains("Icon.Glyph.Copy", xaml);
        Assert.Contains("_actions.OpenFolder(path)", switching);
        Assert.Contains("OpenFolderInDesktopShellAsync", switchingWorkflow);
        Assert.Contains("x:Name=\"RepositorySelectorOpenTerminalButton\"", xaml);
        Assert.Contains("Click=\"RepositorySelectorOpenTerminal_Click\"", xaml);
        Assert.Contains("Open Terminal Here", xaml);
        Assert.Contains("_desktopShellService.OpenTerminalAsync(path)", switching);
        Assert.Contains("RepositorySelectorOpenTerminalButton.IsEnabled", switching);
        var shellComposition = File.ReadAllText(Path.Combine(presentation, "Composition/WindowWorkspaceComposition.Lifetime.cs"));
        Assert.Contains("_creationWorkflow.ShowAsync()", shellComposition);
        Assert.Contains("_repositorySwitch.ShowUnavailableAsync", switchingWorkflow);

        Assert.Contains("e.Key == VirtualKey.O", page);
        Assert.Contains("RunPageUiOperation(OpenRepositoryPickerAsync, \"Open repository shortcut\")", page);
    }


    [Fact]
    public void RepositorySelectorUsesSharedCompactDensity()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var xaml = XDocument.Load(Path.Combine(presentation, "Views", "RepositorySelectorView.xaml"));
        var controls = XDocument.Load(Path.Combine(presentation, "Styles", "Controls.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var flyout = Assert.Single(xaml.Descendants(),
            element => (string?)element.Attribute(x + "Name") == "RepositorySelectorFlyout");
        var presenterStyle = Assert.Single(flyout.Elements(),
            element => element.Name.LocalName == "Flyout.FlyoutPresenterStyle");
        var style = Assert.Single(presenterStyle.Elements());
        Assert.Equal("FlyoutPresenter", (string?)style.Attribute("TargetType"));
        var presenterSetters = style.Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .ToDictionary(element => (string)element.Attribute("Property")!,
                element => (string)element.Attribute("Value")!);
        Assert.Equal("Disabled", presenterSetters["ScrollViewer.HorizontalScrollMode"]);
        Assert.Equal("Disabled", presenterSetters["ScrollViewer.HorizontalScrollBarVisibility"]);

        var panel = Assert.Single(flyout.Elements(),
            element => element.Name.LocalName == "StackPanel");
        Assert.Equal("0", (string?)panel.Attribute("Spacing"));

        var list = Assert.Single(panel.Elements(),
            element => (string?)element.Attribute(x + "Name") == "RepositorySelectorList");
        Assert.Equal("{StaticResource DenseListItemStyle}", (string?)list.Attribute("ItemContainerStyle"));
        var row = Assert.Single(list.Descendants(), element => element.Name.LocalName == "Grid"
            && (string?)element.Attribute("ColumnDefinitions") == "20,*,160");
        var header = Assert.Single(row.Elements(), element => element.Name.LocalName == "Grid");
        Assert.Equal("{Binding HeaderVisibility}", (string?)header.Attribute("Visibility"));
        Assert.Equal("*,Auto,*", (string?)header.Attribute("ColumnDefinitions"));
        Assert.Equal("{StaticResource Height.DataRow}", (string?)row.Attribute("MinHeight"));
        Assert.Equal("20,*,160", (string?)row.Attribute("ColumnDefinitions"));
        Assert.Equal("Stretch", (string?)row.Attribute("HorizontalAlignment"));

        var pathText = Assert.Single(row.Elements(),
            element => element.Name.LocalName == "MiddleEllipsisTextBlock");
        Assert.Equal("1", (string?)pathText.Attribute("Grid.Column"));
        Assert.Equal("Stretch", (string?)pathText.Attribute("HorizontalAlignment"));
        Assert.Equal("True", (string?)pathText.Attribute("UseMiddleEllipsis"));

        var branchText = Assert.Single(row.Elements(),
            element => element.Name.LocalName == "TextBlock");
        Assert.Equal("2", (string?)branchText.Attribute("Grid.Column"));
        Assert.Equal("{Binding Branch}", (string?)branchText.Attribute("Text"));
        Assert.Equal("Right", (string?)branchText.Attribute("TextAlignment"));
        Assert.Equal("NoWrap", (string?)branchText.Attribute("TextWrapping"));
        Assert.Equal("CharacterEllipsis", (string?)branchText.Attribute("TextTrimming"));
        Assert.Equal("Disabled", (string?)list.Attribute("ScrollViewer.HorizontalScrollMode"));
        Assert.Equal("Disabled", (string?)list.Attribute("ScrollViewer.HorizontalScrollBarVisibility"));

        var buttons = panel.Elements().Where(element => element.Name.LocalName == "Button").ToArray();
        Assert.Equal(8, buttons.Length);
        Assert.All(buttons, button => Assert.Equal(
            "{StaticResource RepositorySelectorActionButtonStyle}", (string?)button.Attribute("Style")));

        var separators = panel.Elements().Where(element => element.Name.LocalName == "Border").ToArray();
        Assert.Equal(3, separators.Length);
        Assert.All(separators, separator => Assert.Equal(
            "{StaticResource Margin.MenuSeparator}", (string?)separator.Attribute("Margin")));

        var actionStyle = Assert.Single(controls.Descendants(),
            element => (string?)element.Attribute(x + "Key") == "RepositorySelectorActionButtonStyle");
        var setters = actionStyle.Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .ToDictionary(element => (string)element.Attribute("Property")!,
                element => (string)element.Attribute("Value")!);
        Assert.Equal("{StaticResource Height.DataRow}", setters["MinHeight"]);
        Assert.Equal("{StaticResource Padding.DenseRow}", setters["Padding"]);
        Assert.Equal("True", setters["UseSystemFocusVisuals"]);
    }

    [Fact]
    public void RepositorySelectorLivesInSidebarHeaderAndToolbarContainsActionsOnly()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var xaml = File.ReadAllText(Path.Combine(presentation, "MainPage.xaml"))
            + File.ReadAllText(Path.Combine(presentation, "Views", "RepositorySelectorView.xaml"))
            + File.ReadAllText(Path.Combine(presentation, "Views", "HistoryWorkspaceView.xaml"));
        var page = File.ReadAllText(Path.Combine(presentation, "Composition/WindowWorkspaceComposition.cs"));
        var switching = File.ReadAllText(Path.Combine(
            presentation,
            "Workflows", "RepositorySwitchController.cs"));

        Assert.Contains("x:Name=\"RepositoryHeader\"", xaml);
        Assert.Contains("<ColumnDefinition Width=\"270\" MinWidth=\"180\" MaxWidth=\"430\" />", xaml);
        Assert.Contains("<controls:GridSplitter Grid.Row=\"2\"", xaml);
        var explorer = File.ReadAllText(Path.Combine(presentation, "Views", "RepositoryExplorerView.xaml"));
        Assert.Contains("<TreeView x:Name=\"Tree\"", explorer);
        Assert.Contains("AutomationProperties.HelpText=\"{Binding Repository.WorkingDirectory}\"", xaml);
        Assert.Contains("DataContext=\"{Binding RepositoryOperations}\"", xaml);
        Assert.Contains("Visibility=\"{Binding HasOperationOrConflicts", xaml);
        Assert.Contains("x:Name=\"GitConsoleHost\" Grid.Row=\"3\" Grid.ColumnSpan=\"3\"", xaml);

        var header = File.ReadAllText(Path.Combine(presentation, "Views", "RepositoryHeaderView.xaml"));
        var selector = File.ReadAllText(Path.Combine(presentation, "Views", "RepositorySelectorView.xaml"));
        var toolbar = File.ReadAllText(Path.Combine(presentation, "Views", "RepositoryToolbarView.xaml"));
        var selectorController = File.ReadAllText(Path.Combine(presentation, "Views", "RepositorySelectorView.xaml.cs"));

        Assert.Contains("<views:RepositoryHeaderView", xaml);
        Assert.Contains("<views:RepositoryToolbarView", xaml);
        Assert.Contains("<views:RepositorySelectorView", header);
        Assert.Contains("RepositorySelectorButton", selector);
        Assert.DoesNotContain("RepositorySelectorButton", toolbar);
        Assert.DoesNotContain("ToolbarBranchText", toolbar);
        Assert.DoesNotContain("ToolbarProductTextStyle", toolbar);
        Assert.Contains("CommitNavigationButton", toolbar);
        Assert.Contains("Text=\"Fetch\"", toolbar);
        Assert.Contains("RepositorySync.PullButtonText", toolbar);
        Assert.Contains("ConverterParameter=Push", toolbar);
        Assert.Contains("x:Name=\"RefreshButton\"", toolbar);
        Assert.Contains("AutomationProperties.Name=\"Application menu\"", toolbar);

        Assert.DoesNotContain("RepositorySelectorPath", xaml);
        Assert.DoesNotContain("ToolbarBranchText", xaml);
        var statusXaml = File.ReadAllText(Path.Combine(presentation, "Views", "StatusBarView.xaml"));
        Assert.Contains("x:Name=\"StatusBranchText\"", statusXaml);
        Assert.DoesNotContain("ToolbarBranchText.Text", page);
        var statusCode = File.ReadAllText(Path.Combine(presentation, "Views", "StatusBarView.xaml.cs"));
        Assert.Contains("StatusBranchText.Text = branch;", statusCode);
        Assert.DoesNotContain("RepositorySelectorPath", switching);
        Assert.Contains("QuickRepositoryLimit = 8", selectorController);
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
