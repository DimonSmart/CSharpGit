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

public sealed partial class MainPage : Page
{
    private readonly OpenRepositoryViewModel _viewModel;

    // Temporary typed facade for history/commit-details feature handlers.
    private HistoryView HistoryView => HistoryPane.HistoryViewControl;
    private Pivot DetailsTabs => HistoryPane.DetailsTabsControl;
    private PivotItem SummaryTab => HistoryPane.SummaryTabControl;
    private ScrollViewer DetailsScroller => HistoryPane.DetailsScrollerControl;
    private CommitDetailsView CommitDetailsContent => HistoryPane.CommitDetailsContentControl;
    private ScrollViewer StashDetailsScroller => HistoryPane.StashDetailsScrollerControl;
    private PivotItem ChangesTab => HistoryPane.ChangesTabControl;
    private Border ChangedFilesHeader => HistoryPane.ChangedFilesHeaderControl;
    private TreeView ChangedFilesTree => HistoryPane.ChangedFilesTreeControl;
    private ListView CommitFilesList => HistoryPane.CommitFilesListControl;
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


    // Temporary typed facade while WorkingTree feature interactions are moved into WorkingTreeView.
    private Grid WorkingTreeHeader => WorkingTreePane.WorkingTreeHeaderControl;
    private TreeView UnstagedChangesList => UnstagedChangesTree;
    private TreeView StagedChangesList => StagedChangesTree;
    private TextBlock UnstagedHeader => WorkingTreePane.UnstagedHeaderControl;
    private TreeView UnstagedChangesTree => WorkingTreePane.UnstagedChangesTreeControl;
    private GridSplitter WorkingTreeStagingSplitter => WorkingTreePane.WorkingTreeStagingSplitterControl;
    private TextBlock StagedHeader => WorkingTreePane.StagedHeaderControl;
    private TreeView StagedChangesTree => WorkingTreePane.StagedChangesTreeControl;
    private TextBlock WorkingTreeDiffHeader => WorkingTreePane.WorkingTreeDiffHeaderControl;
    private TextBlock WorkingTreeDiffKindText => WorkingTreePane.WorkingTreeDiffKindTextControl;
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
    private readonly BulkObservableCollection<WorkingTreeChange> _unstagedChanges = [];
    private readonly BulkObservableCollection<WorkingTreeChange> _stagedChanges = [];
    private readonly ObservableCollection<CommitFileRow> _commitFiles = [];
    private bool _wasBusy;
    private readonly WorkspaceNavigator _workspaceNavigator;
    private readonly Workflows.WorkspaceNavigationController _workspaceNavigation;
    private readonly Workflows.RepositoryRefreshController _repositoryRefresh;
    private readonly Dialogs.PageDialogController _pageDialogs;
    private readonly Workflows.DiffPresentationController _diffPresentation;
    private readonly Workflows.WorkingTreeImageDiffWorkflow _workingTreeImageDiff;


    private MainPage(
        OpenRepositoryViewModel viewModel,
        IWorkingTreeStatusReader workingTreeStatusReader,
        WorkspaceNavigator workspaceNavigator)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        _pageDialogs = new Dialogs.PageDialogController(this, _viewModel, () => IsShuttingDown);
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
            _diffPresentation.RenderWorkingTree, ReportUiFailure);
        HistoryPane.ConfigureChanges(
            _viewModel, _commitFiles, () => _commitImageDiff, _diffPresentation,
            UpdateRepositoryFilesViewActivity,
            (operation, description) => RunPageUiOperation(operation, description));
        _workspaceNavigator = workspaceNavigator ?? throw new ArgumentNullException(nameof(workspaceNavigator));
        _workspaceNavigation = new Workflows.WorkspaceNavigationController(
            _viewModel, _workspaceNavigator, HistoryPane, WorkingTreePane, RepositoryToolbar,
            DispatcherQueue, ExecuteCommandAsync, () => IsShuttingDown, SetRepositoryFilesStashMode);
        _repositoryRefresh = new Workflows.RepositoryRefreshController(
            _viewModel,
            new RepositoryChangeCoordinator(
                workingTreeStatusReader ?? throw new ArgumentNullException(nameof(workingTreeStatusReader)),
                new UiDispatcher(DispatcherQueue)),
            RepositoryToolbar, () => IsShuttingDown,
            (operation, description) => RunPageUiOperation(operation, description));

        _presentationRefresh = new Workflows.RepositoryPresentationRefreshController(
            DispatcherQueue, () => IsShuttingDown,
            RefreshPresentationCollections,
            SynchronizeRepositoryTree,
            UpdateStatusBar);

        UnstagedChangesList.ItemsSource = _unstagedChanges;
        StagedChangesList.ItemsSource = _stagedChanges;
        CommitFilesList.ItemsSource = _commitFiles;

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.Stashes.PropertyChanged += StashesViewModel_PropertyChanged;
        _viewModel.CommitDetails.PropertyChanged += CommitDetailsViewModel_PropertyChanged;
        _viewModel.History.PropertyChanged += HistoryViewModel_PropertyChanged;
        _viewModel.History.CommitLookupCompleted += History_CommitLookupCompleted;
        _viewModel.WorkingTree.Changes.CollectionChanged += RepositoryPresentationChanges_CollectionChanged;
        _viewModel.Branches.LocalBranches.CollectionChanged += RepositoryPresentationLocalBranches_CollectionChanged;
        _viewModel.Branches.RemoteBranches.CollectionChanged += RepositoryPresentationRemoteBranches_CollectionChanged;
        _viewModel.RepositorySync.Remotes.CollectionChanged += RepositoryPresentationRemotes_CollectionChanged;
        _viewModel.Tags.Items.CollectionChanged += RepositoryPresentationTags_CollectionChanged;
        _viewModel.Stashes.Items.CollectionChanged += RepositoryPresentationStashes_CollectionChanged;
        Loaded += RunDesktopCheckWhenRequested;
        WorkingTreePane.Configure(
            _viewModel, _unstagedChanges, _stagedChanges,
            _workingTreeImageDiff, _diffPresentation,
            () => IsShuttingDown,
            (operation, description) => RunPageUiOperation(operation, description),
            ShowErrorAsync,
            AddMenuItem, ExecuteCommandAsync, changes => _stashDialogs.ShowCreateSelectedAsync(changes),
            ShowPageDialogAsync);
        RefreshPresentationCollections();
        WorkingTreePane.InitializeWorkingTreeDiffSurface();
        _workspaceNavigation.UpdateHistoryPresentation();
    }

    private async Task ShowInformationAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };
        await ShowPageDialogAsync(dialog);
    }

    private void RepositoryPresentationChanges_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        QueueRepositoryPresentationRefresh(workingTreeChanged: true);

    private void RepositoryPresentationLocalBranches_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        QueueRepositoryPresentationRefresh();

    private void RepositoryPresentationRemoteBranches_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        QueueRepositoryPresentationRefresh();

    private void RepositoryPresentationRemotes_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        QueueRepositoryPresentationRefresh();

    private void RepositoryPresentationTags_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        QueueRepositoryPresentationRefresh();

    private void RepositoryPresentationStashes_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        QueueRepositoryPresentationRefresh();

    private Windowing.ICloseConfirmationDialog? _closeConfirmation;

    // Temporary adapter for legacy repository-switch and restart callers.
    internal void ConfigureCloseConfirmation(Windowing.ICloseConfirmationDialog confirmation)
    {
        ArgumentNullException.ThrowIfNull(confirmation);
        if (_closeConfirmation is not null)
            throw new InvalidOperationException("Close confirmation is already configured.");
        _closeConfirmation = confirmation;
    }

    internal Task<ContentDialogResult> ShowCommitDraftDialogAsync(ContentDialog dialog) =>
        ShowPageDialogAsync(dialog);

    internal bool IsClosingForDialog => IsShuttingDown;

    public Task<bool> ConfirmCloseAsync() =>
        (_closeConfirmation ?? throw new InvalidOperationException(
            "Close confirmation was not configured."))
        .ConfirmDiscardCommitMessageAsync(CancellationToken.None);

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(OpenRepositoryViewModel.Repository))
        {
            _workspaceNavigation.ResetForRepositoryChange();
            UpdateStatusBar();
            RepositoryHeader.Refresh();
            _settingsWindowController.RepositoryChanged();
        }
        else if (eventArgs.PropertyName is nameof(OpenRepositoryViewModel.HeadDisplay)
                 or nameof(OpenRepositoryViewModel.CurrentBranchName)
                 or nameof(RepositoryOperationsViewModel.CurrentOperation))
        {
            UpdateStatusBar();
        }
        else if (eventArgs.PropertyName == nameof(OpenRepositoryViewModel.IsBusy))
        {
            var becameIdle = _wasBusy && !_viewModel.IsBusy;
            _wasBusy = _viewModel.IsBusy;
            UpdateStatusBar();
            _workspaceNavigation.UpdateLoadMoreAvailability();
            if (becameIdle && _editCommitMessage.ShouldRestorePendingEditedCommitSelection)
                RunPageUiOperation(_editCommitMessage.RestorePendingEditedCommitSelectionAsync,
                    "Restore edited commit selection");
            if (becameIdle && _repositoryFilePreview.HasPending)
                RunPageUiOperation(_repositoryFilePreview.ResumeAsync, "Resume repository file preview");
            if (becameIdle && _commitImageDiff.HasPending)
                RunPageUiOperation(_commitImageDiff.ResumeAsync, "Resume commit image preview");
            if (becameIdle)
                RunPageUiOperation(_workspaceNavigation.ResumePendingAsync, "Resume repository tree navigation");
        }
        else if (eventArgs.PropertyName == nameof(OpenRepositoryViewModel.CanChangeRepository))
        {
            _workspaceNavigation.UpdateLoadMoreAvailability();
        }
    }

    private void StashesViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(StashesViewModel.SelectedStash)
            or nameof(StashesViewModel.HasSelectedStash))
            _workspaceNavigation.UpdateStashPresentation();
    }

    private void CommitDetailsViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(CommitDetailsViewModel.ChangedFiles))
            RunPageUiOperation(RefreshCommitFilesAsync, "Refresh commit file list");
        else if (eventArgs.PropertyName is nameof(CommitDetailsViewModel.SelectedStashDetails)
                 or nameof(CommitDetailsViewModel.HasSelectedStash))
            _workspaceNavigation.UpdateStashPresentation();

        if (eventArgs.PropertyName == nameof(CommitDetailsViewModel.SelectedObjectCommit))
            HistoryPane.SynchronizeDetailsViewport();
    }

    private void HistoryViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(HistoryViewModel.IsReferenceScoped)
            or nameof(HistoryViewModel.ActiveReferenceLabel))
        {
            _workspaceNavigation.UpdateHistoryPresentation();
        }
        else if (eventArgs.PropertyName is nameof(HistoryViewModel.HasMore)
                 or nameof(HistoryViewModel.IsLoading))
        {
            _workspaceNavigation.UpdateLoadMoreAvailability();
        }
    }

    private static void History_CommitLookupCompleted(long startedAt, int itemsExamined) =>
        HistoryRenderDiagnostics.CommitLookupCompleted(startedAt, itemsExamined);

    private void RefreshPresentationCollections()
    {
        var unstaged = new List<WorkingTreeChange>();
        var staged = new List<WorkingTreeChange>();
        foreach (var change in _viewModel.WorkingTree.Changes)
        {
            if (change.IsUnstaged) unstaged.Add(change);
            if (change.IsStaged) staged.Add(change);
        }

        WorkingTreePane.SetSelectionSync(true);
        try
        {
            _unstagedChanges.ReplaceAll(unstaged);
            _stagedChanges.ReplaceAll(staged);
        }
        finally
        {
            WorkingTreePane.SetSelectionSync(false);
        }

        UnstagedHeader.Text = $"Unstaged changes ({_unstagedChanges.Count})";
        StagedHeader.Text = $"Staged changes ({_stagedChanges.Count})";
        UpdateStatusBar();
    }

    private void RebuildRepositoryTree()
    {
        SynchronizeRepositoryTree();
    }

    private void UpdateStatusBar()
    {
        var current = _viewModel.Branches.LocalBranches.FirstOrDefault(branch => branch.IsCurrent);
        var branch = current?.Name
                     ?? _viewModel.CurrentBranchName
                     ?? (_viewModel.Repository is null ? string.Empty : "detached HEAD");
        GitConsoleHost.StatusBar.SetStatus(
            branch,
            current is null ? string.Empty : $"↑{current.Ahead} ↓{current.Behind}",
            _viewModel.WorkingTree.Changes.Count,
            _viewModel.IsBusy ? "Working…" :
                _viewModel.RepositoryOperations.CurrentOperation == RepositoryOperation.None
                    ? "Ready" : _viewModel.RepositoryOperations.CurrentOperation.ToString(),
            _viewModel.IsBusy);
        _workspaceNavigation.UpdateCommitNavigationText();
    }

    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (IsShuttingDown || GitCommandFeedbackModalHost.Visibility == Visibility.Visible) return;
        var control = IsControlDown();
        if (control && e.Key == VirtualKey.O)
        {
            e.Handled = true;
            RunPageUiOperation(OpenRepositoryPickerAsync, "Open repository shortcut");
        }
        else if (control && e.Key == VirtualKey.F)
        {
            e.Handled = true;
            RunPageUiOperation(async () =>
            {
                if (WorkingTreePane.Visibility == Visibility.Visible)
                    await _workspaceNavigation.ShowAllHistoryAsync();
                if (!IsShuttingDown)
                    HistoryView.FocusFilter();
            }, "History search shortcut");
        }
        else if (e.Key == VirtualKey.F5)
        {
            e.Handled = true;
            RunPageUiOperation(_manualRefresh.RefreshAsync, "Refresh shortcut");
        }
        else if (control && e.Key == VirtualKey.Enter && WorkingTreePane.Visibility == Visibility.Visible)
        {
            e.Handled = true;
            RunPageUiOperation(
                () => ExecuteCommandAsync(_viewModel.CommitCreation.CommitCommand),
                "Commit shortcut");
        }
        else if (e.Key == VirtualKey.Escape && WorkingTreePane.Visibility == Visibility.Visible)
        {
            e.Handled = true;
            RunPageUiOperation(_workspaceNavigation.ShowAllHistoryAsync, "Back to history shortcut");
        }
    }

    private static bool IsControlDown() =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;

    private async void RunDesktopCheckWhenRequested(object sender, RoutedEventArgs args)
    {
        var resultPath = Environment.GetEnvironmentVariable("CSHARPGIT_UI_CHECK_RESULT");
        if (string.IsNullOrWhiteSpace(resultPath)) return;

        Loaded -= RunDesktopCheckWhenRequested;
        await new DesktopUiCheckRunner(this).RunAsync(resultPath);
    }

}

internal static class DispatcherQueueExtensions
{
    public static Task EnqueueAsync(this DispatcherQueue queue, Action action) =>
        UiDispatchCompletion.EnqueueAsync(callback => queue.TryEnqueue(() => callback()), action);
}
