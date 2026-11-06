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

        Assert.Contains("internal Task<bool> OpenRepositoryPathAsync", viewModel);
        Assert.Contains("OpenRepositoryPathFromLifecycleAsync", viewModel);
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
        Assert.Contains("if (!opened || IsShuttingDown) return false;", switching);
        Assert.Contains("_viewModel.CommitCreation.CommitMessage = string.Empty;", switching);

        Assert.Contains("await TrySwitchRepositoryAsync(item.Path);", recent);
        Assert.Contains("await _recentRepositoryFolderPicker.PickFolderAsync()", recent);
        Assert.DoesNotContain("QueuePath(item.Path)", recent);

        Assert.Contains("SwitchRepositoryCoreAsync(path, discardDraft, admission)", creation);
        Assert.Contains("TryBeginRepositoryLifecycle()", creation);
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
        Assert.Contains("OpenFolderInDesktopShellAsync", switching);
        Assert.Contains("x:Name=\"RepositorySelectorOpenTerminalButton\"", xaml);
        Assert.Contains("Click=\"RepositorySelectorOpenTerminal_Click\"", xaml);
        Assert.Contains("Open Terminal Here", xaml);
        Assert.Contains("_desktopShellService.OpenTerminalAsync(path)", switching);
        Assert.Contains("RepositorySelectorOpenTerminalButton.IsEnabled", switching);
        Assert.Contains("ShowCreateRepositoryAsync", switching);
        Assert.Contains("ShowUnavailableRepositoryAsync", switching);

        Assert.Contains("e.Key == VirtualKey.O", page);
        Assert.Contains("RunPageUiOperation(OpenRepositoryPickerAsync, \"Open repository shortcut\")", page);
    }


    [Fact]
    public void RepositorySelectorUsesSharedCompactDensity()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var xaml = XDocument.Load(Path.Combine(presentation, "MainPage.xaml"));
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
        Assert.Contains("DataContext=\"{Binding RepositoryOperations}\"", xaml);
        Assert.Contains("Visibility=\"{Binding HasOperationOrConflicts", xaml);
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
        Assert.Contains("RepositorySync.PullButtonText", toolbar);
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
