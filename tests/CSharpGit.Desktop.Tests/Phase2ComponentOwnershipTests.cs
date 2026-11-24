using System.Xml.Linq;

namespace CSharpGit.Desktop.Tests;

public sealed class Phase2ComponentOwnershipTests
{
    private static readonly string[] Components =
    [
        "StartScreenView",
        "RepositoryHeaderView",
        "RepositorySelectorView",
        "RepositoryToolbarView",
        "StatusBarView",
        "GitConsoleHostView",
        "RepositoryExplorerView",
        "HistoryView"
    ];

    [Fact]
    public void MigratedFeaturesAreComposedAsIndependentXamlViews()
    {
        var presentation = Path.Combine(FindRoot(), "src", "CSharpGit.Presentation");
        var page = XDocument.Load(Path.Combine(presentation, "MainPage.xaml"));
        var historyWorkspace = XDocument.Load(Path.Combine(presentation, "Views", "HistoryWorkspaceView.xaml"));
        foreach (var component in Components)
        {
            var path = Path.Combine(presentation, "Views", component + ".xaml");
            Assert.True(File.Exists(path), $"Missing owned view {component}");
            Assert.Contains(page.Descendants(),
                node => node.Name.LocalName == component ||
                    (component == "RepositorySelectorView" &&
                     XDocument.Load(Path.Combine(presentation, "Views", "RepositoryHeaderView.xaml"))
                         .Descendants().Any(child => child.Name.LocalName == component)) ||
                    (component == "StatusBarView" &&
                     XDocument.Load(Path.Combine(presentation, "Views", "GitConsoleHostView.xaml"))
                         .Descendants().Any(child => child.Name.LocalName == component)) ||
                    (component == "HistoryView" && historyWorkspace.Descendants()
                         .Any(child => child.Name.LocalName == component)));
        }

        Assert.DoesNotContain(page.Descendants(),
            node => node.Name.LocalName is "TreeView" or "ListView"
                && (string?)node.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))
                    is "RepositoryTree" or "HistoryList");
    }

    [Fact]
    public void ComponentCodeDoesNotCallMainPageOrOwnUnrelatedFeatureViews()
    {
        var views = Path.Combine(FindRoot(), "src", "CSharpGit.Presentation", "Views");
        foreach (var name in Components)
        {
            var source = File.ReadAllText(Path.Combine(views, name + ".xaml.cs"));
            Assert.DoesNotContain("MainPage.", source, StringComparison.Ordinal);
            Assert.DoesNotContain(" is MainPage", source, StringComparison.Ordinal);
            Assert.DoesNotContain("new MainPage(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("App.Current.Services", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NavigatorIsXamlFreeAndGraphContextIsWindowLocal()
    {
        var core = Read("src/CSharpGit.Presentation.Core/WorkspaceNavigator.cs");
        var graph = Read("src/CSharpGit.Presentation/Views/HistoryView.Graph.cs");
        var control = Read("src/CSharpGit.Presentation/Controls/CommitGraph/CommitGraphControl.cs");
        Assert.DoesNotContain("Microsoft.UI.Xaml", core, StringComparison.Ordinal);
        Assert.DoesNotContain("ScrollIntoView", core, StringComparison.Ordinal);
        Assert.DoesNotContain("ContentDialog", core, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(_context.Repository, repository)", core, StringComparison.Ordinal);
        Assert.Contains("ICommitGraphLayoutProvider", control, StringComparison.Ordinal);
        Assert.Contains("private readonly CommitGraphLayoutState _historyGraphLayout = new();", graph, StringComparison.Ordinal);
        Assert.Contains("LayoutChanged?.Invoke", graph, StringComparison.Ordinal);
        var historyView = Read("src/CSharpGit.Presentation/Views/HistoryView.xaml.cs");
        var navigation = Read("src/CSharpGit.Presentation/MainPage.RepositoryNavigation.cs");
        Assert.Contains("HistoryView.ScrollTo(", navigation, StringComparison.Ordinal);
        Assert.DoesNotContain("HistoryList.ScrollIntoView", navigation, StringComparison.Ordinal);
        Assert.Contains("HistoryList.ScrollIntoView", historyView, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(FindRoot(), "src", "CSharpGit.Presentation",
            "Controls", "CommitGraph", "CommitGraphPresentationContext.cs")));
    }

    [Fact]
    public void StatefulComponentSubscriptionsHaveExplicitShutdown()
    {
        var start = Read("src/CSharpGit.Presentation/Views/StartScreenView.xaml.cs");
        var selector = Read("src/CSharpGit.Presentation/Views/RepositorySelectorView.xaml.cs");
        var history = Read("src/CSharpGit.Presentation/Views/HistoryView.xaml.cs");
        var performance = Read("src/CSharpGit.Presentation/Views/HistoryView.Performance.cs");
        var graph = Read("src/CSharpGit.Presentation/Views/HistoryView.Graph.cs");
        var explorer = Read("src/CSharpGit.Presentation/Views/RepositoryExplorerView.xaml.cs");
        var console = Read("src/CSharpGit.Presentation/Views/GitConsoleHostView.xaml.cs");

        Assert.Contains("_recentRepositorySettings.Changed -= RecentRepositorySettings_Changed", start);
        Assert.Contains("_viewModel.PropertyChanged -= Repository_PropertyChanged", selector);
        Assert.Contains("_history.PropertyChanged -= History_PropertyChanged", history);
        Assert.Contains("_scrollViewer.ViewChanged -= ScrollViewer_ViewChanged", history);
        Assert.Contains("_perfSettings.Changed -= PerformanceSettings_Changed", performance);
        Assert.Contains("_history.Rows.CollectionChanged -= HistoryRows_CollectionChanged", history);
        Assert.Contains("_repository.PropertyChanged -= Repository_PropertyChanged", explorer);
        Assert.Contains("_activitySource.Changed -= ActivitySource_Changed", console);
        Assert.Contains("ConsoleView.SetActive(false)", console);
        Assert.Contains("HistoryGraphHeaderColumn.Width", graph);
        Assert.Contains("IAuthorAvatarDependencyProvider.AvatarService", history, StringComparison.Ordinal);
        Assert.DoesNotContain("MainPage", history, StringComparison.Ordinal);
    }

    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(FindRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CSharpGit.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
