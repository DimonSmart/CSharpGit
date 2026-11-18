using System.Collections.Specialized;
using CSharpGit.Domain;
using CSharpGit.Presentation.Controls;
using CSharpGit.Presentation.Controls.CommitGraph;
using Microsoft.UI.Xaml;

namespace CSharpGit.Presentation.Views;

public sealed partial class HistoryView
{
    private readonly CommitGraphLayoutState _historyGraphLayout = new();
    private bool _commitGraphLayoutInitialized;
    private bool _commitGraphLayoutApplyQueued;

    public CommitGraphLayoutState Layout => _historyGraphLayout;
    public event EventHandler? LayoutChanged;

    private void HistoryColumnHeader_Loaded(object sender, RoutedEventArgs args)
    {
        if (_disposed || _history is null) return;
        if (!_commitGraphLayoutInitialized)
        {
            _commitGraphLayoutInitialized = true;
            InitializeGraphLayout(_historyGraphLayout, _history.Rows);
            _history.Rows.CollectionChanged += HistoryRows_CollectionChanged;
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

    private void HistoryRows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (_disposed) return;
        HistoryRenderDiagnostics.HistoryCollectionChanged(args.Action, args.NewItems?.Count ?? 0);
        UpdateGraphLayout(_historyGraphLayout, args);
    }

    private void UpdateGraphLayout(
        CommitGraphLayoutState state, NotifyCollectionChangedEventArgs args)
    {
        var startedAt = HistoryRenderDiagnostics.TimestampIfPerformanceCaptureActive();
        var reset = args.Action == NotifyCollectionChangedAction.Reset;
        var layoutChanged = false;
        var newRowsExamined = 0;
        if (reset)
        {
            layoutChanged |= state.Reset();
            HistoryRenderDiagnostics.GeometrySharedCacheStateChanged(
                state.GeometryCache.Count, evicted: false);
        }

        if (args.NewItems is not null)
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
        if (layoutChanged)
            QueueActiveCommitGraphLayout();
    }

    private void QueueActiveCommitGraphLayout()
    {
        HistoryRenderDiagnostics.GraphLayoutQueued();
        if (_commitGraphLayoutApplyQueued || _disposed) return;
        _commitGraphLayoutApplyQueued = true;
        if (!DispatcherQueue.TryEnqueue(() =>
            {
                _commitGraphLayoutApplyQueued = false;
                if (!_disposed) ApplyActiveCommitGraphLayout();
            }))
        {
            _commitGraphLayoutApplyQueued = false;
        }
    }

    private void ApplyActiveCommitGraphLayout()
    {
        if (!_commitGraphLayoutInitialized || _disposed) return;
        var startedAt = HistoryRenderDiagnostics.TimestampIfPerformanceCaptureActive();
        HistoryGraphHeaderColumn.Width = new GridLength(_historyGraphLayout.GraphWidth);
        LayoutChanged?.Invoke(this, EventArgs.Empty);
        HistoryRenderDiagnostics.PresentationPublished(startedAt);
        HistoryRenderDiagnostics.GraphLayoutApplied(startedAt);
    }
}
