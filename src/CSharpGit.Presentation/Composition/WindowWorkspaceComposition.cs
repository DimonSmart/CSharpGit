using System.Collections.ObjectModel;
using System.Diagnostics;
using CSharpGit.Presentation.Threading;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json;
using System.Windows.Input;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.Controls;
using CSharpGit.Presentation.Views;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;

namespace CSharpGit.Presentation;

internal sealed partial class WindowWorkspaceComposition
{
    private readonly MainPage _page;
    internal MainPage Page => _page;
    private DispatcherQueue DispatcherQueue => _page.DispatcherQueue;
    private XamlRoot? XamlRoot => _page.XamlRoot;

    private T Part<T>(string name) where T : FrameworkElement =>
        _page.FindName(name) as T
        ?? throw new InvalidOperationException($"Workspace XAML part '{name}' is missing or has the wrong type.");

    private Grid RootLayout => Part<Grid>("RootLayout");
    private Grid RepositoryWorkspace => Part<Grid>("RepositoryWorkspace");
    private StartScreenView StartScreenHost => Part<StartScreenView>("StartScreenHost");
    private RepositoryHeaderView RepositoryHeader => Part<RepositoryHeaderView>("RepositoryHeader");
    private RepositoryToolbarView RepositoryToolbar => Part<RepositoryToolbarView>("RepositoryToolbar");
    private RepositoryExplorerView RepositoryExplorer => Part<RepositoryExplorerView>("RepositoryExplorer");
    private HistoryWorkspaceView HistoryPane => Part<HistoryWorkspaceView>("HistoryPane");
    private WorkingTreeView WorkingTreePane => Part<WorkingTreeView>("WorkingTreePane");
    private GitConsoleHostView GitConsoleHost => Part<GitConsoleHostView>("GitConsoleHost");
    private Grid GitCommandFeedbackModalHost => Part<Grid>("GitCommandFeedbackModalHost");
    private GitCommandFeedbackView FloatingGitCommandFeedbackView => Part<GitCommandFeedbackView>("FloatingGitCommandFeedbackView");

    private readonly OpenRepositoryViewModel _viewModel;

    // Typed control references for window-local controller construction.
    private HistoryView HistoryView => HistoryPane.HistoryViewControl;
    private ScrollViewer DetailsScroller => HistoryPane.DetailsScrollerControl;
    private CommitDetailsView CommitDetailsContent => HistoryPane.CommitDetailsContentControl;
    private TreeView ChangedFilesTree => HistoryPane.ChangedFilesTreeControl;
    private Border CommitDiffHeader => HistoryPane.CommitDiffHeaderControl;
    private Grid CommitTextDiffHeader => HistoryPane.CommitTextDiffHeaderControl;
    private Button CommitOpenOriginalButton => HistoryPane.CommitOpenOriginalButtonControl;
    private Button CommitOpenChangedButton => HistoryPane.CommitOpenChangedButtonControl;
    private Button CommitExternalDiffButton => HistoryPane.CommitExternalDiffButtonControl;
    private Button CommitRevealButton => HistoryPane.CommitRevealButtonControl;
    private StackPanel CommitLargeDiffInfo => HistoryPane.CommitLargeDiffInfoControl;
    private TextBlock CommitLargeDiffMessage => HistoryPane.CommitLargeDiffMessageControl;
    private Button CommitLargeDiffButton => HistoryPane.CommitLargeDiffButtonControl;
    private InfoBar CommitBinaryInfo => HistoryPane.CommitBinaryInfoControl;
    private StackPanel CommitImageLoading => HistoryPane.CommitImageLoadingControl;
    private ImageDiffPreviewHost CommitImageDiffHost => HistoryPane.CommitImageDiffHostControl;
    private SelectableDiffViewer CompactDiffViewer => HistoryPane.CompactDiffViewerControl;


    // Working Tree controls used to wire diff and file-action controllers.
    private TreeView UnstagedChangesTree => WorkingTreePane.UnstagedChangesTreeControl;
    private TreeView StagedChangesTree => WorkingTreePane.StagedChangesTreeControl;
    private Button WorkingTreeOpenOriginalButton => WorkingTreePane.WorkingTreeOpenOriginalButtonControl;
    private Button WorkingTreeOpenChangedButton => WorkingTreePane.WorkingTreeOpenChangedButtonControl;
    private Button WorkingTreeExternalDiffButton => WorkingTreePane.WorkingTreeExternalDiffButtonControl;
    private Button WorkingTreeRevealButton => WorkingTreePane.WorkingTreeRevealButtonControl;
    private Border WorkingTreeTextDiffHeader => WorkingTreePane.WorkingTreeTextDiffHeaderControl;
    private StackPanel WorkingTreeLargeDiffInfo => WorkingTreePane.WorkingTreeLargeDiffInfoControl;
    private TextBlock WorkingTreeLargeDiffMessage => WorkingTreePane.WorkingTreeLargeDiffMessageControl;
    private Button WorkingTreeLargeDiffButton => WorkingTreePane.WorkingTreeLargeDiffButtonControl;
    private StackPanel WorkingTreeDiffLoading => WorkingTreePane.WorkingTreeDiffLoadingControl;
    private InfoBar WorkingTreeBinaryInfo => WorkingTreePane.WorkingTreeBinaryInfoControl;
    private InfoBar WorkingTreeNoChangesInfo => WorkingTreePane.WorkingTreeNoChangesInfoControl;
    private StackPanel WorkingTreeImageLoading => WorkingTreePane.WorkingTreeImageLoadingControl;
    private ImageDiffPreviewHost WorkingTreeImageDiffHost => WorkingTreePane.WorkingTreeImageDiffHostControl;
    private SelectableDiffViewer WorkingTreeDiffViewer => WorkingTreePane.WorkingTreeDiffViewerControl;
    private WorkingTreeCommitEditorView WorkingTreeCommitEditor => WorkingTreePane.WorkingTreeCommitEditorControl;

    internal OpenRepositoryViewModel WindowRepository => _viewModel;
    private readonly WorkspaceNavigator _workspaceNavigator;
    private readonly Workflows.WorkspaceNavigationController _workspaceNavigation;
    private Workflows.WorkspaceKeyboardController _keyboardShortcuts = null!;
    private readonly Workflows.RepositoryRefreshController _repositoryRefresh;
    private readonly Dialogs.PageDialogController _pageDialogs;
    private readonly Windowing.WindowUiOperations _ui;
    internal Windowing.ICloseConfirmationDialog CloseConfirmation { get; }
    private readonly Windowing.RepositoryMenuItemFactory _menu;
    private readonly Workflows.DiffPresentationController _diffPresentation;
    private readonly Workflows.WorkingTreeImageDiffWorkflow _workingTreeImageDiff;


    private WindowWorkspaceComposition(
        OpenRepositoryViewModel viewModel,
        IWorkingTreeStatusReader workingTreeStatusReader,
        WorkspaceNavigator workspaceNavigator)
    {
        _page = new MainPage();
        _page.DataContext = _viewModel = viewModel;
        _pageDialogs = new Dialogs.PageDialogController(_page, _viewModel, () => IsShuttingDown);
        CloseConfirmation = new Windowing.CommitDraftCloseConfirmationDialog(
            _viewModel, ShowPageDialogAsync, () => IsShuttingDown);
        _ui = new Windowing.WindowUiOperations(() => XamlRoot, () => IsShuttingDown, ShowPageDialogAsync);
        _menu = new Windowing.RepositoryMenuItemFactory(_viewModel, _ui);
        _diffPresentation = new Workflows.DiffPresentationController(
            _viewModel,
            new Workflows.DiffPresentationController.CommitControls(
                CommitDiffHeader, CommitTextDiffHeader, CompactDiffViewer,
                CommitLargeDiffInfo, CommitLargeDiffMessage, CommitLargeDiffButton,
                CommitImageLoading, CommitImageDiffHost, CommitBinaryInfo),
            new Workflows.DiffPresentationController.WorkingTreeControls(
                WorkingTreeTextDiffHeader, WorkingTreeDiffViewer, WorkingTreeDiffLoading,
                WorkingTreeLargeDiffInfo, WorkingTreeLargeDiffMessage, WorkingTreeLargeDiffButton,
                WorkingTreeImageLoading, WorkingTreeImageDiffHost, WorkingTreeBinaryInfo,
                WorkingTreeNoChangesInfo));
        _workingTreeImageDiff = new Workflows.WorkingTreeImageDiffWorkflow(
            _viewModel, () => _fileActions, () => _imageDiffService,
            WorkingTreeImageDiffHost, () => IsShuttingDown,
            _diffPresentation.RenderWorkingTree, _ui.ReportUiFailure);
        HistoryPane.ConfigureChanges(
            _viewModel, () => _commitImageDiff, _diffPresentation,
            UpdateRepositoryFilesViewActivity,
            (operation, description) => _ui.RunPageUiOperation(operation, description));
        _workspaceNavigator = workspaceNavigator ?? throw new ArgumentNullException(nameof(workspaceNavigator));
        _workspaceNavigation = new Workflows.WorkspaceNavigationController(
            _viewModel, _workspaceNavigator, HistoryPane, WorkingTreePane, RepositoryToolbar,
            DispatcherQueue, _ui.ExecuteCommandAsync, () => IsShuttingDown, SetRepositoryFilesStashMode,
            (operation, description) => _ui.RunPageUiOperation(operation, description));
        _repositoryRefresh = new Workflows.RepositoryRefreshController(
            _viewModel,
            new RepositoryChangeCoordinator(
                workingTreeStatusReader ?? throw new ArgumentNullException(nameof(workingTreeStatusReader)),
                new UiDispatcher(DispatcherQueue)),
            RepositoryToolbar, () => IsShuttingDown,
            (operation, description) => _ui.RunPageUiOperation(operation, description));

        _presentationRefresh = new Workflows.RepositoryPresentationRefreshController(
            _viewModel, DispatcherQueue, () => IsShuttingDown,
            RefreshPresentationCollections,
            SynchronizeRepositoryTree,
            UpdateStatusBar);


        GitConsoleHost.StatusBar.ConfigureRepository(_viewModel);
        _workspaceNavigation.Initialize();
        _presentationRefresh.Initialize();
        WorkingTreePane.Configure(
            _viewModel,
            _workingTreeImageDiff, _diffPresentation,
            () => IsShuttingDown,
            (operation, description) => _ui.RunPageUiOperation(operation, description),
            _ui.ShowErrorAsync,
            _menu.AddMenuItem, _ui.ExecuteCommandAsync, changes => _stashDialogs.ShowCreateSelectedAsync(changes),
            ShowPageDialogAsync);
        RefreshPresentationCollections();
        WorkingTreePane.InitializeWorkingTreeDiffSurface();
        _workspaceNavigation.UpdateHistoryPresentation();
    }

    private void RefreshPresentationCollections()
    {
        WorkingTreePane.RefreshChangesProjection();
        UpdateStatusBar();
    }

    private void RebuildRepositoryTree()
    {
        SynchronizeRepositoryTree();
    }

    private void UpdateStatusBar()
    {
        GitConsoleHost.StatusBar.RefreshRepositoryStatus();
        _workspaceNavigation.UpdateCommitNavigationText();
    }

    private async void RunDesktopCheckWhenRequested(object sender, RoutedEventArgs args)
    {
        var resultPath = Environment.GetEnvironmentVariable("CSHARPGIT_UI_CHECK_RESULT");
        if (string.IsNullOrWhiteSpace(resultPath)) return;

        _page.Loaded -= RunDesktopCheckWhenRequested;
        await new DesktopUiCheckRunner(new DesktopUiCheckContext(
            _page, RootLayout, RepositoryWorkspace, RepositoryExplorer, RepositoryToolbar,
            HistoryPane, WorkingTreePane, GitConsoleHost, _viewModel, _workspaceNavigation,
            _recentRepositorySettings, _settingsWindowController, _authorAvatarService,
            _gitCommandActivitySource, RefreshPresentationCollections, _ui.ExecuteCommandAsync))
            .RunAsync(resultPath);
    }

}

internal static class DispatcherQueueExtensions
{
    public static Task EnqueueAsync(this DispatcherQueue queue, Action action) =>
        UiDispatchCompletion.EnqueueAsync(callback => queue.TryEnqueue(() => callback()), action);
}
