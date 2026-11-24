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
    private RepositoryTreeNavigationCoordinator _repositoryTreeNavigation => _workspaceNavigator.Interactions;
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
        HistoryPane.DetailsTabs_SelectionChangedCallback = DetailsTabs_SelectionChanged;
        HistoryPane.DropSelectedStash_ClickCallback = DropSelectedStash_Click;
        HistoryPane.ChangesSurface_LoadedCallback = ChangesSurface_Loaded;
        HistoryPane.ChangedFilesTree_ItemInvokedCallback = ChangedFilesTree_ItemInvoked;
        HistoryPane.CommitFilesList_SelectionChangedCallback = CommitFilesList_SelectionChanged;
        HistoryPane.CommitFilesList_DoubleTappedCallback = CommitFilesList_DoubleTapped;
        HistoryPane.CommitFilesList_KeyDownCallback = CommitFilesList_KeyDown;
        HistoryPane.CommitOpenOriginal_ClickCallback = CommitOpenOriginal_Click;
        HistoryPane.CommitOpenChanged_ClickCallback = CommitOpenChanged_Click;
        HistoryPane.CommitExternalDiff_ClickCallback = CommitExternalDiff_Click;
        HistoryPane.CommitReveal_ClickCallback = CommitReveal_Click;
        HistoryPane.CommitLargeDiffButton_ClickCallback = CommitLargeDiffButton_Click;
        WorkingTreePane.UnstagedChangesTree_RightTappedCallback = UnstagedChangesTree_RightTapped;
        WorkingTreePane.StagedChangesTree_RightTappedCallback = StagedChangesTree_RightTapped;
        WorkingTreePane.WorkingTreeOpenOriginal_ClickCallback = WorkingTreeOpenOriginal_Click;
        WorkingTreePane.WorkingTreeOpenChanged_ClickCallback = WorkingTreeOpenChanged_Click;
        WorkingTreePane.WorkingTreeExternalDiff_ClickCallback = WorkingTreeExternalDiff_Click;
        WorkingTreePane.WorkingTreeReveal_ClickCallback = WorkingTreeReveal_Click;
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
        _commitSummaryViewport = new CollectionViewportController(DetailsScroller);
        _stashSummaryViewport = new CollectionViewportController(StashDetailsScroller);
        SynchronizeDetailsViewport();
        _workspaceNavigator = workspaceNavigator ?? throw new ArgumentNullException(nameof(workspaceNavigator));
        _repositoryRefresh = new Workflows.RepositoryRefreshController(
            _viewModel,
            new RepositoryChangeCoordinator(
                workingTreeStatusReader ?? throw new ArgumentNullException(nameof(workingTreeStatusReader)),
                new UiDispatcher(DispatcherQueue)),
            RepositoryToolbar, () => IsShuttingDown,
            (operation, description) => RunPageUiOperation(operation, description));

        _presentationRefresh = new Workflows.RepositoryPresentationRefreshController(
            DispatcherQueue, () => IsShuttingDown,
            InitializeTagSupportIfNeeded,
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
            ShowErrorAsync);
        RefreshPresentationCollections();
        WorkingTreePane.InitializeWorkingTreeDiffSurface();
        InitializeCommitActions();
        UpdateHistoryScopePresentation();
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
            _workspaceNavigator.ResetForRepositoryChange();
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
            UpdateLoadMoreHistoryAvailability();
            if (becameIdle && _editCommitMessage.ShouldRestorePendingEditedCommitSelection)
                RunPageUiOperation(_editCommitMessage.RestorePendingEditedCommitSelectionAsync,
                    "Restore edited commit selection");
            if (becameIdle && _repositoryFilePreview.HasPending)
                RunPageUiOperation(_repositoryFilePreview.ResumeAsync, "Resume repository file preview");
            if (becameIdle && _commitImageDiff.HasPending)
                RunPageUiOperation(_commitImageDiff.ResumeAsync, "Resume commit image preview");
            if (becameIdle && _repositoryTreeNavigation.HasPendingCheckout)
                RunPageUiOperation(ResumePendingRepositoryTreeCheckoutAsync, "Resume branch checkout");
            else if (becameIdle && _repositoryTreeNavigation.HasPendingReference)
                RunPageUiOperation(ResumePendingRepositoryTreeNavigationAsync, "Resume branch navigation");
        }
        else if (eventArgs.PropertyName == nameof(OpenRepositoryViewModel.CanChangeRepository))
        {
            UpdateLoadMoreHistoryAvailability();
        }
    }

    private void StashesViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(StashesViewModel.SelectedStash)
            or nameof(StashesViewModel.HasSelectedStash))
            UpdateStashPresentation();
    }

    private void CommitDetailsViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(CommitDetailsViewModel.ChangedFiles))
            RunPageUiOperation(RefreshCommitFilesAsync, "Refresh commit file list");
        else if (eventArgs.PropertyName is nameof(CommitDetailsViewModel.SelectedStashDetails)
                 or nameof(CommitDetailsViewModel.HasSelectedStash))
            UpdateStashPresentation();

        if (eventArgs.PropertyName == nameof(CommitDetailsViewModel.SelectedObjectCommit))
            SynchronizeDetailsViewport();
    }

    private void HistoryViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(HistoryViewModel.IsReferenceScoped)
            or nameof(HistoryViewModel.ActiveReferenceLabel))
        {
            UpdateHistoryScopePresentation();
        }
        else if (eventArgs.PropertyName is nameof(HistoryViewModel.HasMore)
                 or nameof(HistoryViewModel.IsLoading))
        {
            UpdateLoadMoreHistoryAvailability();
        }
    }

    private static void History_CommitLookupCompleted(long startedAt, int itemsExamined) =>
        HistoryRenderDiagnostics.CommitLookupCompleted(startedAt, itemsExamined);

    private void UpdateHistoryScopePresentation()
    {
        HistoryView.UpdateScopePresentation();
        UpdateLoadMoreHistoryAvailability();
        UpdateCommitNavigationText();
    }

    private void UpdateLoadMoreHistoryAvailability() =>
        HistoryView.UpdateLoadMoreAvailability();

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
        UpdateCommitNavigationText();
    }

    private Task RefreshCommitFilesAsync()
    {
        var files = _viewModel.CommitDetails.ChangedFiles;
        _commitFiles.Clear();
        ChangesTab.Header = files.Count == 0 ? "Changes" : $"Changes ({files.Count})";

        foreach (var file in files)
            _commitFiles.Add(new CommitFileRow(
                _viewModel.CommitDetails.GetChangedFileDisplayStatus(file),
                file));

        var selected = _commitFiles.FirstOrDefault(row => ReferenceEquals(row.File, _viewModel.CommitDetails.SelectedFile)) ?? _commitFiles.FirstOrDefault();
        CommitFilesList.SelectedItem = selected;
        return Task.CompletedTask;
    }

    private void ReportUiFailure(Exception exception, string operation)
    {
        if (Microsoft.UI.Xaml.Application.Current is App app)
            app.LogUiFailure(exception, operation);
        else
            Trace.TraceError($"{operation} failed: {exception}");
    }

    private void RunPageUiOperation(Func<Task> operation, string description, bool showError = false)
    {
        if (IsShuttingDown) return;
        _ = UiAsyncOperationRunner.RunAsync(
            operation,
            exception =>
            {
                ReportUiFailure(exception, description);
                if (showError && !IsShuttingDown)
                    RunPageUiOperation(
                        () => ShowErrorAsync("Could not complete action", "The requested action could not be completed."),
                        "UI error notification");
            },
            () => IsShuttingDown);
    }

    private async Task ExecuteCommandAsync(ICommand command)
    {
        if (IsShuttingDown) return;
        // ExecuteAsync deliberately propagates; this XAML boundary owns observation.
        await UiAsyncOperationRunner.RunAsync(
            async () =>
            {
                if (command is AsyncCommand asyncCommand)
                    await asyncCommand.ExecuteAsync();
                else if (command.CanExecute(null))
                    command.Execute(null);
            },
            exception => ReportUiFailure(exception, "UI command"),
            () => IsShuttingDown);
    }

    private async Task CreateBranchFromAsync(string startPoint)
    {
        var originalRepository = _viewModel.Repository;
        if (originalRepository is null || _viewModel.IsBusy || IsShuttingDown) return;
        var input = new TextBox { Header = $"New branch from {startPoint}", PlaceholderText = "branch/name" };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Create branch",
            Content = input,
            PrimaryButtonText = "Create and switch",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await ShowPageDialogAsync(dialog) != ContentDialogResult.Primary
            || string.IsNullOrWhiteSpace(input.Text)
            || IsShuttingDown
            || !ReferenceEquals(originalRepository, _viewModel.Repository)
            || _viewModel.IsBusy) return;
        var repository = originalRepository;

        if (!await _viewModel.Branches.CreateBranchAsync(
                repository,
                input.Text.Trim(),
                startPoint,
                switchToBranch: true)
            && !string.IsNullOrWhiteSpace(_viewModel.ErrorMessage))
        {
            await ShowErrorAsync("Could not create branch", _viewModel.ErrorMessage);
        }
    }

    private static Task CopyTextAsync(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        return Task.CompletedTask;
    }

    private async Task ShowErrorAsync(string title, string message)
    {
        if (IsShuttingDown || XamlRoot is null) return;
        try
        {
            // Git progress uses CR to rewrite its current line. Preserve that
            // terminal behavior at the presentation boundary, not in Git results.
            var details = new TextBlock
            {
                Text = new GitOutputDisplayBuffer().Reset(message),
                TextWrapping = TextWrapping.NoWrap,
                IsTextSelectionEnabled = true
            };
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = title,
                Content = new ScrollViewer
                {
                    Content = details,
                    MaxWidth = 720,
                    MaxHeight = 400,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                },
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Close
            };
            await ShowPageDialogAsync(dialog);
        }
        catch (Exception exception)
        {
            // Showing an error can itself fail (concurrent dialog/disposed XamlRoot).
            ReportUiFailure(exception, "Error notification");
        }
    }

    // Dispatch typed toolbar actions directly to their existing workflows.
    private void HandleRepositoryToolbarAction(RepositoryToolbarAction action)
    {
        switch (action)
        {
            case RepositoryToolbarAction.CommitNavigation:
                if (WorkingTreePane.Visibility == Visibility.Visible)
                    RunPageUiOperation(ShowAllHistoryAsync, "Back to history");
                else if (!IsShuttingDown)
                    ShowWorkingTree();
                break;
            case RepositoryToolbarAction.Fetch:
                RunPageUiOperation(() => ExecuteCommandAsync(_viewModel.RepositorySync.FetchCommand), "Fetch");
                break;
            case RepositoryToolbarAction.Pull:
                RunPageUiOperation(() => PullFromUiAsync(null), "Pull");
                break;
            case RepositoryToolbarAction.PullFastForwardOnly:
                RunPageUiOperation(() => PullFromUiAsync(PullStrategy.FastForwardOnly), "Fast-forward pull");
                break;
            case RepositoryToolbarAction.PullMerge:
                RunPageUiOperation(() => PullFromUiAsync(PullStrategy.Merge), "Merge pull");
                break;
            case RepositoryToolbarAction.PullRebase:
                RunPageUiOperation(() => PullFromUiAsync(PullStrategy.Rebase), "Rebase pull");
                break;
            case RepositoryToolbarAction.PullSettings:
                _settingsWindowController.Open(SettingsSection.General, focusPullStrategy: true);
                break;
            case RepositoryToolbarAction.PushPrimary:
                RunPageUiOperation(_repositoryPush.PushPrimaryAsync, "Push");
                break;
            case RepositoryToolbarAction.Push:
                RunPageUiOperation(_repositoryPush.PushAsync, "Push");
                break;
            case RepositoryToolbarAction.PushTo:
                RunPageUiOperation(_repositoryPush.PushToAsync, "PushTo_Click");
                break;
            case RepositoryToolbarAction.ForcePushWithLease:
                RunPageUiOperation(_repositoryPush.ForcePushWithLeaseAsync, "ForcePushWithLease_Click");
                break;
            case RepositoryToolbarAction.PushSetDefault:
                RunPageUiOperation(() => _viewModel.RepositorySync.SetDefaultPushActionAsync(PushToolbarAction.Push), "Set default Push");
                break;
            case RepositoryToolbarAction.PushToSetDefault:
                RunPageUiOperation(() => _viewModel.RepositorySync.SetDefaultPushActionAsync(PushToolbarAction.PushTo), "Set default Push to");
                break;
            case RepositoryToolbarAction.ForcePushWithLeaseSetDefault:
                RunPageUiOperation(() => _viewModel.RepositorySync.SetDefaultPushActionAsync(PushToolbarAction.ForcePushWithLease), "Set default Force push with lease");
                break;
            case RepositoryToolbarAction.RefreshIndicator:
                RunPageUiOperation(_manualRefresh.RefreshAsync, "Repository refresh indicator");
                break;
            case RepositoryToolbarAction.OpenRepository:
                RunPageUiOperation(OpenRepositoryPickerAsync, "OpenRepository_Click");
                break;
            case RepositoryToolbarAction.CloneRepository:
                RunPageUiOperation(_cloneWorkflow.ShowAsync, "CloneRepository_Click");
                break;
            case RepositoryToolbarAction.CreateRepository:
                RunPageUiOperation(_creationWorkflow.ShowAsync, "CreateRepository_Click");
                break;
            case RepositoryToolbarAction.Repositories:
            case RepositoryToolbarAction.CloseRepository:
                RunPageUiOperation(async () => { await TryCloseRepositoryAsync(); }, "CloseRepository_Click");
                break;
            case RepositoryToolbarAction.RepositorySettings:
                _settingsWindowController.Open(SettingsSection.Identity);
                break;
            case RepositoryToolbarAction.OptimizeRepository:
                RunPageUiOperation(_maintenanceController.OptimizeAsync, "Optimize repository");
                break;
            case RepositoryToolbarAction.Settings:
                _settingsWindowController.Open(SettingsSection.General);
                break;
            case RepositoryToolbarAction.About:
                RunPageUiOperation(_aboutUpdate.ShowAboutAsync, "About dialog");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }

    private async Task PullFromUiAsync(PullStrategy? strategy)
    {
        var repository = _viewModel.Repository;
        if (IsShuttingDown || repository is null || !_viewModel.RepositorySync.CanPull)
            return;

        var previousHead = _viewModel.CurrentHeadCommit;
        var history = _viewModel.History;
        var selectionIntentVersion = history.SelectionIntentVersion;
        PullExecutionResult result;
        if (strategy is { } requestedStrategy)
        {
            result = await _viewModel.RepositorySync.PullAsync(repository, requestedStrategy);
        }
        else
        {
            var previousResult = _viewModel.RepositorySync.LastPullResult;
            await ExecuteCommandAsync(_viewModel.RepositorySync.PullCommand);
            if (_viewModel.RepositorySync.LastPullResult is not { } completedResult
                || ReferenceEquals(previousResult, completedResult))
                return;
            result = completedResult;
        }

        var currentHead = _viewModel.CurrentHeadCommit;
        if (!IsShuttingDown
            && ReferenceEquals(_viewModel.Repository, repository)
            && result.Kind == PullExecutionKind.Completed
            && currentHead is not null
            && !string.Equals(previousHead, currentHead, StringComparison.Ordinal)
            && history.SelectionIntentVersion == selectionIntentVersion
            && _viewModel.Stashes.SelectedStash is null
            && HistoryPane.Visibility == Visibility.Visible)
        {
            var target = await history.SelectCommitInCurrentHistoryAsync(
                currentHead, selectionIntentVersion);
            if (!IsShuttingDown
                && ReferenceEquals(_viewModel.Repository, repository)
                && target is not null
                && ReferenceEquals(target, history.SelectedRow))
                HistoryView.ScrollTo(target);
        }

        await ShowPullResultAsync(result);
    }

    private async Task ShowPullResultAsync(PullExecutionResult result)
    {
        if (result.Kind == PullExecutionKind.Completed
            || result.Kind == PullExecutionKind.Unavailable)
            return;
        var title = result.Kind switch
        {
            PullExecutionKind.NeedsAttention => "Pull needs attention",
            PullExecutionKind.Refused => "Pull cannot fast-forward",
            _ => "Pull failed"
        };
        await ShowErrorAsync(title, result.Message);
    }

    private void ApplyHistoryFilter_Click(object sender, RoutedEventArgs e) =>
        RunPageUiOperation(RefreshVisibleHistoryAsync, "Apply history filter");
    private void RefreshHistory_Click(object sender, RoutedEventArgs e) =>
        RunPageUiOperation(RefreshVisibleHistoryAsync, "Refresh history");

    private Task RefreshVisibleHistoryAsync() =>
        _viewModel.History.RefreshAsync();

    private void HistoryFilter_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        RunPageUiOperation(RefreshVisibleHistoryAsync, "Apply history filter");
    }

    private void LoadMoreHistory_Click(object sender, RoutedEventArgs e)
    {
        HistoryRenderDiagnostics.PageLoadStarted();
        RunPageUiOperation(() => _viewModel.History.LoadMoreAsync(), "Load more history");
    }

    private void ClearReference_Click(object sender, RoutedEventArgs e) =>
        RunPageUiOperation(ShowAllHistoryAsync, "Clear history reference");

    private void UnstagedChangesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UnstagedChangesList.SelectedItem is WorkingTreeChange change) WorkingTreePane.SelectWorkingTreeChange(change, WorkingTreeDiffKind.Unstaged);
    }

    private void StagedChangesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StagedChangesList.SelectedItem is WorkingTreeChange change) WorkingTreePane.SelectWorkingTreeChange(change, WorkingTreeDiffKind.Staged);
    }

    private void CommitFilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CommitFilesList.SelectedItem is CommitFileRow row) _viewModel.CommitDetails.SelectedFile = row.File;
    }

    private void CommitFilesList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (CommitFilesList.SelectedItem is null) return;
        DetailsTabs.SelectedItem = ChangesTab;
        e.Handled = true;
    }

    private void CommitFilesList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || CommitFilesList.SelectedItem is null) return;
        DetailsTabs.SelectedItem = ChangesTab;
        e.Handled = true;
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
                    await ShowAllHistoryAsync();
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
            RunPageUiOperation(ShowAllHistoryAsync, "Back to history shortcut");
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
