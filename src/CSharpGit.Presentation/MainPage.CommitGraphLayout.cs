using System.Collections.Specialized;
using CSharpGit.Domain;
using CSharpGit.Presentation.Controls.CommitGraph;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private readonly CommitGraphLayoutState _historyGraphLayout = new();
    private readonly CommitGraphLayoutState _scopedHistoryGraphLayout = new();
    private bool _commitGraphLayoutInitialized;
    private bool _commitGraphLayoutApplyQueued;

    private CommitGraphLayoutState ActiveCommitGraphLayout =>
        ReferenceEquals(HistoryList.ItemsSource, _scopedHistory)
            ? _scopedHistoryGraphLayout
            : _historyGraphLayout;

    private void HistoryColumnHeader_Loaded(object sender, RoutedEventArgs args)
    {
        if (!_commitGraphLayoutInitialized)
        {
            _commitGraphLayoutInitialized = true;
            InitializeGraphLayout(_historyGraphLayout, _viewModel.History);
            InitializeGraphLayout(_scopedHistoryGraphLayout, _scopedHistory);
            _viewModel.History.CollectionChanged += MainHistory_CollectionChanged;
            _scopedHistory.CollectionChanged += ScopedHistory_CollectionChanged;
            HistoryList.RegisterPropertyChangedCallback(ItemsControl.ItemsSourceProperty, HistoryItemsSourceChanged);
        }

        ApplyActiveCommitGraphLayout();
    }

    private static void InitializeGraphLayout(CommitGraphLayoutState state, IEnumerable<HistoryRow> rows)
    {
        foreach (var row in rows)
            state.ObserveLaneCount(row.Topology.LaneCount);
    }

    private void MainHistory_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
        => UpdateGraphLayout(_historyGraphLayout, args, ReferenceEquals(HistoryList.ItemsSource, _viewModel.History));

    private void ScopedHistory_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
        => UpdateGraphLayout(_scopedHistoryGraphLayout, args, ReferenceEquals(HistoryList.ItemsSource, _scopedHistory));

    private void UpdateGraphLayout(
        CommitGraphLayoutState state,
        NotifyCollectionChangedEventArgs args,
        bool isActive)
    {
        var layoutChanged = false;
        if (args.Action == NotifyCollectionChangedAction.Reset)
            layoutChanged |= state.Reset();

        if (args.NewItems is not null)
        {
            foreach (var item in args.NewItems)
            {
                if (item is HistoryRow row)
                    layoutChanged |= state.ObserveLaneCount(row.Topology.LaneCount);
            }
        }

        if (layoutChanged && isActive)
            QueueActiveCommitGraphLayout();
    }

    private void HistoryItemsSourceChanged(DependencyObject sender, DependencyProperty property)
        => QueueActiveCommitGraphLayout();

    private void QueueActiveCommitGraphLayout()
    {
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

        var layout = ActiveCommitGraphLayout;
        HistoryGraphHeaderColumn.Width = new GridLength(layout.GraphWidth);
        CommitGraphPresentationContext.Publish(layout);
    }

    private void HistoryList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue)
            return;

        var container = args.ItemContainer;
        DispatcherQueue.TryEnqueue(() => ConfigureAuthorAvatars(container));
    }
}
