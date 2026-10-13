using System.ComponentModel;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation.Controls;

/// <summary>
/// Applies preview spans to GridViewItems, never to the template roots. A
/// subscription belongs to one container/item pair and is detached on recycle.
/// </summary>
public sealed class RepositoryTileGridView : GridView
{
    private readonly Dictionary<GridViewItem, Subscription> _subscriptions = [];
    private int _columns;

    public RepositoryTileGridView()
    {
        Loaded += (_, _) =>
        {
            RefreshMetrics();
            // An unloaded view may be reattached without new Prepare calls.
            foreach (var item in Items)
                if (item is RecentRepositoryItem repository
                    && ContainerFromItem(item) is GridViewItem container)
                    Track(container, repository);
        };
        Unloaded += (_, _) =>
        {
            foreach (var container in _subscriptions.Keys.ToArray())
                Untrack(container);
        };
        SizeChanged += (_, _) => RefreshMetrics();
    }

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        if (element is GridViewItem container)
            Untrack(container);
        base.PrepareContainerForItemOverride(element, item);
        if (element is GridViewItem prepared && item is RecentRepositoryItem repository)
            Track(prepared, repository);
    }

    protected override void ClearContainerForItemOverride(DependencyObject element, object item)
    {
        if (element is GridViewItem container)
            Untrack(container);
        base.ClearContainerForItemOverride(element, item);
    }

    private void Track(GridViewItem container, RecentRepositoryItem item)
    {
        if (_subscriptions.TryGetValue(container, out var existing))
        {
            if (ReferenceEquals(existing.Item, item)) return;
            Untrack(container);
        }

        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName != nameof(RecentRepositoryItem.HasReadyPreview)
                || !_subscriptions.TryGetValue(container, out var current)
                || !ReferenceEquals(current.Item, item))
                return;
            ApplySpan(container, item);
        };
        _subscriptions.Add(container, new Subscription(item, handler));
        item.PropertyChanged += handler;
        ApplySpan(container, item);
    }

    private void Untrack(GridViewItem container)
    {
        if (_subscriptions.Remove(container, out var subscription))
            subscription.Item.PropertyChanged -= subscription.Handler;
        if (VariableSizedWrapGrid.GetColumnSpan(container) != 1)
            VariableSizedWrapGrid.SetColumnSpan(container, 1);
    }

    private void ApplySpan(GridViewItem container, RecentRepositoryItem item)
    {
        var span = RepositoryTileGridMetrics.ColumnSpan(item.HasReadyPreview, _columns);
        if (VariableSizedWrapGrid.GetColumnSpan(container) != span)
            VariableSizedWrapGrid.SetColumnSpan(container, span);
    }

    private void RefreshMetrics()
    {
        var (columns, cellWidth) = RepositoryTileGridMetrics.Calculate(ActualWidth);
        if (ItemsPanelRoot is VariableSizedWrapGrid panel && cellWidth > 0
            && Math.Abs(panel.ItemWidth - cellWidth) >= 0.05)
            panel.ItemWidth = cellWidth;

        if (_columns == columns) return;
        _columns = columns;
        foreach (var (container, subscription) in _subscriptions)
            ApplySpan(container, subscription.Item);
    }

    private sealed record Subscription(RecentRepositoryItem Item, PropertyChangedEventHandler Handler);
}
