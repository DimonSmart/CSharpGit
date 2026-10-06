using System.Collections.Specialized;
using System.ComponentModel;
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
    private readonly BulkObservableCollection<WorkingTreeTreeNode> _unstagedTreeRoots = [];
    private readonly BulkObservableCollection<WorkingTreeTreeNode> _stagedTreeRoots = [];
    private readonly WorkingTreeTreeSelection _unstagedTreeSelection = new();
    private readonly WorkingTreeTreeSelection _stagedTreeSelection = new();
    private readonly Dictionary<string, bool> _unstagedExpansionState = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _stagedExpansionState = new(StringComparer.Ordinal);
    private CancellationTokenSource? _workingTreeImageDiffCts;
    private long _workingTreeImageDiffGeneration;
    private bool _workingTreeSelectionSync;
    private bool _workingTreeTreeRefreshQueued;
    private bool _workingTreeTreeRefreshScheduled;
    private bool _workingTreePreviewRestoreQueued;
    private string? _workingTreeExpansionRepositoryIdentity;
    private string? _desiredWorkingTreePath;

    private TreeView UnstagedChangesList => UnstagedChangesTree;
    private TreeView StagedChangesList => StagedChangesTree;

    private void InitializeWorkingTreeDiffSurface()
    {
        UnstagedChangesTree.ItemsSource = _unstagedTreeRoots;
        StagedChangesTree.ItemsSource = _stagedTreeRoots;

        _unstagedChanges.CollectionChanged += WorkingTreePresentationSourceChanged;
        _stagedChanges.CollectionChanged += WorkingTreePresentationSourceChanged;
        _viewModel.WorkingTree.PropertyChanged += WorkingTreeViewModel_PropertyChanged;
        WorkingTreePane.RegisterPropertyChangedCallback(VisibilityProperty, (_, _) =>
        {
            if (WorkingTreePane.Visibility == Visibility.Visible)
                EnsureWorkingTreeActivePreview();
            else
                CancelWorkingTreeImageDiff();
        });

        RebuildWorkingTreeTrees();
        ClearWorkingTreeDiffViewer(clearSelectionKind: true);
    }

    private void WorkingTreePresentationSourceChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (_workingTreeTreeRefreshQueued) return;
        _workingTreeTreeRefreshQueued = true;
        ScheduleWorkingTreeTreeRefreshIfNeeded();
    }

    private void ScheduleWorkingTreeTreeRefreshIfNeeded()
    {
        if (!_workingTreeTreeRefreshQueued ||
            _workingTreeTreeRefreshScheduled ||
            _workingTreeSelectionSync)
            return;

        _workingTreeTreeRefreshScheduled = true;
        if (DispatcherQueue.TryEnqueue(() =>
        {
            _workingTreeTreeRefreshScheduled = false;
            if (!_workingTreeTreeRefreshQueued) return;
            _workingTreeTreeRefreshQueued = false;
            RebuildWorkingTreeTrees();
        })) return;

        _workingTreeTreeRefreshScheduled = false;
        _workingTreeTreeRefreshQueued = false;
        RebuildWorkingTreeTrees();
    }

    private void RebuildWorkingTreeTrees()
    {
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
        _desiredWorkingTreePath = change.Path;
        UpdateWorkingTreeDiffHeader(change, kind);
        _ = _viewModel.WorkingTree.SelectChangeAsync(change, kind);
    }

    private void WorkingTreeViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        var workingTree = _viewModel.WorkingTree;
        if (args.PropertyName is nameof(WorkingTreeViewModel.SelectedChange)
            or nameof(WorkingTreeViewModel.SelectedDiffKind))
        {
            CancelWorkingTreeImageDiff();
            if (workingTree.SelectedChange is { } change && workingTree.SelectedDiffKind is { } kind)
                UpdateWorkingTreeDiffHeader(change, kind);
            else
                ClearWorkingTreeDiffViewer(clearSelectionKind: false);
            return;
        }

        if (args.PropertyName == nameof(WorkingTreeViewModel.IsDiffLoading))
        {
            if (workingTree.IsDiffLoading && workingTree.SelectedDiff is null)
                SetWorkingTreeDiffPresentationState(DiffPresentationState.LoadingDiff);
            return;
        }

        if (args.PropertyName == nameof(WorkingTreeViewModel.IsDiffPreviewDeferred))
        {
            if (workingTree.IsDiffPreviewDeferred)
            {
                CancelWorkingTreeImageDiff();
                WorkingTreeDiffViewer.Clear();
                SetWorkingTreeDiffPresentationState(DiffPresentationState.LargeDiff);
            }
            return;
        }

        if (args.PropertyName == nameof(WorkingTreeViewModel.DiffLoadErrorMessage))
        {
            if (!string.IsNullOrWhiteSpace(workingTree.DiffLoadErrorMessage))
            {
                CancelWorkingTreeImageDiff();
                WorkingTreeDiffViewer.Clear();
                SetWorkingTreeDiffPresentationState(DiffPresentationState.Error);
                _ = ShowErrorAsync("Could not read working tree diff", workingTree.DiffLoadErrorMessage);
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
            SetWorkingTreeDiffPresentationState(DiffPresentationState.LargeDiff);
            return;
        }

        if (!string.IsNullOrWhiteSpace(workingTree.DiffLoadErrorMessage))
        {
            WorkingTreeDiffViewer.Clear();
            SetWorkingTreeDiffPresentationState(DiffPresentationState.Error);
            return;
        }

        if (workingTree.SelectedDiff is not { } diff)
        {
            WorkingTreeDiffViewer.Clear();
            SetWorkingTreeDiffPresentationState(
                workingTree.HasCurrentDelta(change.Path, kind.Value)
                    ? DiffPresentationState.LoadingDiff
                    : DiffPresentationState.DeltaMissing);
            return;
        }

        if (diff.IsBinary)
        {
            WorkingTreeDiffViewer.Clear();
            _ = LoadWorkingTreeImageDiffAsync(change, kind.Value);
            return;
        }

        CancelWorkingTreeImageDiff();
        var compactLines = CompactDiffLine.Build(diff.Lines);
        switch (DiffPresentationResolver.Resolve(diff, compactLines.Count))
        {
            case DiffContentPresentation.NoTextualPatch:
                WorkingTreeDiffViewer.Clear();
                SetWorkingTreeDiffPresentationState(
                    workingTree.HasCurrentDelta(change.Path, kind.Value)
                        ? DiffPresentationState.NoTextualPatch
                        : DiffPresentationState.DeltaMissing);
                return;
            case DiffContentPresentation.Text:
                WorkingTreeDiffViewer.SetLines(compactLines, diff.Diagnostics);
                SetWorkingTreeDiffPresentationState(DiffPresentationState.Text);
                return;
            case DiffContentPresentation.Binary:
                throw new InvalidOperationException("Binary diff should have been handled before text presentation.");
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private async Task LoadWorkingTreeImageDiffAsync(
        WorkingTreeChange change,
        WorkingTreeDiffKind kind)
    {
        CancelWorkingTreeImageDiff();
        var repository = _viewModel.Repository;
        if (repository is null) return;

        var cancellation = new CancellationTokenSource();
        _workingTreeImageDiffCts = cancellation;
        var generation = ++_workingTreeImageDiffGeneration;
        SetWorkingTreeDiffPresentationState(DiffPresentationState.LoadingImage);

        try
        {
            var imageResult = await ImageDiffService.LoadWorkingTreeAsync(
                repository,
                change,
                kind,
                cancellation.Token);
            if (!IsCurrentWorkingTreeImageRequest(repository, change, kind, generation, cancellation.Token))
                return;

            _workingTreeFileVersions = imageResult.Versions;
            _workingTreeRevealPath = TryResolveReveal(repository, imageResult.Versions.RevealPath);
            UpdateWorkingTreeButtons();

            if (imageResult.Content is null)
            {
                SetWorkingTreeDiffPresentationState(DiffPresentationState.OtherBinary);
                return;
            }

            WorkingTreeImageDiffHost.Show(imageResult.Content);
            SetWorkingTreeDiffPresentationState(DiffPresentationState.Image);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (IsCurrentWorkingTreeImageRequest(repository, change, kind, generation, CancellationToken.None))
                SetWorkingTreeDiffPresentationState(DiffPresentationState.Unavailable);
        }
        finally
        {
            if (ReferenceEquals(_workingTreeImageDiffCts, cancellation))
            {
                _workingTreeImageDiffCts = null;
                cancellation.Dispose();
            }
        }
    }

    private bool IsCurrentWorkingTreeImageRequest(
        Repository repository,
        WorkingTreeChange change,
        WorkingTreeDiffKind kind,
        long generation,
        CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested &&
        generation == _workingTreeImageDiffGeneration &&
        ReferenceEquals(repository, _viewModel.Repository) &&
        _viewModel.WorkingTree.IsActive(change, kind);

    private void WorkingTreeLargeDiffButton_Click(object sender, RoutedEventArgs args) =>
        _ = _viewModel.WorkingTree.LoadSelectedDiffAnywayAsync();

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
        CancelWorkingTreeImageDiff();
        if (clearViewer) ClearWorkingTreeDiffViewer(clearSelectionKind: false);
    }

    private void CancelWorkingTreeImageDiff()
    {
        _workingTreeImageDiffGeneration++;
        var cancellation = Interlocked.Exchange(ref _workingTreeImageDiffCts, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private void ClearWorkingTreeDiffViewer(
        bool clearSelectionKind,
        DiffPresentationState state = DiffPresentationState.NothingSelected)
    {
        CancelWorkingTreeImageDiff();
        WorkingTreeDiffViewer.Clear();
        SetWorkingTreeDiffPresentationState(state);
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

internal static class WorkingTreeTreeViewExtensions
{
    public static void ScrollIntoView(this TreeView tree, object item)
    {
        object? treeItem = item;
        if (item is WorkingTreeChange change && tree.ItemsSource is IEnumerable<WorkingTreeTreeNode> roots)
            treeItem = FindLeaf(roots, change.Path);

        if (treeItem is not null && tree.ContainerFromItem(treeItem) is FrameworkElement container)
            container.StartBringIntoView();
    }

    private static WorkingTreeTreeNode? FindLeaf(IEnumerable<WorkingTreeTreeNode> nodes, string path)
    {
        foreach (var node in nodes)
        {
            if (node.Change is not null && string.Equals(node.Path, path, StringComparison.Ordinal)) return node;
            if (FindLeaf(node.Children, path) is { } match) return match;
        }

        return null;
    }
}
