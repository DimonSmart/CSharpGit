using System.Collections.Specialized;
using CSharpGit.Domain;
using CSharpGit.Presentation.Controls;
using CSharpGit.Presentation.Controls.CommitGraph;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private readonly CommitGraphLayoutState _historyGraphLayout = new();
    private bool _commitGraphLayoutInitialized;
    private bool _commitGraphLayoutApplyQueued;

    private CommitGraphLayoutState ActiveCommitGraphLayout => _historyGraphLayout;

    private void HistoryColumnHeader_Loaded(object sender, RoutedEventArgs args)
    {
        if (!_commitGraphLayoutInitialized)
        {
            _commitGraphLayoutInitialized = true;
            InitializeGraphLayout(_historyGraphLayout, _viewModel.History.Rows);
            _viewModel.History.Rows.CollectionChanged += MainHistory_CollectionChanged;
        }

        ApplyActiveCommitGraphLayout();
    }

    private static void InitializeGraphLayout(CommitGraphLayoutState state, IEnumerable<HistoryRow> rows)
    {
        var startedAt = HistoryRenderDiagnostics.TimestampIfPerformanceCaptureActive();
        var rowsExamined = 0;
        foreach (var row in rows)
        {
            rowsExamined++;
            state.ObserveLaneCount(row.Topology.LaneCount);
        }
        HistoryRenderDiagnostics.GraphLayoutInitialized(startedAt, rowsExamined);
    }

    private void MainHistory_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        HistoryRenderDiagnostics.HistoryCollectionChanged(args.Action, args.NewItems?.Count ?? 0);
        UpdateGraphLayout(_historyGraphLayout, args, isActive: true);
    }

    private void UpdateGraphLayout(
        CommitGraphLayoutState state,
        NotifyCollectionChangedEventArgs args,
        bool isActive)
    {
        var startedAt = HistoryRenderDiagnostics.TimestampIfPerformanceCaptureActive();
        var reset = args.Action == NotifyCollectionChangedAction.Reset;
        var layoutChanged = false;
        var newRowsExamined = 0;
        if (reset)
        {
            layoutChanged |= state.Reset();
            HistoryRenderDiagnostics.GeometrySharedCacheStateChanged(
                state.GeometryCache.Count,
                evicted: false);
            foreach (var row in _viewModel.History.Rows)
            {
                newRowsExamined++;
                layoutChanged |= state.ObserveLaneCount(row.Topology.LaneCount);
            }
        }

        if (!reset && args.NewItems is not null)
        {
            foreach (var item in args.NewItems)
            {
                if (item is HistoryRow row)
                {
                    newRowsExamined++;
                    layoutChanged |= state.ObserveLaneCount(row.Topology.LaneCount);
                }
            }
        }

        HistoryRenderDiagnostics.GraphLayoutUpdated(startedAt, reset, newRowsExamined, layoutChanged);
        if (layoutChanged && isActive)
            QueueActiveCommitGraphLayout();
    }

    private void QueueActiveCommitGraphLayout()
    {
        HistoryRenderDiagnostics.GraphLayoutQueued();
        if (_commitGraphLayoutApplyQueued)
            return;

        _commitGraphLayoutApplyQueued = true;
        if (!DispatcherQueue.TryEnqueue(() =>
            {
                _commitGraphLayoutApplyQueued = false;
                ApplyActiveCommitGraphLayout();
            }))
        {
            _commitGraphLayoutApplyQueued = false;
        }
    }

    private void ApplyActiveCommitGraphLayout()
    {
        if (!_commitGraphLayoutInitialized)
            return;

        var startedAt = HistoryRenderDiagnostics.TimestampIfPerformanceCaptureActive();
        var layout = ActiveCommitGraphLayout;
        HistoryGraphHeaderColumn.Width = new GridLength(layout.GraphWidth);
        CommitGraphPresentationContext.Publish(layout);
        HistoryRenderDiagnostics.GraphLayoutApplied(startedAt);
    }

}
