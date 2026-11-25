using System.Windows.Input;
using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.Controls;
using CSharpGit.Presentation.ViewModels;
using CSharpGit.Presentation.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

// A diagnostic-only seam: the runner does not own the window, its workflows or its lifetime.
internal sealed record DesktopUiCheckContext(
    FrameworkElement Root,
    Grid RootLayout,
    Grid RepositoryWorkspace,
    RepositoryExplorerView RepositoryExplorer,
    RepositoryToolbarView RepositoryToolbar,
    HistoryWorkspaceView HistoryPane,
    WorkingTreeView WorkingTreePane,
    GitConsoleHostView GitConsoleHost,
    OpenRepositoryViewModel Repository,
    Workflows.WorkspaceNavigationController Navigation,
    IAppSettingsService Settings,
    SettingsWindowController SettingsWindow,
    IAuthorAvatarService Avatars,
    IGitCommandActivitySource Activity,
    Action RefreshPresentationCollections,
    Func<ICommand, Task> ExecuteCommandAsync,
    Func<Task> DismissActiveDialogAsync)
{
    public ElementTheme RequestedTheme => Root.RequestedTheme;
    public XamlRoot? XamlRoot => Root.XamlRoot;
    public HistoryView HistoryView => HistoryPane.HistoryViewControl;
    public TreeView RepositoryTree => RepositoryExplorer.TreeControl;
    public ScrollViewer DetailsScroller => HistoryPane.DetailsScrollerControl;
    public CommitDetailsView CommitDetailsContent => HistoryPane.CommitDetailsContentControl;
    public Pivot DetailsTabs => HistoryPane.DetailsTabsControl;
    public Border ChangedFilesHeader => HistoryPane.ChangedFilesHeaderControl;
    public TreeView ChangedFilesTree => HistoryPane.ChangedFilesTreeControl;
    public SelectableDiffViewer CompactDiffViewer => HistoryPane.CompactDiffViewerControl;
    public TreeView StagedChangesTree => WorkingTreePane.StagedChangesTreeControl;
    public TreeView UnstagedChangesTree => WorkingTreePane.UnstagedChangesTreeControl;
}
