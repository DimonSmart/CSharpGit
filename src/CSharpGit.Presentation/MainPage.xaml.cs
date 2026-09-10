using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Windows.Input;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;

namespace CSharpGit.Presentation;

public sealed partial class MainPage : Page
{
    private readonly OpenRepositoryViewModel _viewModel;
    private readonly IReferenceHistoryService _referenceHistoryService;
    private readonly IReferenceService _referenceService;
    private readonly ObservableCollection<RepositoryTreeNode> _repositoryTreeRoots = [];
    private readonly ObservableCollection<HistoryRow> _scopedHistory = [];
    private readonly ObservableCollection<WorkingTreeChange> _unstagedChanges = [];
    private readonly ObservableCollection<WorkingTreeChange> _stagedChanges = [];
    private readonly ObservableCollection<CommitFileRow> _commitFiles = [];
    private CancellationTokenSource? _referenceHistoryCts;
    private string? _activeReference;
    private bool _scopedHasMore;
    private bool _isScopedHistoryLoading;
    private bool _wasBusy;

    public MainPage(OpenRepositoryViewModel viewModel, IReferenceHistoryService referenceHistoryService, IReferenceService referenceService, IWorkingTreeDiffService? workingTreeDiffService = null)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        _referenceHistoryService = referenceHistoryService;
        _referenceService = referenceService;
        _workingTreeDiffService = workingTreeDiffService;

        RepositoryTree.ItemsSource = _repositoryTreeRoots;
        HistoryList.ItemsSource = _viewModel.History;
        UnstagedChangesList.ItemsSource = _unstagedChanges;
        StagedChangesList.ItemsSource = _stagedChanges;
        CommitFilesList.ItemsSource = _commitFiles;

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.Changes.CollectionChanged += (_, _) => QueueRepositoryPresentationRefresh(workingTreeChanged: true);
        _viewModel.LocalBranches.CollectionChanged += (_, _) => QueueRepositoryPresentationRefresh();
        _viewModel.RemoteBranches.CollectionChanged += (_, _) => QueueRepositoryPresentationRefresh();
        _viewModel.Remotes.CollectionChanged += (_, _) => QueueRepositoryPresentationRefresh();
        _viewModel.Tags.CollectionChanged += (_, _) => QueueRepositoryPresentationRefresh();
        _viewModel.Stashes.CollectionChanged += (_, _) => QueueRepositoryPresentationRefresh();
        Loaded += RunDesktopCheckWhenRequested;
        RefreshPresentationCollections();
        InitializeWorkingTreeDiffSurface();
        InitializeCommitActions();
    }

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
        if (eventArgs.PropertyName is nameof(OpenRepositoryViewModel.Repository) or nameof(OpenRepositoryViewModel.HeadDisplay) or nameof(OpenRepositoryViewModel.CurrentOperation))
        {
            RebuildRepositoryTree();
            UpdateStatusBar();
        }
        else if (eventArgs.PropertyName == nameof(OpenRepositoryViewModel.SelectedCommit))
        {
            _ = RefreshCommitFilesAsync();
        }
        else if (eventArgs.PropertyName == nameof(OpenRepositoryViewModel.SelectedHistoryRow) && _activeReference is not null)
        {
            var selectedHash = _viewModel.SelectedHistoryRow?.Commit.Hash;
            var visibleSelection = selectedHash is null
                ? null
                : _scopedHistory.FirstOrDefault(row => string.Equals(row.Commit.Hash, selectedHash, StringComparison.Ordinal));
            visibleSelection ??= _scopedHistory.FirstOrDefault();
            if (!ReferenceEquals(visibleSelection, _viewModel.SelectedHistoryRow))
                _viewModel.SelectedHistoryRow = visibleSelection;
        }
        else if (eventArgs.PropertyName == nameof(OpenRepositoryViewModel.HasMore) && _activeReference is null)
        {
            LoadMoreHistoryButton.IsEnabled = _viewModel.HasMore;
        }
        else if (eventArgs.PropertyName == nameof(OpenRepositoryViewModel.IsBusy))
        {
            var becameIdle = _wasBusy && !_viewModel.IsBusy;
            _wasBusy = _viewModel.IsBusy;
            UpdateStatusBar();
            if (becameIdle && _activeReference is not null && !_isScopedHistoryLoading)
                _ = LoadScopedHistoryAsync(true);
        }
    }

    private void RefreshPresentationCollections()
    {
        _workingTreeSelectionSync = true;
        try
        {
            _unstagedChanges.Clear();
            _stagedChanges.Clear();
            foreach (var change in _viewModel.Changes)
            {
                if (change.IsUnstaged) _unstagedChanges.Add(change);
                if (change.IsStaged) _stagedChanges.Add(change);
            }
        }
        finally
        {
            _workingTreeSelectionSync = false;
        }

        UnstagedHeader.Text = $"Unstaged changes ({_unstagedChanges.Count})";
        StagedHeader.Text = $"Staged changes ({_stagedChanges.Count})";
        RebuildRepositoryTree();
        UpdateStatusBar();
    }

    private void RebuildRepositoryTree()
    {
        _repositoryTreeRoots.Clear();
        if (_viewModel.Repository is null) return;

        _repositoryTreeRoots.Add(new RepositoryTreeNode(
            RepositoryTreeNodeKind.WorkingTree,
            $"Working tree ({_viewModel.Changes.Count})"));

        _repositoryTreeRoots.Add(new RepositoryTreeNode(
            RepositoryTreeNodeKind.Group,
            "Branches",
            isExpanded: true,
            children: _viewModel.LocalBranches
                .OrderByDescending(branch => branch.IsCurrent)
                .ThenBy(branch => branch.Name, StringComparer.OrdinalIgnoreCase)
                .Select(branch => new RepositoryTreeNode(
                    RepositoryTreeNodeKind.LocalBranch,
                    branch.Name,
                    branch.Name,
                    branch,
                    branch.IsCurrent))));

        var remoteNodes = new List<RepositoryTreeNode>();
        foreach (var remote in _viewModel.Remotes.OrderBy(remote => remote.Name, StringComparer.OrdinalIgnoreCase))
        {
            var prefix = remote.Name + "/";
            var branches = _viewModel.RemoteBranches
                .Where(branch => branch.Name.StartsWith(prefix, StringComparison.Ordinal))
                .OrderBy(branch => branch.Name, StringComparer.OrdinalIgnoreCase)
                .Select(branch => new RepositoryTreeNode(
                    RepositoryTreeNodeKind.RemoteBranch,
                    branch.Name[prefix.Length..],
                    branch.Name,
                    branch))
                .ToList();
            remoteNodes.Add(new RepositoryTreeNode(
                RepositoryTreeNodeKind.Remote,
                remote.Name,
                value: remote,
                isExpanded: true,
                children: branches));
        }
        _repositoryTreeRoots.Add(new RepositoryTreeNode(
            RepositoryTreeNodeKind.Group,
            "Remotes",
            isExpanded: true,
            children: remoteNodes));

        _repositoryTreeRoots.Add(new RepositoryTreeNode(
            RepositoryTreeNodeKind.Group,
            "Tags",
            children: _viewModel.Tags
                .OrderByDescending(tag => tag.Name, StringComparer.OrdinalIgnoreCase)
                .Select(tag => new RepositoryTreeNode(RepositoryTreeNodeKind.Tag, tag.Name, tag.Name, tag))));

        _repositoryTreeRoots.Add(new RepositoryTreeNode(
            RepositoryTreeNodeKind.Group,
            "Stashes",
            children: _viewModel.Stashes.Select(stash => new RepositoryTreeNode(
                RepositoryTreeNodeKind.Stash,
                $"{stash.Name}: {stash.Message}",
                stash.Commit,
                stash))));
    }

    private void UpdateStatusBar()
    {
        var current = _viewModel.LocalBranches.FirstOrDefault(branch => branch.IsCurrent);
        var branch = current?.Name ?? (_viewModel.Repository is null ? string.Empty : "detached HEAD");
        ToolbarBranchText.Text = branch;
        StatusBranchText.Text = branch;
        StatusTrackingText.Text = current is null ? string.Empty : $"↑{current.Ahead} ↓{current.Behind}";
        StatusChangesText.Text = $"{_viewModel.Changes.Count} changes";
        StatusOperationText.Text = _viewModel.IsBusy
            ? "Working…"
            : _viewModel.CurrentOperation == RepositoryOperation.None ? "Ready" : _viewModel.CurrentOperation.ToString();
    }

    private async Task RefreshCommitFilesAsync()
    {
        var selectedCommit = _viewModel.SelectedCommit;
        _commitFiles.Clear();
        if (selectedCommit is null || _viewModel.Repository is null)
        {
            FilesTab.Header = "Files";
            return;
        }

        FilesTab.Header = $"Files ({selectedCommit.Files.Count})";
        IReadOnlyDictionary<string, string> statuses;
        try
        {
            statuses = await _referenceHistoryService.ReadFileStatusesAsync(_viewModel.Repository, selectedCommit.Commit.Hash);
        }
        catch
        {
            statuses = new Dictionary<string, string>();
        }

        foreach (var file in selectedCommit.Files)
            _commitFiles.Add(new CommitFileRow(statuses.GetValueOrDefault(file.Path, "?"), file));

        var selected = _commitFiles.FirstOrDefault(row => row.File == _viewModel.SelectedFile) ?? _commitFiles.FirstOrDefault();
        CommitFilesList.SelectedItem = selected;
    }

    private async Task ShowReferenceHistoryAsync(string reference, string label)
    {
        _viewModel.InvalidateHistoryLoad();
        _activeReference = reference;
        ActiveReferenceText.Text = label;
        ScopeCombo.Visibility = Visibility.Collapsed;
        ReferenceScopePanel.Visibility = Visibility.Visible;
        HistoryPane.Visibility = Visibility.Visible;
        WorkingTreePane.Visibility = Visibility.Collapsed;
        await LoadScopedHistoryAsync(true);
    }

    private async Task LoadScopedHistoryAsync(bool reset)
    {
        if (_viewModel.Repository is null || _activeReference is null) return;

        var selectedHash = reset ? _viewModel.SelectedHistoryRow?.Commit.Hash : null;
        var skip = reset ? 0 : _scopedHistory.Count;
        if (reset)
        {
            _referenceHistoryCts?.Cancel();
            _referenceHistoryCts?.Dispose();
            _referenceHistoryCts = new CancellationTokenSource();
        }
        _referenceHistoryCts ??= new CancellationTokenSource();
        var repository = _viewModel.Repository;
        var reference = _activeReference;
        var token = _referenceHistoryCts.Token;
        _isScopedHistoryLoading = true;
        LoadMoreHistoryButton.IsEnabled = false;
        try
        {
            var page = await _referenceHistoryService.ReadHistoryAsync(
                repository,
                reference,
                _viewModel.FilterText,
                skip,
                100,
                token);
            if (token.IsCancellationRequested
                || !ReferenceEquals(repository, _viewModel.Repository)
                || !string.Equals(reference, _activeReference, StringComparison.Ordinal))
                return;

            if (reset) _scopedHistory.Clear();
            foreach (var row in page.Rows) _scopedHistory.Add(row);
            _scopedHasMore = page.HasMore;
            HistoryList.ItemsSource = _scopedHistory;

            if (reset)
            {
                var restored = selectedHash is null
                    ? _scopedHistory.FirstOrDefault()
                    : _scopedHistory.FirstOrDefault(row => string.Equals(row.Commit.Hash, selectedHash, StringComparison.Ordinal))
                      ?? _scopedHistory.FirstOrDefault();
                _viewModel.SelectedHistoryRow = restored;
            }
            else if (_viewModel.SelectedHistoryRow is null && _scopedHistory.FirstOrDefault() is { } first)
            {
                _viewModel.SelectedHistoryRow = first;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            await ShowErrorAsync("Could not read the selected reference history", exception.Message);
        }
        finally
        {
            _isScopedHistoryLoading = false;
            if (string.Equals(reference, _activeReference, StringComparison.Ordinal))
                LoadMoreHistoryButton.IsEnabled = _scopedHasMore;
        }
    }

    private void ShowAllHistory()
    {
        var selectedHash = _viewModel.SelectedHistoryRow?.Commit.Hash;
        _referenceHistoryCts?.Cancel();
        _activeReference = null;
        ScopeCombo.Visibility = Visibility.Visible;
        ReferenceScopePanel.Visibility = Visibility.Collapsed;
        HistoryPane.Visibility = Visibility.Visible;
        WorkingTreePane.Visibility = Visibility.Collapsed;
        HistoryList.ItemsSource = _viewModel.History;
        LoadMoreHistoryButton.IsEnabled = _viewModel.HasMore;

        var restored = selectedHash is null
            ? _viewModel.History.FirstOrDefault()
            : _viewModel.History.FirstOrDefault(row => string.Equals(row.Commit.Hash, selectedHash, StringComparison.Ordinal))
              ?? _viewModel.History.FirstOrDefault();
        _viewModel.SelectedHistoryRow = restored;

        if (_viewModel.SelectedScope != _viewModel.Scopes[0])
            _viewModel.SelectedScope = _viewModel.Scopes[0];
    }

    private void ShowWorkingTree()
    {
        HistoryPane.Visibility = Visibility.Collapsed;
        WorkingTreePane.Visibility = Visibility.Visible;
        if (_unstagedChanges.FirstOrDefault() is { } unstaged)
        {
            UnstagedChangesList.SelectedItem = unstaged;
            _viewModel.SelectedChange = unstaged;
        }
        else if (_stagedChanges.FirstOrDefault() is { } staged)
        {
            StagedChangesList.SelectedItem = staged;
            _viewModel.SelectedChange = staged;
        }
    }

    private async void RepositoryTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (ResolveNode(args.InvokedItem) is { } node) await SelectRepositoryNodeAsync(node);
    }

    private async Task SelectRepositoryNodeAsync(RepositoryTreeNode node)
    {
        switch (node.Kind)
        {
            case RepositoryTreeNodeKind.WorkingTree:
                ShowWorkingTree();
                break;
            case RepositoryTreeNodeKind.LocalBranch when node.Value is GitBranch local:
                _viewModel.SelectedLocalBranch = local;
                await ShowReferenceHistoryAsync(local.Name, $"Branch: {local.Name}");
                break;
            case RepositoryTreeNodeKind.RemoteBranch when node.Value is GitBranch remoteBranch:
                _viewModel.SelectedRemoteBranch = remoteBranch;
                await ShowReferenceHistoryAsync(remoteBranch.Name, $"Remote: {remoteBranch.Name}");
                break;
            case RepositoryTreeNodeKind.Tag when node.Value is GitTag tag:
                _viewModel.SelectedTag = tag;
                await ShowReferenceHistoryAsync(tag.Name, $"Tag: {tag.Name}");
                break;
            case RepositoryTreeNodeKind.Stash when node.Value is GitStash stash:
                _viewModel.SelectedStash = stash;
                await ShowReferenceHistoryAsync(stash.Commit, $"Stash: {stash.Name}");
                break;
            case RepositoryTreeNodeKind.Group:
                ShowAllHistory();
                break;
        }
    }

    private async void RepositoryTree_DoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        var node = ResolveNode((args.OriginalSource as FrameworkElement)?.DataContext);
        if (node?.Kind != RepositoryTreeNodeKind.LocalBranch || node.Value is not GitBranch branch) return;
        _viewModel.SelectedLocalBranch = branch;
        await ExecuteCommandAsync(_viewModel.SwitchBranchCommand);
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
                    _viewModel.SelectedLocalBranch = branch;
                    await ExecuteCommandAsync(_viewModel.SwitchBranchCommand);
                });
                AddMenuItem(flyout, "Create branch from here…", !_viewModel.IsBusy, () => CreateBranchFromAsync(branch.Name));
                AddMenuItem(flyout, "Merge into current branch", !branch.IsCurrent && !_viewModel.IsBusy, async () =>
                {
                    _viewModel.SelectedMergeBranch = branch;
                    await ExecuteCommandAsync(_viewModel.MergeCommand);
                });
                AddMenuItem(flyout, "Delete", !branch.IsCurrent && !_viewModel.IsBusy, async () =>
                {
                    _viewModel.SelectedLocalBranch = branch;
                    await ExecuteCommandAsync(_viewModel.DeleteBranchCommand);
                });
                flyout.Items.Add(new MenuFlyoutSeparator());
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
                    _viewModel.SelectedRemoteBranch = remoteBranch;
                    var slash = remoteBranch.Name.IndexOf('/');
                    _viewModel.NewBranchName = slash >= 0 ? remoteBranch.Name[(slash + 1)..] : remoteBranch.Name;
                    await ExecuteCommandAsync(_viewModel.CheckoutRemoteCommand);
                });
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

            case RepositoryTreeNodeKind.Stash when node.Value is GitStash stash:
                AddMenuItem(flyout, "Apply", !_viewModel.IsBusy, async () =>
                {
                    _viewModel.SelectedStash = stash;
                    await ExecuteCommandAsync(_viewModel.ApplyStashCommand);
                });
                AddMenuItem(flyout, "Pop", !_viewModel.IsBusy, async () =>
                {
                    _viewModel.SelectedStash = stash;
                    await ExecuteCommandAsync(_viewModel.PopStashCommand);
                });
                break;
        }

        if (flyout.Items.Count == 0) return;
        flyout.ShowAt(source, args.GetPosition(source));
        args.Handled = true;
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
        try
        {
            await _referenceService.CreateBranchAsync(_viewModel.Repository, input.Text.Trim(), startPoint, true);
            await _viewModel.RefreshAsyncForDesktopCheck();
        }
        catch (Exception exception)
        {
            await ShowErrorAsync("Could not create branch", exception.Message);
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
    private async void Push_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button)
        {
            GitOperationsDialog.Hide();
            await Task.Delay(20);
        }
        await PushFromUiAsync();
    }

    private async void RefreshAll_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshAsyncForDesktopCheck();
        RefreshPresentationCollections();
    }

    private async void MoreOperations_Click(object sender, RoutedEventArgs e) => await GitOperationsDialog.ShowAsync();

    private async void ApplyHistoryFilter_Click(object sender, RoutedEventArgs e) => await RefreshVisibleHistoryAsync();
    private async void RefreshHistory_Click(object sender, RoutedEventArgs e) => await RefreshVisibleHistoryAsync();

    private async Task RefreshVisibleHistoryAsync()
    {
        if (_activeReference is not null) await LoadScopedHistoryAsync(true);
        else await ExecuteCommandAsync(_viewModel.RefreshHistoryCommand);
    }

    private async void HistoryFilter_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        await RefreshVisibleHistoryAsync();
    }

    private async void LoadMoreHistory_Click(object sender, RoutedEventArgs e)
    {
        if (_activeReference is not null) await LoadScopedHistoryAsync(false);
        else await ExecuteCommandAsync(_viewModel.LoadMoreCommand);
    }

    private void ClearReference_Click(object sender, RoutedEventArgs e) => ShowAllHistory();
    private void ShowHistory_Click(object sender, RoutedEventArgs e) => ShowAllHistory();

    private void UnstagedChangesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UnstagedChangesList.SelectedItem is WorkingTreeChange change) _viewModel.SelectedChange = change;
    }

    private void StagedChangesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StagedChangesList.SelectedItem is WorkingTreeChange change) _viewModel.SelectedChange = change;
    }

    private void CommitFilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CommitFilesList.SelectedItem is CommitFileRow row) _viewModel.SelectedFile = row.File;
    }

    private void CommitFilesList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (CommitFilesList.SelectedItem is null) return;
        DetailsTabs.SelectedIndex = 2;
        e.Handled = true;
    }

    private void CommitFilesList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || CommitFilesList.SelectedItem is null) return;
        DetailsTabs.SelectedIndex = 2;
        e.Handled = true;
    }

    private async void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var control = IsControlDown();
        if (control && e.Key == VirtualKey.F)
        {
            if (WorkingTreePane.Visibility == Visibility.Visible) ShowAllHistory();
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
            ShowAllHistory();
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
                 (_viewModel.Changes.Count == 0 || _viewModel.History.Count == 0); attempt++)
            {
                await Task.Delay(100 * (attempt + 1));
                await _viewModel.RefreshAsyncForDesktopCheck();
                RefreshPresentationCollections();
            }
            Check(_viewModel.Changes.Count > 0, $"working tree changes were not loaded: {_viewModel.ErrorMessage}", failures);
            Check(_viewModel.History.Count > 0, $"history was not loaded: {_viewModel.ErrorMessage}", failures);
            await WaitUntilAsync(
                () => RepositoryWorkspace.ActualWidth > 0 && RepositoryWorkspace.ActualHeight > 0 && HistoryList.ActualWidth > 0 && HistoryList.ActualHeight > 0,
                TimeSpan.FromSeconds(10));
            Check(_viewModel.Repository is not null, "repository did not open", failures);
            Check(_viewModel.Repository?.IsWorktree == (Environment.GetEnvironmentVariable("CSHARPGIT_UI_CHECK_WORKTREE") == "1"), "repository kind is incorrect", failures);
            Check(_viewModel.Scopes.All(scope => scope.Label is "All references" or "Current branch"), "English history scopes are missing", failures);

            var settings = AppSettingsContext.Current;
            await settings.SetThemeModeAsync(ApplicationThemeMode.System);
            await WaitUntilAsync(() => RequestedTheme == ElementTheme.Default, TimeSpan.FromSeconds(5));
            Check(RequestedTheme == ElementTheme.Default, "System theme did not clear the root override", failures);

            await settings.SetThemeModeAsync(ApplicationThemeMode.Light);
            await WaitUntilAsync(() => RequestedTheme == ElementTheme.Light, TimeSpan.FromSeconds(5));
            Check(RequestedTheme == ElementTheme.Light, "Light theme was not applied to the main root", failures);

            OpenSettingsWindow();
            await WaitUntilAsync(() => _settingsPage is { RequestedTheme: ElementTheme.Light }, TimeSpan.FromSeconds(5));
            Check(_settingsPage is { RequestedTheme: ElementTheme.Light }, "new settings root did not receive the current theme", failures);

            var originalCommitTimeMode = settings.CommitTimeDisplayMode;
            var alternateCommitTimeMode = originalCommitTimeMode == CommitTimeDisplayMode.Smart
                ? CommitTimeDisplayMode.Relative
                : CommitTimeDisplayMode.Smart;
            await settings.SetCommitTimeDisplayModeAsync(alternateCommitTimeMode);
            Check(RequestedTheme == ElementTheme.Light && _settingsPage is { RequestedTheme: ElementTheme.Light }, "unrelated settings change desynchronized the theme", failures);
            await settings.SetCommitTimeDisplayModeAsync(originalCommitTimeMode);

            await settings.SetThemeModeAsync(ApplicationThemeMode.Dark);
            await WaitUntilAsync(
                () => RequestedTheme == ElementTheme.Dark && _settingsPage is { RequestedTheme: ElementTheme.Dark },
                TimeSpan.FromSeconds(5));
            Check(RequestedTheme == ElementTheme.Dark, "Dark theme was not applied to the main root", failures);
            Check(_settingsPage is { RequestedTheme: ElementTheme.Dark }, "Dark theme was not propagated to the settings root", failures);

            CloseSettingsWindow();
            await settings.SetThemeModeAsync(ApplicationThemeMode.System);
            await WaitUntilAsync(() => RequestedTheme == ElementTheme.Default, TimeSpan.FromSeconds(5));
            Check(RequestedTheme == ElementTheme.Default, "Dark to System did not clear the main root override", failures);

            Check(RepositoryWorkspace.ActualWidth > 0 && RepositoryWorkspace.ActualHeight > 0, "workspace was not laid out", failures);
            Check(HistoryList.ActualWidth > 0 && HistoryList.ActualHeight > 0, "history list was not laid out", failures);
            Check(RepositoryTree.ActualWidth > 0 && _repositoryTreeRoots.Count >= 5, "repository tree was not laid out", failures);
            Check(DetailsScroller.HorizontalScrollBarVisibility == ScrollBarVisibility.Auto && DetailsScroller.VerticalScrollBarVisibility == ScrollBarVisibility.Auto, "detail scrolling is not automatic", failures);
            Check(CountDescendants<Controls.GridSplitter>(RootLayout) >= 2, "resizable splitters are missing", failures);
            Check(CountDescendants<ScrollViewer>(RootLayout) > 0, "scroll viewers are missing", failures);
            var xamlRoot = XamlRoot;
            Check(xamlRoot is not null && (RootLayout.Clip is not null || RootLayout.ActualWidth <= xamlRoot.Size.Width + 1), "root content exceeds its viewport", failures);
            var splitter = FindDescendant<Controls.GridSplitter>(RootLayout);
            var splitterGrid = splitter?.Parent as Grid;
            var oldWidth = splitterGrid is null ? 0 : splitterGrid.ColumnDefinitions[0].ActualWidth;
            splitter?.ResizeForCheck(24);
            Check(splitterGrid is not null && splitterGrid.ColumnDefinitions[0].Width.IsAbsolute && Math.Abs(splitterGrid.ColumnDefinitions[0].Width.Value - oldWidth) > 1, "splitter did not resize its pane", failures);

            ShowWorkingTree();
            Check(WorkingTreePane.Visibility == Visibility.Visible && HistoryPane.Visibility == Visibility.Collapsed, "working tree mode did not open", failures);
            Check(_unstagedChanges.Count + _stagedChanges.Count >= _viewModel.Changes.Count, "working tree staged/unstaged views lost changes", failures);
            ShowAllHistory();

            if (_viewModel.History.FirstOrDefault() is { } selectedBeforeRefresh)
            {
                _viewModel.SelectedHistoryRow = selectedBeforeRefresh;
                var selectedHash = selectedBeforeRefresh.Commit.Hash;
                await _viewModel.RefreshAsyncForDesktopCheck();
                Check(_viewModel.SelectedHistoryRow?.Commit.Hash == selectedHash, "history refresh did not preserve the selected commit", failures);
                Check(_viewModel.History.Any(row => ReferenceEquals(row, _viewModel.SelectedHistoryRow)), "history refresh kept a stale selected row instance", failures);
            }

            if (_viewModel.LocalBranches.FirstOrDefault(branch => branch.IsCurrent) is { } currentBranch)
            {
                await ShowReferenceHistoryAsync(currentBranch.Name, $"Branch: {currentBranch.Name}");
                Check(_scopedHistory.Any(row => ReferenceEquals(row, _viewModel.SelectedHistoryRow)), "scoped history selection is not part of the current ItemsSource", failures);
                ShowAllHistory();
                Check(_viewModel.SelectedHistoryRow is null || _viewModel.History.Any(row => ReferenceEquals(row, _viewModel.SelectedHistoryRow)), "all-history selection is not part of the current ItemsSource", failures);
            }

            if (Environment.GetEnvironmentVariable("CSHARPGIT_GRAPH_VIEWPORT_CHECK") == "1")
                await RunCommitGraphViewportLifecycleCheckAsync(failures);

            _viewModel.CommitMessage = "draft retained by close guard";
            Check(_viewModel.HasUnappliedCommitMessage, "commit draft close guard is inactive", failures);
            _viewModel.SelectedChange = _viewModel.Changes.FirstOrDefault(change => change.IsUnstaged);
            Check(_viewModel.StageCommand.CanExecute(null), "Stage must be enabled for an unstaged selection", failures);
            Check(!_viewModel.UnstageCommand.CanExecute(null), "Unstage must be disabled for an unstaged selection", failures);
            var selectedChange = _viewModel.SelectedChange;
            _viewModel.SelectedChange = null;
            Check(!_viewModel.StageCommand.CanExecute(null) && !_viewModel.UnstageCommand.CanExecute(null), "file commands must be disabled without a selection", failures);
            _viewModel.SelectedChange = selectedChange;
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
