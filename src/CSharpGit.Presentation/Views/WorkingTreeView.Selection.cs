using CSharpGit.Presentation.Workflows;
using System.Collections.Specialized;
using System.ComponentModel;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;
using CSharpGit.Presentation.Controls;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;
using Windows.UI.Core;

namespace CSharpGit.Presentation.Views;

public sealed partial class WorkingTreeView
{
    private OpenRepositoryViewModel _viewModel = null!;
    private BulkObservableCollection<WorkingTreeChange> _unstagedChanges = null!;
    private BulkObservableCollection<WorkingTreeChange> _stagedChanges = null!;
    private WorkingTreeImageDiffWorkflow _workingTreeImageDiff = null!;
    private DiffPresentationController _diffPresentation = null!;
    private Func<bool> _isShuttingDown = null!;
    private Action<Func<Task>, string> _runUiOperation = null!;
    private Func<string, string, Task> _showError = null!;
    private bool _configured;
    private long _visibilityCallbackToken;
    private WorkingTreeView WorkingTreePane => this;
    private bool IsShuttingDown => _isShuttingDown();

    internal void Configure(
        OpenRepositoryViewModel viewModel,
        BulkObservableCollection<WorkingTreeChange> unstaged,
        BulkObservableCollection<WorkingTreeChange> staged,
        WorkingTreeImageDiffWorkflow imageDiff,
        DiffPresentationController diffPresentation,
        Func<bool> isShuttingDown,
        Action<Func<Task>, string> runUiOperation,
        Func<string, string, Task> showError,
        Action<MenuFlyout, string, Func<bool>, Func<Task>> addMenuItem,
        Func<System.Windows.Input.ICommand, Task> executeCommandAsync,
        Func<IReadOnlyCollection<WorkingTreeChange>, Task> showSelectedStashDialogAsync,
        Func<ContentDialog, Task<ContentDialogResult>> showDialog)
    {
        if (_configured) throw new InvalidOperationException("WorkingTreeView is already configured.");
        _configured = true;
        _viewModel = viewModel;
        _unstagedChanges = unstaged;
        _stagedChanges = staged;
        _workingTreeImageDiff = imageDiff;
        _diffPresentation = diffPresentation;
        _isShuttingDown = isShuttingDown;
        _runUiOperation = runUiOperation;
        _showError = showError;
        _addMenuItem = addMenuItem;
        _executeCommandAsync = executeCommandAsync;
        _showSelectedStashDialogAsync = showSelectedStashDialogAsync;
        _showDialog = showDialog;
    }

    private void RunPageUiOperation(Func<Task> action, string description) => _runUiOperation(action, description);
    private Task ShowErrorAsync(string title, string message) => _showError(title, message);
    private static bool IsControlDown() =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;

    internal void SetSelectionSync(bool value)
    {
        _workingTreeSelectionSync = value;
        if (!value) ScheduleWorkingTreeTreeRefreshIfNeeded();
    }

    internal IReadOnlyList<WorkingTreeChange> SelectWorkingTreeContextTarget(
        WorkingTreeTreeNode node, WorkingTreeDiffKind kind)
    {
        var roots = kind == WorkingTreeDiffKind.Unstaged ? _unstagedTreeRoots : _stagedTreeRoots;
        var selection = kind == WorkingTreeDiffKind.Unstaged ? _unstagedTreeSelection : _stagedTreeSelection;
        if (!selection.IsSelected(node.Path)) selection.SelectSingle(node, roots);
        var selected = selection.GetSelectedLeaves(roots).Select(n => n.Change!).ToArray();
        _viewModel.WorkingTree.SetSelection(kind, selected);
        SelectWorkingTreeChange(node.Change!, kind);
        return selected;
    }

    internal void ShutdownWorkingTreeDiffSurface()
    {
        if (!_configured) return;
        _configured = false;
        _unstagedChanges.CollectionChanged -= WorkingTreePresentationSourceChanged;
        _stagedChanges.CollectionChanged -= WorkingTreePresentationSourceChanged;
        _viewModel.WorkingTree.PropertyChanged -= WorkingTreeViewModel_PropertyChanged;
        UnstagedChangesTree.Expanding -= WorkingTreeTree_Expanding;
        UnstagedChangesTree.Collapsed -= WorkingTreeTree_Collapsed;
        StagedChangesTree.Expanding -= WorkingTreeTree_Expanding;
        StagedChangesTree.Collapsed -= WorkingTreeTree_Collapsed;
        UnregisterPropertyChangedCallback(VisibilityProperty, _visibilityCallbackToken);
        _unstagedViewport?.Dispose();
        _stagedViewport?.Dispose();
        _workingTreeImageDiff.Cancel();
    }


    private readonly BulkObservableCollection<WorkingTreeTreeNode> _unstagedTreeRoots = [];
    private readonly BulkObservableCollection<WorkingTreeTreeNode> _stagedTreeRoots = [];
    private readonly WorkingTreeTreeSelection _unstagedTreeSelection = new();
    private readonly WorkingTreeTreeSelection _stagedTreeSelection = new();
    private readonly Dictionary<string, bool> _unstagedExpansionState = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _stagedExpansionState = new(StringComparer.Ordinal);
    private bool _workingTreeSelectionSync;
    private bool _workingTreeTreeRefreshQueued;
    private bool _workingTreeTreeRefreshScheduled;
    private bool _workingTreePreviewRestoreQueued;
    private CollectionViewportController? _unstagedViewport;
    private CollectionViewportController? _stagedViewport;
    private string? _workingTreeExpansionRepositoryIdentity;
    private string? _desiredWorkingTreePath;

    private TreeView UnstagedChangesList => UnstagedChangesTree;
    private TreeView StagedChangesList => StagedChangesTree;

    internal void InitializeWorkingTreeDiffSurface()
    {
        UnstagedChangesTree.ItemsSource = _unstagedTreeRoots;
        StagedChangesTree.ItemsSource = _stagedTreeRoots;
        _unstagedViewport = new CollectionViewportController(UnstagedChangesTree);
        _stagedViewport = new CollectionViewportController(StagedChangesTree);
        UnstagedChangesTree.Expanding += WorkingTreeTree_Expanding;
        UnstagedChangesTree.Collapsed += WorkingTreeTree_Collapsed;
        StagedChangesTree.Expanding += WorkingTreeTree_Expanding;
        StagedChangesTree.Collapsed += WorkingTreeTree_Collapsed;

        _unstagedChanges.CollectionChanged += WorkingTreePresentationSourceChanged;
        _stagedChanges.CollectionChanged += WorkingTreePresentationSourceChanged;
        _viewModel.WorkingTree.PropertyChanged += WorkingTreeViewModel_PropertyChanged;
        _visibilityCallbackToken = RegisterPropertyChangedCallback(VisibilityProperty, (_, _) =>
        {
            if (IsShuttingDown) return;
            if (WorkingTreePane.Visibility == Visibility.Visible)
                EnsureWorkingTreeActivePreview();
            else
                _workingTreeImageDiff.Cancel();
        });

        RebuildWorkingTreeTrees();
        ClearWorkingTreeDiffViewer(clearSelectionKind: true);
    }

    private void WorkingTreePresentationSourceChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (IsShuttingDown || _workingTreeTreeRefreshQueued) return;
        _workingTreeTreeRefreshQueued = true;
        ScheduleWorkingTreeTreeRefreshIfNeeded();
    }

    private void ScheduleWorkingTreeTreeRefreshIfNeeded()
    {
        if (IsShuttingDown)
        {
            _workingTreeTreeRefreshQueued = false;
            _workingTreeTreeRefreshScheduled = false;
            return;
        }
        if (!_workingTreeTreeRefreshQueued ||
            _workingTreeTreeRefreshScheduled ||
            _workingTreeSelectionSync)
            return;

        _workingTreeTreeRefreshScheduled = true;
        if (DispatcherQueue.TryEnqueue(() =>
        {
            _workingTreeTreeRefreshScheduled = false;
            if (IsShuttingDown || !_workingTreeTreeRefreshQueued)
            {
                _workingTreeTreeRefreshQueued = false;
                return;
            }
            _workingTreeTreeRefreshQueued = false;
            RebuildWorkingTreeTrees();
        })) return;

        _workingTreeTreeRefreshScheduled = false;
        _workingTreeTreeRefreshQueued = false;
        if (!IsShuttingDown && DispatcherQueue.HasThreadAccess)
            RebuildWorkingTreeTrees();
    }

    internal void RebuildWorkingTreeTrees()
    {
        if (IsShuttingDown) return;
        var repositoryIdentity = _viewModel.Repository?.GitDirectory;
        if (string.Equals(repositoryIdentity, _workingTreeExpansionRepositoryIdentity, StringComparison.Ordinal))
        {
            WorkingTreeTreeExpansionState.Capture(_unstagedTreeRoots, _unstagedExpansionState);
            WorkingTreeTreeExpansionState.Capture(_stagedTreeRoots, _stagedExpansionState);
        }
        else
        {
            _workingTreeExpansionRepositoryIdentity = repositoryIdentity;
            _unstagedExpansionState.Clear();
            _stagedExpansionState.Clear();
        }

        var workingTree = _viewModel.WorkingTree;
        var selectedUnstagedPaths = workingTree.SelectedUnstagedChanges.Select(change => change.Path).ToArray();
        var selectedStagedPaths = workingTree.SelectedStagedChanges.Select(change => change.Path).ToArray();

        ReplaceRoots(_unstagedTreeRoots, WorkingTreeTreeNode.Build(_unstagedChanges, WorkingTreeDiffKind.Unstaged));
        ReplaceRoots(_stagedTreeRoots, WorkingTreeTreeNode.Build(_stagedChanges, WorkingTreeDiffKind.Staged));
        WorkingTreeTreeExpansionState.Restore(_unstagedTreeRoots, _unstagedExpansionState);
        WorkingTreeTreeExpansionState.Restore(_stagedTreeRoots, _stagedExpansionState);
        ObserveWorkingTreeViewports();

        _unstagedTreeSelection.SetSelectedPaths(selectedUnstagedPaths, _unstagedTreeRoots);
        _stagedTreeSelection.SetSelectedPaths(selectedStagedPaths, _stagedTreeRoots);
        workingTree.SetSelection(
            WorkingTreeDiffKind.Unstaged,
            _unstagedTreeSelection.GetSelectedLeaves(_unstagedTreeRoots).Select(node => node.Change!));
        workingTree.SetSelection(
            WorkingTreeDiffKind.Staged,
            _stagedTreeSelection.GetSelectedLeaves(_stagedTreeRoots).Select(node => node.Change!));

        if (WorkingTreePane.Visibility == Visibility.Visible) QueueWorkingTreePreviewRestore();
    }

    private void WorkingTreeTree_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (ResolveWorkingTreeNode(args.Item) is { } node) node.IsExpanded = true;
        ObserveWorkingTreeViewports();
    }

    private void WorkingTreeTree_Collapsed(TreeView sender, TreeViewCollapsedEventArgs args)
    {
        if (ResolveWorkingTreeNode(args.Item) is { } node) node.IsExpanded = false;
        ObserveWorkingTreeViewports();
    }

    private void ObserveWorkingTreeViewports()
    {
        var repositoryKey = _viewModel.Repository?.GitDirectory;
        _unstagedViewport?.Observe(repositoryKey, CountVisibleWorkingTreeNodes(_unstagedTreeRoots));
        _stagedViewport?.Observe(repositoryKey, CountVisibleWorkingTreeNodes(_stagedTreeRoots));
    }

    private static int CountVisibleWorkingTreeNodes(IEnumerable<WorkingTreeTreeNode> nodes)
    {
        var count = 0;
        foreach (var node in nodes)
        {
            count++;
            if (node.IsExpanded) count += CountVisibleWorkingTreeNodes(node.Children);
        }
        return count;
    }

    private static void ReplaceRoots(
        BulkObservableCollection<WorkingTreeTreeNode> target,
        IReadOnlyList<WorkingTreeTreeNode> source) =>
        target.ReplaceAll(source);

    private void UnstagedChangesTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args) =>
        WorkingTreeNodeInvoked(ResolveWorkingTreeNode(args.InvokedItem), WorkingTreeDiffKind.Unstaged);

    private void StagedChangesTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args) =>
        WorkingTreeNodeInvoked(ResolveWorkingTreeNode(args.InvokedItem), WorkingTreeDiffKind.Staged);

    private void WorkingTreeNodeInvoked(WorkingTreeTreeNode? node, WorkingTreeDiffKind kind)
    {
        if (node?.Change is null) return;

        var roots = kind == WorkingTreeDiffKind.Unstaged ? _unstagedTreeRoots : _stagedTreeRoots;
        var selection = kind == WorkingTreeDiffKind.Unstaged ? _unstagedTreeSelection : _stagedTreeSelection;
        var selectedNodes = selection.Apply(node, roots, IsControlDown(), IsShiftDown());
        _viewModel.WorkingTree.SetSelection(kind, selectedNodes.Select(selected => selected.Change!));

        if (selection.IsSelected(node.Path))
        {
            SelectWorkingTreeChange(node.Change, kind);
            return;
        }

        var workingTree = _viewModel.WorkingTree;
        if (workingTree.SelectedDiffKind != kind ||
            workingTree.SelectedChange is not { } active ||
            !SameWorkingTreeChange(active, node.Change))
            return;

        if (selectedNodes.LastOrDefault() is { Change: { } fallback })
        {
            SelectWorkingTreeChange(fallback, kind);
            return;
        }

        ClearActiveWorkingTreeChange();
    }

    internal static WorkingTreeTreeNode? ResolveWorkingTreeNode(object? value) => value switch
    {
        WorkingTreeTreeNode node => node,
        TreeViewNode { Content: WorkingTreeTreeNode node } => node,
        TreeViewItem { DataContext: WorkingTreeTreeNode node } => node,
        FrameworkElement { DataContext: WorkingTreeTreeNode node } => node,
        _ => null
    };

    private static bool IsShiftDown() =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;

    internal IReadOnlyList<WorkingTreeTreeNode> UnstagedTreeRoots => _unstagedTreeRoots;
    internal IReadOnlyList<WorkingTreeTreeNode> StagedTreeRoots => _stagedTreeRoots;
    internal void InvokeWorkingTreeNodeForCheck(WorkingTreeTreeNode node, WorkingTreeDiffKind kind) =>
        WorkingTreeNodeInvoked(node, kind);

    internal void SelectWorkingTreeChange(WorkingTreeChange change, WorkingTreeDiffKind kind)
    {
        _desiredWorkingTreePath = change.Path;
        UpdateWorkingTreeDiffHeader(change, kind);
        RunPageUiOperation(
            () => _viewModel.WorkingTree.SelectChangeAsync(change, kind),
            "Working-tree file selection");
    }

    private void WorkingTreeViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (IsShuttingDown) return;
        var workingTree = _viewModel.WorkingTree;
        if (args.PropertyName is nameof(WorkingTreeViewModel.SelectedChange)
            or nameof(WorkingTreeViewModel.SelectedDiffKind))
        {
            _workingTreeImageDiff.Cancel();
            if (workingTree.SelectedChange is { } change && workingTree.SelectedDiffKind is { } kind)
                UpdateWorkingTreeDiffHeader(change, kind);
            else
                ClearWorkingTreeDiffViewer(clearSelectionKind: false);
            return;
        }

        if (args.PropertyName == nameof(WorkingTreeViewModel.IsDiffLoading))
        {
            if (workingTree.IsDiffLoading && workingTree.SelectedDiff is null)
                _diffPresentation.RenderWorkingTree(DiffPresentationState.LoadingDiff);
            return;
        }

        if (args.PropertyName == nameof(WorkingTreeViewModel.IsDiffPreviewDeferred))
        {
            if (workingTree.IsDiffPreviewDeferred)
            {
                _workingTreeImageDiff.Cancel();
                WorkingTreeDiffViewer.Clear();
                _diffPresentation.RenderWorkingTree(DiffPresentationState.LargeDiff);
            }
            return;
        }

        if (args.PropertyName == nameof(WorkingTreeViewModel.DiffLoadErrorMessage))
        {
            if (!string.IsNullOrWhiteSpace(workingTree.DiffLoadErrorMessage))
            {
                _workingTreeImageDiff.Cancel();
                WorkingTreeDiffViewer.Clear();
                _diffPresentation.RenderWorkingTree(DiffPresentationState.Error);
                RunPageUiOperation(
                    () => ShowErrorAsync("Could not read working tree diff", workingTree.DiffLoadErrorMessage),
                    "Working-tree diff error");
            }
            return;
        }

        if (args.PropertyName == nameof(WorkingTreeViewModel.SelectedDiff))
            RenderWorkingTreeDiff();
    }

    private void RenderWorkingTreeDiff()
    {
        var workingTree = _viewModel.WorkingTree;
        var change = workingTree.SelectedChange;
        var kind = workingTree.SelectedDiffKind;
        if (change is null || kind is null)
        {
            ClearWorkingTreeDiffViewer(clearSelectionKind: false);
            return;
        }

        UpdateWorkingTreeDiffHeader(change, kind.Value);

        if (workingTree.IsDiffPreviewDeferred)
        {
            WorkingTreeDiffViewer.Clear();
            _diffPresentation.RenderWorkingTree(DiffPresentationState.LargeDiff);
            return;
        }

        if (!string.IsNullOrWhiteSpace(workingTree.DiffLoadErrorMessage))
        {
            WorkingTreeDiffViewer.Clear();
            _diffPresentation.RenderWorkingTree(DiffPresentationState.Error);
            return;
        }

        if (workingTree.SelectedDiff is not { } diff)
        {
            WorkingTreeDiffViewer.Clear();
            _diffPresentation.RenderWorkingTree(
                workingTree.HasCurrentDelta(change.Path, kind.Value)
                    ? DiffPresentationState.LoadingDiff
                    : DiffPresentationState.DeltaMissing);
            return;
        }

        if (diff.IsBinary)
        {
            WorkingTreeDiffViewer.Clear();
            RunPageUiOperation(
                () => _workingTreeImageDiff.LoadAsync(change, kind.Value),
                "Working-tree image diff");
            return;
        }

        _workingTreeImageDiff.Cancel();
        var compactLines = CompactDiffLine.Build(diff.Lines);
        switch (DiffPresentationResolver.Resolve(diff, compactLines.Count))
        {
            case DiffContentPresentation.NoTextualPatch:
                WorkingTreeDiffViewer.Clear();
                _diffPresentation.RenderWorkingTree(
                    workingTree.HasCurrentDelta(change.Path, kind.Value)
                        ? DiffPresentationState.NoTextualPatch
                        : DiffPresentationState.DeltaMissing);
                return;
            case DiffContentPresentation.Text:
                WorkingTreeDiffViewer.SetLines(compactLines, diff.Diagnostics);
                _diffPresentation.RenderWorkingTree(DiffPresentationState.Text);
                return;
            case DiffContentPresentation.Binary:
                throw new InvalidOperationException("Binary diff should have been handled before text presentation.");
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void WorkingTreeLargeDiffButton_Click(object sender, RoutedEventArgs args) =>
        RunPageUiOperation(
            () => _viewModel.WorkingTree.LoadSelectedDiffAnywayAsync(),
            "Working-tree large diff");

    private void QueueWorkingTreePreviewRestore()
    {
        if (IsShuttingDown || _workingTreePreviewRestoreQueued) return;
        _workingTreePreviewRestoreQueued = true;
        if (DispatcherQueue.TryEnqueue(() =>
        {
            _workingTreePreviewRestoreQueued = false;
            if (!IsShuttingDown)
                EnsureWorkingTreeActivePreview();
        })) return;

        _workingTreePreviewRestoreQueued = false;
        if (!IsShuttingDown && DispatcherQueue.HasThreadAccess)
            EnsureWorkingTreeActivePreview();
    }

    private void EnsureWorkingTreeActivePreview()
    {
        if (IsShuttingDown || WorkingTreePane.Visibility != Visibility.Visible) return;
        if (_workingTreeTreeRefreshQueued && !_workingTreeSelectionSync)
        {
            _workingTreeTreeRefreshQueued = false;
            RebuildWorkingTreeTrees();
        }

        var workingTree = _viewModel.WorkingTree;
        if (workingTree.SelectedChange is not null && workingTree.SelectedDiffKind is not null)
        {
            RestoreWorkingTreeSelection();
            return;
        }

        if (WorkingTreeTreeSelection.GetLeaves(_unstagedTreeRoots).FirstOrDefault() is { Change: { } unstaged } unstagedNode)
        {
            _unstagedTreeSelection.SelectSingle(unstagedNode, _unstagedTreeRoots);
            workingTree.SetSelection(WorkingTreeDiffKind.Unstaged, [unstaged]);
            SelectWorkingTreeChange(unstaged, WorkingTreeDiffKind.Unstaged);
            return;
        }

        if (WorkingTreeTreeSelection.GetLeaves(_stagedTreeRoots).FirstOrDefault() is { Change: { } staged } stagedNode)
        {
            _stagedTreeSelection.SelectSingle(stagedNode, _stagedTreeRoots);
            workingTree.SetSelection(WorkingTreeDiffKind.Staged, [staged]);
            SelectWorkingTreeChange(staged, WorkingTreeDiffKind.Staged);
            return;
        }

        ClearActiveWorkingTreeChange();
    }

    private void RestoreWorkingTreeSelection()
    {
        var workingTree = _viewModel.WorkingTree;
        if (WorkingTreePane.Visibility != Visibility.Visible ||
            workingTree.SelectedChange is not { } active ||
            workingTree.SelectedDiffKind is not { } kind)
            return;

        _desiredWorkingTreePath ??= active.Path;
        var target = FindWorkingTreeLeaf(
            kind == WorkingTreeDiffKind.Unstaged ? _unstagedTreeRoots : _stagedTreeRoots,
            _desiredWorkingTreePath);

        if (target is null && kind == WorkingTreeDiffKind.Unstaged)
        {
            target = FindWorkingTreeLeaf(_stagedTreeRoots, _desiredWorkingTreePath);
            if (target is not null) kind = WorkingTreeDiffKind.Staged;
        }
        else if (target is null && kind == WorkingTreeDiffKind.Staged)
        {
            target = FindWorkingTreeLeaf(_unstagedTreeRoots, _desiredWorkingTreePath);
            if (target is not null) kind = WorkingTreeDiffKind.Unstaged;
        }

        if (target?.Change is null)
        {
            ClearActiveWorkingTreeChange(showMissingDelta: true);
            return;
        }

        var movedBetweenTrees = kind != workingTree.SelectedDiffKind;
        SelectWorkingTreeChange(target.Change, kind);
        if (movedBetweenTrees)
        {
            var targetPath = target.Path;
            var repository = _viewModel.Repository;
            var tree = kind == WorkingTreeDiffKind.Unstaged
                ? UnstagedChangesTree : StagedChangesTree;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (IsShuttingDown || !ReferenceEquals(repository, _viewModel.Repository)
                    || !string.Equals(targetPath, _desiredWorkingTreePath, StringComparison.Ordinal))
                    return;

                tree.ScrollIntoView(target);
            });
        }
    }

    private static WorkingTreeTreeNode? FindWorkingTreeLeaf(
        IEnumerable<WorkingTreeTreeNode> nodes,
        string path)
    {
        foreach (var node in nodes)
        {
            if (node.Change is not null && string.Equals(node.Path, path, StringComparison.Ordinal)) return node;
            if (FindWorkingTreeLeaf(node.Children, path) is { } match) return match;
        }

        return null;
    }

    internal static bool SameWorkingTreeChange(WorkingTreeChange left, WorkingTreeChange right) =>
        string.Equals(left.Path, right.Path, StringComparison.Ordinal) &&
        left.IndexStatus == right.IndexStatus &&
        left.WorkingTreeStatus == right.WorkingTreeStatus &&
        string.Equals(left.OriginalPath, right.OriginalPath, StringComparison.Ordinal);

    private void ClearWorkingTreeSelection()
    {
        _unstagedTreeSelection.Clear(_unstagedTreeRoots);
        _stagedTreeSelection.Clear(_stagedTreeRoots);
        _viewModel.WorkingTree.SetSelection(WorkingTreeDiffKind.Unstaged, []);
        _viewModel.WorkingTree.SetSelection(WorkingTreeDiffKind.Staged, []);
        ClearActiveWorkingTreeChange();
    }

    private void ClearActiveWorkingTreeChange(bool showMissingDelta = false)
    {
        var previousPath = _desiredWorkingTreePath ?? _viewModel.WorkingTree.SelectedChange?.Path;
        _desiredWorkingTreePath = null;
        _viewModel.WorkingTree.ClearActiveSelection();
        ClearWorkingTreeDiffViewer(
            clearSelectionKind: false,
            showMissingDelta ? DiffPresentationState.DeltaMissing : DiffPresentationState.NothingSelected);
        if (showMissingDelta && previousPath is not null)
            WorkingTreeDiffHeader.Text = previousPath;
    }

    private void CancelWorkingTreeDiff(bool clearViewer)
    {
        _viewModel.WorkingTree.CancelDiff();
        _workingTreeImageDiff.Cancel();
        if (clearViewer) ClearWorkingTreeDiffViewer(clearSelectionKind: false);
    }

    private void ClearWorkingTreeDiffViewer(
        bool clearSelectionKind,
        DiffPresentationState state = DiffPresentationState.NothingSelected)
    {
        _workingTreeImageDiff.Cancel();
        WorkingTreeDiffViewer.Clear();
        _diffPresentation.RenderWorkingTree(state);
        WorkingTreeDiffHeader.Text = string.Empty;
        WorkingTreeDiffKindText.Text = string.Empty;
        if (clearSelectionKind)
            _viewModel.WorkingTree.ClearActiveSelection();
    }

    private void UpdateWorkingTreeDiffHeader(WorkingTreeChange change, WorkingTreeDiffKind kind)
    {
        WorkingTreeDiffHeader.Text = BuildWorkingTreeDiffHeader(change, kind);
        WorkingTreeDiffKindText.Text = kind.ToString().ToUpperInvariant();
    }

    private static string BuildWorkingTreeDiffHeader(WorkingTreeChange change, WorkingTreeDiffKind kind)
    {
        var status = kind == WorkingTreeDiffKind.Staged ? change.IndexStatus : change.WorkingTreeStatus;
        var path = change.OriginalPath is null
            ? change.Path
            : $"{change.OriginalPath} → {change.Path}";
        return $"{status}  {path}";
    }
}
