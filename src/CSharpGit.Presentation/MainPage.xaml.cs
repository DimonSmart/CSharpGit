using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json;
using System.Windows.Input;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.Controls;
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
    private readonly IReferenceService _referenceService;
    private readonly IRepositorySyncService _repositorySyncService;
    private readonly ICommitActionService _commitActionService;
    private readonly TagsViewModel _tagsViewModel;
    private readonly ObservableCollection<RepositoryTreeNode> _repositoryTreeRoots = [];
    private readonly BulkObservableCollection<WorkingTreeChange> _unstagedChanges = [];
    private readonly BulkObservableCollection<WorkingTreeChange> _stagedChanges = [];
    private readonly ObservableCollection<CommitFileRow> _commitFiles = [];
    private bool _wasBusy;
    private readonly Storyboard _busyPulseStoryboard = new();
    private readonly SolidColorBrush _busyStatusBackgroundBrush = new(Windows.UI.Color.FromArgb(28, 34, 197, 94));
    private bool _busyPulseRunning;

    internal event Action<string>? WindowTitleChanged;

    internal string CurrentWindowTitle => MainWindowTitleFormatter.Format(
        _viewModel.Repository,
        _viewModel.CurrentBranchName,
        _viewModel.CurrentHeadCommit,
        _viewModel.IsDetachedHead);

    private MainPage(
        OpenRepositoryViewModel viewModel,
        IReferenceService referenceService,
        IRepositorySyncService repositorySyncService,
        ICommitActionService commitActionService,
        TagsViewModel tagsViewModel,
        IWorkingTreeStatusReader workingTreeStatusReader)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        _referenceService = referenceService;
        _repositorySyncService = repositorySyncService ?? throw new ArgumentNullException(nameof(repositorySyncService));
        _commitActionService = commitActionService ?? throw new ArgumentNullException(nameof(commitActionService));
        _tagsViewModel = tagsViewModel ?? throw new ArgumentNullException(nameof(tagsViewModel));
        _tagsViewModel.Attach(_viewModel);
        _workingTreeStatusReader = workingTreeStatusReader ?? throw new ArgumentNullException(nameof(workingTreeStatusReader));
        InitializeBusyStatusPresentation();

        RepositoryTree.ItemsSource = _repositoryTreeRoots;
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
        _viewModel.Remotes.CollectionChanged += RepositoryPresentationRemotes_CollectionChanged;
        _viewModel.Tags.CollectionChanged += RepositoryPresentationTags_CollectionChanged;
        _viewModel.Stashes.Items.CollectionChanged += RepositoryPresentationStashes_CollectionChanged;
        Loaded += RunDesktopCheckWhenRequested;
        RefreshPresentationCollections();
        InitializeWorkingTreeDiffSurface();
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

    public async Task<bool> ConfirmCloseAsync()
    {
        if (!_viewModel.HasUnappliedCommitMessage) return true;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Discard commit message?",
            Content = "The commit message has not been applied. Close the window and discard it?",
            PrimaryButtonText = "Discard and close",
            CloseButtonText = "Keep editing",
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(OpenRepositoryViewModel.Repository)
            or nameof(OpenRepositoryViewModel.CurrentBranchName)
            or nameof(OpenRepositoryViewModel.CurrentHeadCommit)
            or nameof(OpenRepositoryViewModel.IsDetachedHead))
            PublishWindowTitle();

        if (eventArgs.PropertyName == nameof(OpenRepositoryViewModel.Repository))
        {
            UpdateStatusBar();
            UpdateRepositorySelectorPresentation();
            _settingsWindowController.RepositoryChanged();
        }
        else if (eventArgs.PropertyName is nameof(OpenRepositoryViewModel.HeadDisplay)
                 or nameof(OpenRepositoryViewModel.CurrentBranchName)
                 or nameof(OpenRepositoryViewModel.CurrentOperation))
        {
            UpdateStatusBar();
        }
        else if (eventArgs.PropertyName == nameof(OpenRepositoryViewModel.IsBusy))
        {
            var becameIdle = _wasBusy && !_viewModel.IsBusy;
            _wasBusy = _viewModel.IsBusy;
            UpdateStatusBar();
            if (becameIdle && ShouldRestorePendingEditedCommitSelection)
                _ = RestorePendingEditedCommitSelectionAsync();
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
            _ = RefreshCommitFilesAsync();
        else if (eventArgs.PropertyName is nameof(CommitDetailsViewModel.SelectedStashDetails)
                 or nameof(CommitDetailsViewModel.HasSelectedStash))
            UpdateStashPresentation();
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
            LoadMoreHistoryButton.IsEnabled =
                _viewModel.History.HasMore && !_viewModel.History.IsLoading;
        }
    }

    private static void History_CommitLookupCompleted(long startedAt, int itemsExamined) =>
        HistoryRenderDiagnostics.CommitLookupCompleted(startedAt, itemsExamined);

    private void UpdateHistoryScopePresentation()
    {
        var history = _viewModel.History;
        ShowReflogToggle.IsEnabled = !history.IsReferenceScoped;
        ScopeCombo.Visibility = history.IsReferenceScoped ? Visibility.Collapsed : Visibility.Visible;
        ReferenceScopePanel.Visibility = history.IsReferenceScoped ? Visibility.Visible : Visibility.Collapsed;
        ActiveReferenceText.Text = history.ActiveReferenceLabel ?? string.Empty;
        LoadMoreHistoryButton.IsEnabled = history.HasMore && !history.IsLoading;
        UpdateCommitNavigationText();
    }

    private void PublishWindowTitle() => WindowTitleChanged?.Invoke(CurrentWindowTitle);

    private void RefreshPresentationCollections()
    {
        var unstaged = new List<WorkingTreeChange>();
        var staged = new List<WorkingTreeChange>();
        foreach (var change in _viewModel.WorkingTree.Changes)
        {
            if (change.IsUnstaged) unstaged.Add(change);
            if (change.IsStaged) staged.Add(change);
        }

        _workingTreeSelectionSync = true;
        try
        {
            _unstagedChanges.ReplaceAll(unstaged);
            _stagedChanges.ReplaceAll(staged);
        }
        finally
        {
            _workingTreeSelectionSync = false;
            ScheduleWorkingTreeTreeRefreshIfNeeded();
        }

        UnstagedHeader.Text = $"Unstaged changes ({_unstagedChanges.Count})";
        StagedHeader.Text = $"Staged changes ({_stagedChanges.Count})";
        UpdateStatusBar();
    }

    private void RebuildRepositoryTree()
    {
        SynchronizeRepositoryTree();
    }

    private void InitializeBusyStatusPresentation()
    {
        var opacityAnimation = new DoubleAnimationUsingKeyFrames
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        opacityAnimation.KeyFrames.Add(new LinearDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero),
            Value = 1
        });
        opacityAnimation.KeyFrames.Add(new LinearDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(900)),
            Value = 0.45
        });
        opacityAnimation.KeyFrames.Add(new LinearDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1800)),
            Value = 1
        });
        Storyboard.SetTarget(opacityAnimation, BusyPulseDot);
        Storyboard.SetTargetProperty(opacityAnimation, "Opacity");
        _busyPulseStoryboard.Children.Add(opacityAnimation);
    }

    private void UpdateBusyStatusPresentation()
    {
        var isBusy = _viewModel.IsBusy;
        StatusOperationIdleIcon.Visibility = isBusy ? Visibility.Collapsed : Visibility.Visible;
        BusyPulseDot.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        StatusOperationContainer.Background = isBusy ? _busyStatusBackgroundBrush : null;

        if (_busyPulseRunning == isBusy) return;

        _busyPulseRunning = isBusy;
        if (isBusy)
        {
            BusyPulseDot.Opacity = 1;
            _busyPulseStoryboard.Begin();
        }
        else
        {
            _busyPulseStoryboard.Stop();
            BusyPulseDot.Opacity = 1;
        }
    }

    private void UpdateStatusBar()
    {
        var current = _viewModel.Branches.LocalBranches.FirstOrDefault(branch => branch.IsCurrent);
        var branch = current?.Name
                     ?? _viewModel.CurrentBranchName
                     ?? (_viewModel.Repository is null ? string.Empty : "detached HEAD");
        StatusBranchText.Text = branch;
        StatusTrackingText.Text = current is null ? string.Empty : $"↑{current.Ahead} ↓{current.Behind}";
        StatusChangesText.Text = $"{_viewModel.WorkingTree.Changes.Count} changes";
        StatusOperationText.Text = _viewModel.IsBusy
            ? "Working…"
            : _viewModel.CurrentOperation == RepositoryOperation.None ? "Ready" : _viewModel.CurrentOperation.ToString();
        UpdateBusyStatusPresentation();
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

    private async Task NavigateToReferenceAsync(
        string commitHash,
        bool preserveSelectedStash = false)
    {
        if (!preserveSelectedStash)
            _viewModel.Stashes.ClearSelection();

        HistoryPane.Visibility = Visibility.Visible;
        WorkingTreePane.Visibility = Visibility.Collapsed;

        var target = await _viewModel.History.NavigateToCommitAsync(commitHash);
        UpdateHistoryScopePresentation();
        if (target is null) return;

        HistoryList.SelectedItem = target;
        HistoryList.ScrollIntoView(target, ScrollIntoViewAlignment.Leading);
    }

    private async Task ShowReferenceHistoryAsync(string reference, string label)
    {
        HistoryPane.Visibility = Visibility.Visible;
        WorkingTreePane.Visibility = Visibility.Collapsed;
        await _viewModel.History.ShowReferenceAsync(reference, label);
        UpdateHistoryScopePresentation();
    }

    private async Task ShowAllHistoryAsync()
    {
        _viewModel.Stashes.ClearSelection();
        HistoryPane.Visibility = Visibility.Visible;
        WorkingTreePane.Visibility = Visibility.Collapsed;
        await _viewModel.History.ShowAllAsync();
        UpdateHistoryScopePresentation();

        if (_viewModel.History.SelectedRow is { } selected)
        {
            HistoryList.SelectedItem = selected;
            HistoryList.ScrollIntoView(selected, ScrollIntoViewAlignment.Leading);
        }
    }

    private void ShowWorkingTree()
    {
        HistoryPane.Visibility = Visibility.Collapsed;
        WorkingTreePane.Visibility = Visibility.Visible;
        UpdateCommitNavigationText();
        if (_unstagedChanges.FirstOrDefault() is { } unstaged)
        {
            UnstagedChangesList.SelectedItem = unstaged;
            SelectWorkingTreeChange(unstaged, WorkingTreeDiffKind.Unstaged);
        }
        else if (_stagedChanges.FirstOrDefault() is { } staged)
        {
            StagedChangesList.SelectedItem = staged;
            SelectWorkingTreeChange(staged, WorkingTreeDiffKind.Staged);
        }
    }

    private void UpdateCommitNavigationText() =>
        CommitNavigationText.Text = WorkingTreePane.Visibility == Visibility.Visible
            ? "Back to history"
            : $"Commit ({_viewModel.WorkingTree.Changes.Count})";

    private async void RepositoryTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (ResolveNode(args.InvokedItem) is { } node) await SelectRepositoryNodeAsync(node);
    }

    private async Task SelectRepositoryNodeAsync(RepositoryTreeNode node)
    {
        switch (node.Kind)
        {
            case RepositoryTreeNodeKind.LocalBranch when node.Value is GitBranch local:
                _viewModel.Branches.SelectedLocalBranch = local;
                await NavigateToReferenceAsync(local.Commit);
                break;
            case RepositoryTreeNodeKind.RemoteBranch when node.Value is GitBranch remoteBranch:
                _viewModel.Branches.SelectedRemoteBranch = remoteBranch;
                await NavigateToReferenceAsync(remoteBranch.Commit);
                break;
            case RepositoryTreeNodeKind.Tag when node.Value is GitTag tag:
                _viewModel.SelectedTag = tag;
                await NavigateToReferenceAsync(tag.TargetCommit);
                break;
            case RepositoryTreeNodeKind.Stash when node.Value is GitStash stash:
                await _viewModel.Stashes.SelectStashAsync(stash);
                await NavigateToReferenceAsync(stash.Commit, preserveSelectedStash: true);
                UpdateStashPresentation();
                break;
            case RepositoryTreeNodeKind.Group:
                await ShowAllHistoryAsync();
                break;
        }
    }

    private async void RepositoryTree_DoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        var node = ResolveNode((args.OriginalSource as FrameworkElement)?.DataContext);
        if (node?.Kind != RepositoryTreeNodeKind.LocalBranch || node.Value is not GitBranch branch) return;
        _viewModel.Branches.SelectedLocalBranch = branch;
        await ExecuteCommandAsync(_viewModel.Branches.SwitchBranchCommand);
        args.Handled = true;
    }

    private void RepositoryTree_RightTapped(object sender, RightTappedRoutedEventArgs args)
    {
        var source = args.OriginalSource as FrameworkElement;
        var node = ResolveNode(source?.DataContext);
        if (source is null || node is null) return;

        var flyout = new MenuFlyout();
        switch (node.Kind)
        {
            case RepositoryTreeNodeKind.LocalBranch when node.Value is GitBranch branch:
                AddMenuItem(flyout, "Switch / Checkout", !branch.IsCurrent && !_viewModel.IsBusy, async () =>
                {
                    _viewModel.Branches.SelectedLocalBranch = branch;
                    await ExecuteCommandAsync(_viewModel.Branches.SwitchBranchCommand);
                });
                AddMenuItem(flyout, "Create branch from here…", !_viewModel.IsBusy, () => CreateBranchFromAsync(branch.Name));
                AddMenuItem(flyout, "Merge into current branch", !branch.IsCurrent && !_viewModel.IsBusy, async () =>
                {
                    _viewModel.SelectedMergeBranch = branch;
                    await ExecuteCommandAsync(_viewModel.MergeCommand);
                });
                AddMenuItem(
                    flyout,
                    "Delete",
                    !branch.IsCurrent && !_viewModel.IsBusy,
                    () => ConfirmDeleteLocalBranchAsync(branch));
                flyout.Items.Add(new MenuFlyoutSeparator());
                AddMenuItem(flyout, "Show branch history only", !_viewModel.IsBusy,
                    () => ShowReferenceHistoryAsync(branch.Name, $"Branch: {branch.Name}"));
                AddMenuItem(flyout, "Copy branch name", true, () => CopyTextAsync(branch.Name));
                break;

            case RepositoryTreeNodeKind.Group when node.Name == "Remotes":
                AddMenuItem(flyout, "Fetch all", !_viewModel.IsBusy, async () => await ExecuteCommandAsync(_viewModel.FetchAllCommand));
                break;

            case RepositoryTreeNodeKind.Remote when node.Value is GitRemote remote:
                AddMenuItem(flyout, "Fetch", !_viewModel.IsBusy, async () =>
                {
                    _viewModel.SelectedRemote = remote;
                    await ExecuteCommandAsync(_viewModel.FetchCommand);
                });
                break;

            case RepositoryTreeNodeKind.RemoteBranch when node.Value is GitBranch remoteBranch:
                AddMenuItem(flyout, "Checkout as tracking branch", !_viewModel.IsBusy, async () =>
                {
                    _viewModel.Branches.SelectedRemoteBranch = remoteBranch;
                    var slash = remoteBranch.Name.IndexOf('/');
                    _viewModel.Branches.NewBranchName = slash >= 0 ? remoteBranch.Name[(slash + 1)..] : remoteBranch.Name;
                    await ExecuteCommandAsync(_viewModel.Branches.CheckoutRemoteCommand);
                });
                AddMenuItem(flyout, "Show branch history only", !_viewModel.IsBusy,
                    () => ShowReferenceHistoryAsync(remoteBranch.Name, $"Remote: {remoteBranch.Name}"));
                AddMenuItem(flyout, "Copy branch name", true, () => CopyTextAsync(remoteBranch.Name));
                break;

            case RepositoryTreeNodeKind.Tag when node.Value is GitTag tag:
                AddMenuItem(flyout, "Checkout detached", !_viewModel.IsBusy, async () =>
                {
                    _viewModel.SelectedTag = tag;
                    await ExecuteCommandAsync(_viewModel.CheckoutTagCommand);
                });
                AddMenuItem(flyout, "Copy tag name", true, () => CopyTextAsync(tag.Name));
                break;

            case RepositoryTreeNodeKind.Group
                when string.Equals(node.Key, RepositoryTreeDescriptorBuilder.StashesRootKey, StringComparison.Ordinal):
                AddMenuItem(flyout, "Stash…", _viewModel.Stashes.CanCreateStash, ShowCreateStashDialogAsync);
                break;

            case RepositoryTreeNodeKind.Stash when node.Value is GitStash stash:
                AddMenuItem(flyout, "Apply", _viewModel.Stashes.CanMutateStash(stash), async () =>
                {
                    await _viewModel.Stashes.SelectStashAsync(stash);
                    await ExecuteCommandAsync(_viewModel.Stashes.ApplyCommand);
                });
                AddMenuItem(flyout, "Pop", _viewModel.Stashes.CanMutateStash(stash), async () =>
                {
                    await _viewModel.Stashes.SelectStashAsync(stash);
                    await ExecuteCommandAsync(_viewModel.Stashes.PopCommand);
                });
                flyout.Items.Add(new MenuFlyoutSeparator());
                AddMenuItem(flyout, "Drop…", _viewModel.Stashes.CanMutateStash(stash), async () =>
                {
                    await _viewModel.Stashes.SelectStashAsync(stash);
                    await ConfirmDropStashAsync(stash);
                });
                break;
        }

        if (flyout.Items.Count == 0) return;
        flyout.ShowAt(source, args.GetPosition(source));
        args.Handled = true;
    }

    private void UpdateStashPresentation()
    {
        if (_repositoryFilesTab is not null)
            _repositoryFilesTab.Header = _viewModel.Stashes.HasSelectedStash
                ? "Tracked files"
                : "Files";
    }

    private static RepositoryTreeNode? ResolveNode(object? value) => value switch
    {
        RepositoryTreeNode node => node,
        TreeViewNode { Content: RepositoryTreeNode node } => node,
        TreeViewItem { DataContext: RepositoryTreeNode node } => node,
        FrameworkElement { DataContext: RepositoryTreeNode node } => node,
        _ => null
    };

    private static void AddMenuItem(MenuFlyout flyout, string text, bool enabled, Func<Task> action)
    {
        var item = new MenuFlyoutItem { Text = text, IsEnabled = enabled };
        item.Click += async (_, _) => await action();
        flyout.Items.Add(item);
    }

    private async Task ExecuteCommandAsync(ICommand command)
    {
        if (command is AsyncCommand asyncCommand) await asyncCommand.ExecuteAsync();
        else if (command.CanExecute(null)) command.Execute(null);
    }

    private async Task CreateBranchFromAsync(string startPoint)
    {
        if (_viewModel.Repository is null || _viewModel.IsBusy) return;
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
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(input.Text)) return;
        var repository = _viewModel.Repository;
        if (repository is null) return;

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
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };
        await dialog.ShowAsync();
    }

    private async void Fetch_Click(object sender, RoutedEventArgs e) => await ExecuteCommandAsync(_viewModel.FetchCommand);
    private async void Pull_Click(object sender, RoutedEventArgs e) => await ExecuteCommandAsync(_viewModel.PullCommand);
    private async void Push_Click(object sender, RoutedEventArgs e) => await PushFromUiAsync();

    private async void RefreshAll_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshAsyncForDesktopCheck();
        RefreshPresentationCollections();
    }



    private async void ApplyHistoryFilter_Click(object sender, RoutedEventArgs e) => await RefreshVisibleHistoryAsync();
    private async void RefreshHistory_Click(object sender, RoutedEventArgs e) => await RefreshVisibleHistoryAsync();

    private Task RefreshVisibleHistoryAsync() =>
        _viewModel.History.RefreshAsync();

    private async void HistoryFilter_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        await RefreshVisibleHistoryAsync();
    }

    private async void LoadMoreHistory_Click(object sender, RoutedEventArgs e)
    {
        HistoryRenderDiagnostics.PageLoadStarted();
        await _viewModel.History.LoadMoreAsync();
    }

    private async void ClearReference_Click(object sender, RoutedEventArgs e) =>
        await ShowAllHistoryAsync();

    private async void CommitNavigation_Click(object sender, RoutedEventArgs e)
    {
        if (WorkingTreePane.Visibility == Visibility.Visible)
            await ShowAllHistoryAsync();
        else
            ShowWorkingTree();
    }

    private void UnstagedChangesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UnstagedChangesList.SelectedItem is WorkingTreeChange change) SelectWorkingTreeChange(change, WorkingTreeDiffKind.Unstaged);
    }

    private void StagedChangesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StagedChangesList.SelectedItem is WorkingTreeChange change) SelectWorkingTreeChange(change, WorkingTreeDiffKind.Staged);
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

    private async void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var control = IsControlDown();
        if (control && e.Key == VirtualKey.O)
        {
            await OpenRepositoryPickerAsync();
            e.Handled = true;
        }
        else if (control && e.Key == VirtualKey.F)
        {
            if (WorkingTreePane.Visibility == Visibility.Visible)
                await ShowAllHistoryAsync();
            HistoryFilter.Focus(FocusState.Programmatic);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.F5)
        {
            await _viewModel.RefreshAsyncForDesktopCheck();
            e.Handled = true;
        }
        else if (control && e.Key == VirtualKey.Enter && WorkingTreePane.Visibility == Visibility.Visible)
        {
            await ExecuteCommandAsync(_viewModel.CommitCommand);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape && WorkingTreePane.Visibility == Visibility.Visible)
        {
            await ShowAllHistoryAsync();
            e.Handled = true;
        }
    }

    private static bool IsControlDown() =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;

    private async void RunDesktopCheckWhenRequested(object sender, RoutedEventArgs args)
    {
        var resultPath = Environment.GetEnvironmentVariable("CSHARPGIT_UI_CHECK_RESULT");
        if (string.IsNullOrWhiteSpace(resultPath)) return;

        Loaded -= RunDesktopCheckWhenRequested;
        var failures = new List<string>();
        try
        {
            var busyObserved = false;
            _viewModel.PropertyChanged += (_, eventArgs) =>
            {
                if (eventArgs.PropertyName == nameof(OpenRepositoryViewModel.IsBusy) && _viewModel.IsBusy)
                    busyObserved = true;
            };

            await _viewModel.OpenRepositoryAsyncForDesktopCheck();
            RefreshPresentationCollections();
            Check(_viewModel.Repository is not null, $"repository did not open: {_viewModel.ErrorMessage}", failures);
            for (var attempt = 0; attempt < 3 && _viewModel.Repository is not null &&
                 (_viewModel.WorkingTree.Changes.Count == 0 || _viewModel.History.Rows.Count == 0); attempt++)
            {
                await Task.Delay(100 * (attempt + 1));
                await _viewModel.RefreshAsyncForDesktopCheck();
                RefreshPresentationCollections();
            }
            Check(_viewModel.WorkingTree.Changes.Count > 0, $"working tree changes were not loaded: {_viewModel.ErrorMessage}", failures);
            Check(_viewModel.History.Rows.Count > 0, $"history was not loaded: {_viewModel.ErrorMessage}", failures);
            await WaitUntilAsync(
                () => RepositoryWorkspace.ActualWidth > 0 && RepositoryWorkspace.ActualHeight > 0 && HistoryList.ActualWidth > 0 && HistoryList.ActualHeight > 0,
                TimeSpan.FromSeconds(10));
            Check(_viewModel.Repository is not null, "repository did not open", failures);
            Check(_viewModel.Repository?.IsWorktree == (Environment.GetEnvironmentVariable("CSHARPGIT_UI_CHECK_WORKTREE") == "1"), "repository kind is incorrect", failures);
            Check(_viewModel.History.Scopes.All(scope => scope.Label is "All references" or "Current branch"), "English history scopes are missing", failures);

            if (Environment.GetEnvironmentVariable("CSHARPGIT_GRAPH_VIEWPORT_CHECK") == "1")
            {
                await RunCommitGraphViewportLifecycleCheckAsync(failures);
                await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(
                    new DesktopCheckResult(failures.Count == 0, failures, false)));
                return;
            }

            var settings = _recentRepositorySettings;
            await settings.SetThemeModeAsync(ApplicationThemeMode.System);
            await WaitUntilAsync(() => RequestedTheme == ElementTheme.Default, TimeSpan.FromSeconds(5));
            Check(RequestedTheme == ElementTheme.Default, "System theme did not clear the root override", failures);

            await settings.SetThemeModeAsync(ApplicationThemeMode.Light);
            await WaitUntilAsync(() => RequestedTheme == ElementTheme.Light, TimeSpan.FromSeconds(5));
            Check(RequestedTheme == ElementTheme.Light, "Light theme was not applied to the main root", failures);

            OpenSettingsWindow();
            await WaitUntilAsync(() => _settingsWindowController.CurrentPage is { RequestedTheme: ElementTheme.Light }, TimeSpan.FromSeconds(5));
            Check(_settingsWindowController.CurrentPage is { RequestedTheme: ElementTheme.Light }, "new settings root did not receive the current theme", failures);

            var originalCommitTimeMode = settings.CommitTimeDisplayMode;
            var alternateCommitTimeMode = originalCommitTimeMode == CommitTimeDisplayMode.Smart
                ? CommitTimeDisplayMode.Relative
                : CommitTimeDisplayMode.Smart;
            await settings.SetCommitTimeDisplayModeAsync(alternateCommitTimeMode);
            Check(RequestedTheme == ElementTheme.Light && _settingsWindowController.CurrentPage is { RequestedTheme: ElementTheme.Light }, "unrelated settings change desynchronized the theme", failures);
            await settings.SetCommitTimeDisplayModeAsync(originalCommitTimeMode);

            await settings.SetThemeModeAsync(ApplicationThemeMode.Dark);
            await WaitUntilAsync(
                () => RequestedTheme == ElementTheme.Dark && _settingsWindowController.CurrentPage is { RequestedTheme: ElementTheme.Dark },
                TimeSpan.FromSeconds(5));
            Check(RequestedTheme == ElementTheme.Dark, "Dark theme was not applied to the main root", failures);
            Check(_settingsWindowController.CurrentPage is { RequestedTheme: ElementTheme.Dark }, "Dark theme was not propagated to the settings root", failures);

            _settingsWindowController.CloseCurrent();
            await settings.SetThemeModeAsync(ApplicationThemeMode.System);
            await WaitUntilAsync(() => RequestedTheme == ElementTheme.Default, TimeSpan.FromSeconds(5));
            Check(RequestedTheme == ElementTheme.Default, "Dark to System did not clear the main root override", failures);

            Check(RepositoryWorkspace.ActualWidth > 0 && RepositoryWorkspace.ActualHeight > 0, "workspace was not laid out", failures);
            Check(HistoryList.ActualWidth > 0 && HistoryList.ActualHeight > 0, "history list was not laid out", failures);
            Check(RepositoryTree.ActualWidth > 0 && _repositoryTreeRoots.Count >= 4, "repository tree was not laid out", failures);
            Check(DetailsScroller.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled && DetailsScroller.VerticalScrollBarVisibility == ScrollBarVisibility.Auto, "detail message wrapping is not configured", failures);
            await WaitUntilAsync(
                () => _commitDetailsView is { ActualWidth: > 0 } && DetailsScroller.ViewportWidth > 0,
                TimeSpan.FromSeconds(5));
            var commitMessageText = _commitDetailsView is null ? null : FindDescendant<TextBlock>(_commitDetailsView);
            var historyOrigin = HistoryPane.TransformToVisual(null).TransformPoint(default);
            var availableDetailsWidth = ((Microsoft.UI.Xaml.Application.Current as App)?.MainWindowClientWidth ?? 0) /
                                        (XamlRoot?.RasterizationScale ?? 1) - historyOrigin.X;
            Check(_commitDetailsView is not null && _commitDetailsView.ActualWidth <= availableDetailsWidth + 1, "commit details exceed the visible window viewport", failures);
            Check(
                commitMessageText is not null && commitMessageText.ActualHeight > commitMessageText.FontSize * 2,
                $"long commit message did not wrap to multiple lines: viewport={DetailsScroller.ViewportWidth}, details={_commitDetailsView?.ActualWidth}, text={commitMessageText?.ActualWidth}x{commitMessageText?.ActualHeight}, font={commitMessageText?.FontSize}, length={commitMessageText?.Text.Length}, window={(Microsoft.UI.Xaml.Application.Current as App)?.MainWindowClientWidth}, scale={XamlRoot?.RasterizationScale}, historyOrigin={historyOrigin.X}",
                failures);
            if (Microsoft.UI.Xaml.Application.Current is App resizeCheckApp && commitMessageText is not null)
            {
                var wideMessageHeight = commitMessageText.ActualHeight;
                var messageLeft = commitMessageText.TransformToVisual(HistoryPane).TransformPoint(default).X;
                resizeCheckApp.ResizeMainWindowForCheck(700, 900);
                await WaitUntilAsync(
                    () => resizeCheckApp.MainWindowClientWidth < 800 &&
                          _commitDetailsView!.ActualWidth < availableDetailsWidth - 200 &&
                          commitMessageText.ActualHeight > wideMessageHeight,
                    TimeSpan.FromSeconds(5));
                var narrowMessageHeight = commitMessageText.ActualHeight;
                Check(Math.Abs(commitMessageText.TransformToVisual(HistoryPane).TransformPoint(default).X - messageLeft) <= 1,
                    "commit message moved away from its left edge after shrinking the window", failures);
                resizeCheckApp.ResizeMainWindowForCheck(1400, 900);
                await WaitUntilAsync(
                    () => _commitDetailsView!.ActualWidth >= availableDetailsWidth - 1 &&
                          commitMessageText.ActualHeight < narrowMessageHeight,
                    TimeSpan.FromSeconds(5));
                Check(Math.Abs(commitMessageText.TransformToVisual(HistoryPane).TransformPoint(default).X - messageLeft) <= 1,
                    "commit message moved away from its left edge after expanding the window", failures);
            }
            Check(CountDescendants<Controls.GridSplitter>(RootLayout) >= 2, "resizable splitters are missing", failures);
            Check(CountDescendants<ScrollViewer>(RootLayout) > 0, "scroll viewers are missing", failures);
            var xamlRoot = XamlRoot;
            Check(xamlRoot is not null && (RootLayout.Clip is not null || RootLayout.ActualWidth <= xamlRoot.Size.Width + 1), "root content exceeds its viewport", failures);
            var splitter = FindDescendant<Controls.GridSplitter>(RootLayout);
            var splitterGrid = splitter?.Parent as Grid;
            var oldWidth = splitterGrid is null ? 0 : splitterGrid.ColumnDefinitions[0].ActualWidth;
            splitter?.ResizeForCheck(24);
            Check(splitterGrid is not null && splitterGrid.ColumnDefinitions[0].Width.IsAbsolute && Math.Abs(splitterGrid.ColumnDefinitions[0].Width.Value - oldWidth) > 1, "splitter did not resize its pane", failures);

            Check(CommitNavigationText.Text == $"Commit ({_viewModel.WorkingTree.Changes.Count})", "commit toolbar count is incorrect", failures);
            ShowWorkingTree();
            Check(WorkingTreePane.Visibility == Visibility.Visible && HistoryPane.Visibility == Visibility.Collapsed, "working tree mode did not open", failures);
            Check(CommitNavigationText.Text == "Back to history", "commit toolbar did not become the history navigation action", failures);
            Check(_unstagedChanges.Count + _stagedChanges.Count >= _viewModel.WorkingTree.Changes.Count, "working tree staged/unstaged views lost changes", failures);
            await ShowAllHistoryAsync();
            Check(CommitNavigationText.Text == $"Commit ({_viewModel.WorkingTree.Changes.Count})", "commit toolbar did not restore the change count", failures);

            if (_viewModel.History.Rows.FirstOrDefault() is { } selectedBeforeRefresh)
            {
                _viewModel.History.SelectedRow = selectedBeforeRefresh;
                var selectedHash = selectedBeforeRefresh.Commit.Hash;
                await _viewModel.RefreshAsyncForDesktopCheck();
                Check(_viewModel.History.SelectedRow?.Commit.Hash == selectedHash, "history refresh did not preserve the selected commit", failures);
                Check(_viewModel.History.Rows.Any(row => ReferenceEquals(row, _viewModel.History.SelectedRow)), "history refresh kept a stale selected row instance", failures);
            }

            if (_viewModel.Branches.LocalBranches.FirstOrDefault(branch => branch.IsCurrent) is { } currentBranch)
            {
                await ShowReferenceHistoryAsync(currentBranch.Name, $"Branch: {currentBranch.Name}");
                Check(_viewModel.History.Rows.Any(row => ReferenceEquals(row, _viewModel.History.SelectedRow)), "scoped history selection is not part of the current ItemsSource", failures);
                await ShowAllHistoryAsync();
                Check(_viewModel.History.SelectedRow is null || _viewModel.History.Rows.Any(row => ReferenceEquals(row, _viewModel.History.SelectedRow)), "all-history selection is not part of the current ItemsSource", failures);
            }

            _viewModel.CommitMessage = "draft retained by close guard";
            Check(_viewModel.HasUnappliedCommitMessage, "commit draft close guard is inactive", failures);
            if (_viewModel.WorkingTree.Changes.FirstOrDefault(change => change.IsUnstaged) is { } workingTreeCheckChange)
                await _viewModel.WorkingTree.SelectChangeAsync(workingTreeCheckChange, WorkingTreeDiffKind.Unstaged);
            Check(_viewModel.WorkingTree.StageCommand.CanExecute(null), "Stage must be enabled for an unstaged selection", failures);
            Check(!_viewModel.WorkingTree.UnstageCommand.CanExecute(null), "Unstage must be disabled for an unstaged selection", failures);
            var selectedChange = _viewModel.WorkingTree.SelectedChange;
            _viewModel.WorkingTree.ClearActiveSelection();
            Check(!_viewModel.WorkingTree.StageCommand.CanExecute(null) && !_viewModel.WorkingTree.UnstageCommand.CanExecute(null), "file commands must be disabled without a selection", failures);
            if (selectedChange is not null)
                await _viewModel.WorkingTree.SelectChangeAsync(selectedChange, WorkingTreeDiffKind.Unstaged);
            Check(_viewModel.CommitCommand.CanExecute(null), "Commit must be enabled for a non-empty draft", failures);
            _viewModel.CommitCommand.Execute(null);
            await WaitUntilAsync(() => !_viewModel.IsBusy, TimeSpan.FromSeconds(20));
            Check(_viewModel.IsEmptyIndexChoiceOpen, "empty index did not present an explicit choice", failures);
            Check(_viewModel.CommitMessage == "draft retained by close guard", "commit draft was lost", failures);
            Check(busyObserved && !BusyIndicator.IsActive, "busy indication did not transition back to idle", failures);

            await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(new DesktopCheckResult(failures.Count == 0, failures, _viewModel.Repository?.IsWorktree ?? false)));
        }
        catch (Exception exception)
        {
            failures.Add(exception.ToString());
            await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(new DesktopCheckResult(false, failures, false)));
        }
        finally
        {
            Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().TryEnqueue(() => Environment.Exit(failures.Count == 0 ? 0 : 1));
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(50);
        if (!condition()) throw new TimeoutException("The desktop UI check timed out.");
    }

    private static int CountDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = 0;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T) count++;
            count += CountDescendants<T>(child);
        }
        return count;
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } descendant) return descendant;
        }
        return default;
    }

    private static void Check(bool condition, string failure, ICollection<string> failures)
    {
        if (!condition) failures.Add(failure);
    }

    private sealed record DesktopCheckResult(bool Passed, IReadOnlyList<string> Failures, bool IsWorktree);
}

internal static class DispatcherQueueExtensions
{
    public static Task EnqueueAsync(this DispatcherQueue queue, Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!queue.TryEnqueue(() =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception exception) { completion.SetException(exception); }
        })) completion.SetException(new InvalidOperationException("The UI dispatcher rejected the check."));
        return completion.Task;
    }
}
