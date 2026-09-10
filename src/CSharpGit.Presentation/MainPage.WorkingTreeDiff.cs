using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private readonly ObservableCollection<CompactDiffLine> _workingTreeCompactDiffLines = [];
    private IWorkingTreeDiffService? _workingTreeDiffService;
    private CancellationTokenSource? _workingTreeDiffCts;
    private long _workingTreeDiffGeneration;
    private bool _workingTreeSelectionSync;
    private bool _workingTreeSelectionRestoreQueued;
    private string? _desiredWorkingTreePath;

    private void InitializeWorkingTreeDiffSurface()
    {
        WorkingTreeCompactDiffList.ItemsSource = _workingTreeCompactDiffLines;
        WorkingTreeCompactDiffList.ItemContainerStyle = CompactResource<Style>("CompactDiffItemContainerStyle");
        WorkingTreeCompactDiffList.ItemTemplate = CompactResource<DataTemplate>("CompactDiffItemTemplate");

        UnstagedChangesList.SelectionChanged += WorkingTreeUnstagedSelectionChanged;
        StagedChangesList.SelectionChanged += WorkingTreeStagedSelectionChanged;
        _viewModel.Changes.CollectionChanged += WorkingTreeChangesCollectionChanged;

        ClearWorkingTreeDiffViewer(clearSelectionKind: true);
    }

    private void WorkingTreeUnstagedSelectionChanged(object sender, SelectionChangedEventArgs args) =>
        SynchronizeWorkingTreeSelection(UnstagedChangesList, WorkingTreeDiffKind.Unstaged, args);

    private void WorkingTreeStagedSelectionChanged(object sender, SelectionChangedEventArgs args) =>
        SynchronizeWorkingTreeSelection(StagedChangesList, WorkingTreeDiffKind.Staged, args);

    private void SynchronizeWorkingTreeSelection(ListView list, WorkingTreeDiffKind kind, SelectionChangedEventArgs args)
    {
        if (_workingTreeSelectionSync) return;

        var selected = list.SelectedItems.OfType<WorkingTreeChange>().ToArray();
        _viewModel.SetWorkingTreeSelection(kind, selected);

        if (args.AddedItems.OfType<WorkingTreeChange>().LastOrDefault() is { } activated)
        {
            SelectWorkingTreeChange(activated, kind);
            return;
        }

        if (_viewModel.ActiveWorkingTreeDiffKind != kind || _viewModel.ActiveWorkingTreeChange is not { } active)
            return;

        if (selected.Any(change => SameWorkingTreeChange(change, active))) return;

        if (selected.LastOrDefault() is { } fallback)
        {
            SelectWorkingTreeChange(fallback, kind);
            return;
        }

        QueueActiveSelectionValidation(list, kind, active);
    }

    private void QueueActiveSelectionValidation(ListView list, WorkingTreeDiffKind kind, WorkingTreeChange active)
    {
        void Validate()
        {
            if (_viewModel.ActiveWorkingTreeDiffKind != kind ||
                _viewModel.ActiveWorkingTreeChange is not { } current ||
                !SameWorkingTreeChange(current, active) ||
                list.SelectedItems.Count != 0)
                return;

            ClearActiveWorkingTreeChange();
        }

        if (DispatcherQueue.TryEnqueue(() =>
        {
            if (!DispatcherQueue.TryEnqueue(Validate)) Validate();
        })) return;

        Validate();
    }

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

    private void WorkingTreeChangesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        CancelWorkingTreeDiff(clearViewer: true);
        QueueWorkingTreeSelectionRestore();
    }

    private void QueueWorkingTreeSelectionRestore()
    {
        if (_workingTreeSelectionRestoreQueued) return;
        _workingTreeSelectionRestoreQueued = true;
        if (DispatcherQueue.TryEnqueue(() =>
        {
            _workingTreeSelectionRestoreQueued = false;
            RestoreWorkingTreeSelection();
        })) return;

        _workingTreeSelectionRestoreQueued = false;
        RestoreWorkingTreeSelection();
    }

    private void RestoreWorkingTreeSelection()
    {
        if (WorkingTreePane.Visibility != Visibility.Visible) return;
        if (_viewModel.ActiveWorkingTreeChange is null || _viewModel.ActiveWorkingTreeDiffKind is null)
        {
            ClearWorkingTreeSelection();
            return;
        }

        _desiredWorkingTreePath ??= _viewModel.ActiveWorkingTreeChange.Path;
        var kind = _viewModel.ActiveWorkingTreeDiffKind;
        WorkingTreeChange? target = kind switch
        {
            WorkingTreeDiffKind.Unstaged => _unstagedChanges.FirstOrDefault(MatchesDesiredPath),
            WorkingTreeDiffKind.Staged => _stagedChanges.FirstOrDefault(MatchesDesiredPath),
            _ => null
        };

        if (target is null && kind == WorkingTreeDiffKind.Unstaged)
        {
            target = _stagedChanges.FirstOrDefault(MatchesDesiredPath);
            if (target is not null) kind = WorkingTreeDiffKind.Staged;
        }
        else if (target is null && kind == WorkingTreeDiffKind.Staged)
        {
            target = _unstagedChanges.FirstOrDefault(MatchesDesiredPath);
            if (target is not null) kind = WorkingTreeDiffKind.Unstaged;
        }

        if (target is null || kind is null)
        {
            ClearWorkingTreeSelection();
            return;
        }

        _workingTreeSelectionSync = true;
        try
        {
            if (kind == WorkingTreeDiffKind.Unstaged) UnstagedChangesList.SelectedItem = target;
            else StagedChangesList.SelectedItem = target;
        }
        finally
        {
            _workingTreeSelectionSync = false;
        }

        _viewModel.SetWorkingTreeSelection(kind.Value, [target]);
        SelectWorkingTreeChange(target, kind.Value);
    }

    private bool MatchesDesiredPath(WorkingTreeChange change) =>
        string.Equals(change.Path, _desiredWorkingTreePath, StringComparison.Ordinal);

    private static bool SameWorkingTreeChange(WorkingTreeChange left, WorkingTreeChange right) =>
        string.Equals(left.Path, right.Path, StringComparison.Ordinal) &&
        left.IndexStatus == right.IndexStatus &&
        left.WorkingTreeStatus == right.WorkingTreeStatus &&
        string.Equals(left.OriginalPath, right.OriginalPath, StringComparison.Ordinal);

    private void ClearWorkingTreeSelection()
    {
        _workingTreeSelectionSync = true;
        try
        {
            UnstagedChangesList.SelectedItems.Clear();
            StagedChangesList.SelectedItems.Clear();
        }
        finally
        {
            _workingTreeSelectionSync = false;
        }

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
