using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;
using Windows.UI.Core;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private readonly ObservableCollection<CompactDiffLine> _workingTreeCompactDiffLines = [];
    private readonly ObservableCollection<WorkingTreeTreeNode> _unstagedTreeRoots = [];
    private readonly ObservableCollection<WorkingTreeTreeNode> _stagedTreeRoots = [];
    private readonly WorkingTreeTreeSelection _unstagedTreeSelection = new();
    private readonly WorkingTreeTreeSelection _stagedTreeSelection = new();
    private readonly Dictionary<string, bool> _unstagedExpansionState = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _stagedExpansionState = new(StringComparer.Ordinal);
    private IWorkingTreeDiffService? _workingTreeDiffService;
    private CancellationTokenSource? _workingTreeDiffCts;
    private long _workingTreeDiffGeneration;
    private bool _workingTreeSelectionSync;
    private bool _workingTreeTreeRefreshQueued;
    private bool _workingTreePreviewRestoreQueued;
    private string? _workingTreeExpansionRepositoryIdentity;
    private string? _desiredWorkingTreePath;

    // Compatibility aliases keep the remaining MainPage partial independent of the control migration.
    private TreeView UnstagedChangesList => UnstagedChangesTree;
    private TreeView StagedChangesList => StagedChangesTree;

    private void InitializeWorkingTreeDiffSurface()
    {
        WorkingTreeCompactDiffList.ItemsSource = _workingTreeCompactDiffLines;
        UnstagedChangesTree.ItemsSource = _unstagedTreeRoots;
        StagedChangesTree.ItemsSource = _stagedTreeRoots;

        _unstagedChanges.CollectionChanged += WorkingTreePresentationSourceChanged;
        _stagedChanges.CollectionChanged += WorkingTreePresentationSourceChanged;
        _viewModel.Changes.CollectionChanged += WorkingTreeChangesCollectionChanged;
        WorkingTreePane.RegisterPropertyChangedCallback(VisibilityProperty, (_, _) =>
        {
            if (WorkingTreePane.Visibility == Visibility.Visible) QueueWorkingTreePreviewRestore();
        });

        RebuildWorkingTreeTrees();
        ClearWorkingTreeDiffViewer(clearSelectionKind: true);
    }

    private void WorkingTreePresentationSourceChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (_workingTreeTreeRefreshQueued) return;
        _workingTreeTreeRefreshQueued = true;
        if (DispatcherQueue.TryEnqueue(() =>
        {
            _workingTreeTreeRefreshQueued = false;
            RebuildWorkingTreeTrees();
        })) return;

        _workingTreeTreeRefreshQueued = false;
        if (!_workingTreeSelectionSync) RebuildWorkingTreeTrees();
    }

    private void RebuildWorkingTreeTrees()
    {
        var repositoryIdentity = _viewModel.Repository?.GitDirectory;
        if (string.Equals(repositoryIdentity, _workingTreeExpansionRepositoryIdentity, StringComparison.Ordinal))
        {
            CaptureExpansionState(_unstagedTreeRoots, _unstagedExpansionState);
            CaptureExpansionState(_stagedTreeRoots, _stagedExpansionState);
        }
        else
        {
            _workingTreeExpansionRepositoryIdentity = repositoryIdentity;
            _unstagedExpansionState.Clear();
            _stagedExpansionState.Clear();
        }

        ReplaceRoots(_unstagedTreeRoots, WorkingTreeTreeNode.Build(_unstagedChanges, WorkingTreeDiffKind.Unstaged));
        ReplaceRoots(_stagedTreeRoots, WorkingTreeTreeNode.Build(_stagedChanges, WorkingTreeDiffKind.Staged));
        RestoreExpansionState(_unstagedTreeRoots, _unstagedExpansionState);
        RestoreExpansionState(_stagedTreeRoots, _stagedExpansionState);

        _unstagedTreeSelection.SetSelectedPaths(
            _viewModel.SelectedUnstagedChanges.Select(change => change.Path),
            _unstagedTreeRoots);
        _stagedTreeSelection.SetSelectedPaths(
            _viewModel.SelectedStagedChanges.Select(change => change.Path),
            _stagedTreeRoots);

        if (WorkingTreePane.Visibility == Visibility.Visible) QueueWorkingTreePreviewRestore();
    }

    private static void ReplaceRoots(
        ObservableCollection<WorkingTreeTreeNode> target,
        IReadOnlyList<WorkingTreeTreeNode> source)
    {
        target.Clear();
        foreach (var node in source) target.Add(node);
    }

    private static void CaptureExpansionState(
        IEnumerable<WorkingTreeTreeNode> nodes,
        IDictionary<string, bool> state)
    {
        foreach (var node in nodes)
        {
            if (node.IsFolder) state[node.Path] = node.IsExpanded;
            CaptureExpansionState(node.Children, state);
        }
    }

    private static void RestoreExpansionState(
        IEnumerable<WorkingTreeTreeNode> nodes,
        IReadOnlyDictionary<string, bool> state)
    {
        foreach (var node in nodes)
        {
            if (node.IsFolder && state.TryGetValue(node.Path, out var expanded)) node.IsExpanded = expanded;
            RestoreExpansionState(node.Children, state);
        }
    }

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
        _viewModel.SetWorkingTreeSelection(kind, selectedNodes.Select(selected => selected.Change!));

        if (selection.IsSelected(node.Path))
        {
            SelectWorkingTreeChange(node.Change, kind);
            return;
        }

        if (_viewModel.ActiveWorkingTreeDiffKind != kind ||
            _viewModel.ActiveWorkingTreeChange is not { } active ||
            !SameWorkingTreeChange(active, node.Change))
            return;

        if (selectedNodes.LastOrDefault() is { Change: { } fallback })
        {
            SelectWorkingTreeChange(fallback, kind);
            return;
        }

        ClearActiveWorkingTreeChange();
    }

    private static WorkingTreeTreeNode? ResolveWorkingTreeNode(object? value) => value switch
    {
        WorkingTreeTreeNode node => node,
        TreeViewNode { Content: WorkingTreeTreeNode node } => node,
        TreeViewItem { DataContext: WorkingTreeTreeNode node } => node,
        FrameworkElement { DataContext: WorkingTreeTreeNode node } => node,
        _ => null
    };

    private static bool IsShiftDown() =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;

    private void SelectWorkingTreeChange(WorkingTreeChange change, WorkingTreeDiffKind kind)
    {
        _viewModel.ActiveWorkingTreeChange = change;
        _viewModel.ActiveWorkingTreeDiffKind = kind;
        _desiredWorkingTreePath = change.Path;
        _ = LoadWorkingTreeDiffAsync(change, kind);
    }

    private async Task LoadWorkingTreeDiffAsync(WorkingTreeChange change, WorkingTreeDiffKind kind)
    {
        CancelWorkingTreeDiff(clearViewer: true);
        _viewModel.ActiveWorkingTreeDiffKind = kind;

        var repository = _viewModel.Repository;
        var service = _workingTreeDiffService;
        if (repository is null || service is null) return;

        WorkingTreeDiffHeader.Text = BuildWorkingTreeDiffHeader(change, kind);
        WorkingTreeDiffKindText.Text = kind.ToString().ToUpperInvariant();

        var cts = new CancellationTokenSource();
        _workingTreeDiffCts = cts;
        var generation = _workingTreeDiffGeneration;
        try
        {
            var diff = await service.ReadDiffAsync(repository, change, kind, cts.Token);
            if (!IsCurrentWorkingTreeDiffRequest(repository, change, kind, generation, cts.Token))
                return;

            _viewModel.SelectedWorkingTreeDiff = diff;
            if (diff.IsBinary)
            {
                WorkingTreeBinaryInfo.Visibility = Visibility.Visible;
                return;
            }

            var compactLines = CompactDiffLine.Build(diff.Lines);
            if (compactLines.Count == 0)
            {
                WorkingTreeNoChangesInfo.Visibility = Visibility.Visible;
                return;
            }

            foreach (var line in compactLines) _workingTreeCompactDiffLines.Add(line);
            WorkingTreeCompactDiffList.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (IsCurrentWorkingTreeDiffRequest(repository, change, kind, generation, CancellationToken.None))
            {
                ClearWorkingTreeDiffViewer(clearSelectionKind: false);
                await ShowErrorAsync("Could not read working tree diff", exception.Message);
            }
        }
        finally
        {
            if (ReferenceEquals(_workingTreeDiffCts, cts))
            {
                _workingTreeDiffCts.Dispose();
                _workingTreeDiffCts = null;
            }
        }
    }

    private bool IsCurrentWorkingTreeDiffRequest(
        Repository repository,
        WorkingTreeChange change,
        WorkingTreeDiffKind kind,
        long generation,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested ||
            generation != _workingTreeDiffGeneration ||
            WorkingTreePane.Visibility != Visibility.Visible ||
            !ReferenceEquals(repository, _viewModel.Repository) ||
            _viewModel.ActiveWorkingTreeDiffKind != kind ||
            _viewModel.ActiveWorkingTreeChange is not { } selected)
            return false;

        return SameWorkingTreeChange(selected, change);
    }

    private void WorkingTreeChangesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) =>
        CancelWorkingTreeDiff(clearViewer: true);

    private void QueueWorkingTreePreviewRestore()
    {
        if (_workingTreePreviewRestoreQueued) return;
        _workingTreePreviewRestoreQueued = true;
        if (DispatcherQueue.TryEnqueue(() =>
        {
            _workingTreePreviewRestoreQueued = false;
            EnsureWorkingTreeActivePreview();
        })) return;

        _workingTreePreviewRestoreQueued = false;
        EnsureWorkingTreeActivePreview();
    }

    private void EnsureWorkingTreeActivePreview()
    {
        if (WorkingTreePane.Visibility != Visibility.Visible) return;
        if (_viewModel.ActiveWorkingTreeChange is not null && _viewModel.ActiveWorkingTreeDiffKind is not null)
        {
            RestoreWorkingTreeSelection();
            return;
        }

        if (WorkingTreeTreeSelection.GetLeaves(_unstagedTreeRoots).FirstOrDefault() is { Change: { } unstaged } unstagedNode)
        {
            _unstagedTreeSelection.SelectSingle(unstagedNode, _unstagedTreeRoots);
            _viewModel.SetWorkingTreeSelection(WorkingTreeDiffKind.Unstaged, [unstaged]);
            SelectWorkingTreeChange(unstaged, WorkingTreeDiffKind.Unstaged);
            return;
        }

        if (WorkingTreeTreeSelection.GetLeaves(_stagedTreeRoots).FirstOrDefault() is { Change: { } staged } stagedNode)
        {
            _stagedTreeSelection.SelectSingle(stagedNode, _stagedTreeRoots);
            _viewModel.SetWorkingTreeSelection(WorkingTreeDiffKind.Staged, [staged]);
            SelectWorkingTreeChange(staged, WorkingTreeDiffKind.Staged);
            return;
        }

        ClearActiveWorkingTreeChange();
    }

    private void RestoreWorkingTreeSelection()
    {
        if (WorkingTreePane.Visibility != Visibility.Visible ||
            _viewModel.ActiveWorkingTreeChange is not { } active ||
            _viewModel.ActiveWorkingTreeDiffKind is not { } kind)
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
            ClearActiveWorkingTreeChange();
            return;
        }

        SelectWorkingTreeChange(target.Change, kind);
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

    private static bool SameWorkingTreeChange(WorkingTreeChange left, WorkingTreeChange right) =>
        string.Equals(left.Path, right.Path, StringComparison.Ordinal) &&
        left.IndexStatus == right.IndexStatus &&
        left.WorkingTreeStatus == right.WorkingTreeStatus &&
        string.Equals(left.OriginalPath, right.OriginalPath, StringComparison.Ordinal);

    private void ClearWorkingTreeSelection()
    {
        _unstagedTreeSelection.Clear(_unstagedTreeRoots);
        _stagedTreeSelection.Clear(_stagedTreeRoots);
        _viewModel.SetWorkingTreeSelection(WorkingTreeDiffKind.Unstaged, []);
        _viewModel.SetWorkingTreeSelection(WorkingTreeDiffKind.Staged, []);
        ClearActiveWorkingTreeChange();
    }

    private void ClearActiveWorkingTreeChange()
    {
        _desiredWorkingTreePath = null;
        _viewModel.ActiveWorkingTreeChange = null;
        ClearWorkingTreeDiffViewer(clearSelectionKind: true);
    }

    private void CancelWorkingTreeDiff(bool clearViewer)
    {
        _workingTreeDiffGeneration++;
        _workingTreeDiffCts?.Cancel();
        _workingTreeDiffCts?.Dispose();
        _workingTreeDiffCts = null;
        _viewModel.SelectedWorkingTreeDiff = null;
        if (clearViewer) ClearWorkingTreeDiffViewer(clearSelectionKind: false);
    }

    private void ClearWorkingTreeDiffViewer(bool clearSelectionKind)
    {
        _workingTreeCompactDiffLines.Clear();
        WorkingTreeCompactDiffList.Visibility = Visibility.Collapsed;
        WorkingTreeBinaryInfo.Visibility = Visibility.Collapsed;
        WorkingTreeNoChangesInfo.Visibility = Visibility.Collapsed;
        WorkingTreeDiffHeader.Text = string.Empty;
        WorkingTreeDiffKindText.Text = string.Empty;
        _viewModel.SelectedWorkingTreeDiff = null;
        if (clearSelectionKind) _viewModel.ActiveWorkingTreeDiffKind = null;
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
