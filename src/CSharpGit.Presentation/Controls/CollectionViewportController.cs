using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace CSharpGit.Presentation.Controls;

/// <summary>
/// Maintains a valid viewport while a logical list is replaced or shortened.
/// It deliberately does not reset scrolling for additions or an unchanged context.
/// </summary>
internal sealed class CollectionViewportController : IDisposable
{
    private readonly FrameworkElement _surface;
    private readonly CollectionViewportState _state = new();
    private readonly DiffViewportResetGate _resetGate = new();
    private bool _disposed;

    public CollectionViewportController(FrameworkElement surface)
    {
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        _surface.PointerPressed += OnPointerInteraction;
        _surface.PointerWheelChanged += OnPointerInteraction;
        _surface.KeyDown += OnKeyInteraction;
    }

    public void Observe(string? contextKey, int itemCount)
    {
        if (_disposed) return;
        var adjustment = _state.Observe(contextKey, itemCount);
        if (adjustment == CollectionViewportAdjustment.None) return;

        var ticket = _resetGate.BeginReplacement();
        if (adjustment == CollectionViewportAdjustment.ResetToTop)
            Apply(ticket, adjustment);

        // A TreeView/ListView may not have measured its new content when the
        // collection notification fires. Recheck on the next UI turn.
        _surface.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low,
            () => Apply(ticket, adjustment));
    }

    public void CancelPendingAdjustment() => _resetGate.RegisterInteraction();

    private void Apply(DiffViewportResetTicket ticket, CollectionViewportAdjustment adjustment)
    {
        if (_disposed || !_resetGate.IsCurrent(ticket)) return;

        if (_surface is Control control) control.ApplyTemplate();
        var viewer = FindScrollViewer(_surface);
        if (viewer is null) return;

        if (adjustment == CollectionViewportAdjustment.ResetToTop)
        {
            viewer.ChangeView(null, 0d, null, true);
            return;
        }

        _surface.UpdateLayout();
        var maximum = Math.Max(0d, viewer.ScrollableHeight);
        if (viewer.VerticalOffset > maximum)
            viewer.ChangeView(null, maximum, null, true);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer viewer) return viewer;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var nested = FindScrollViewer(VisualTreeHelper.GetChild(root, index));
            if (nested is not null) return nested;
        }

        return null;
    }

    private void OnPointerInteraction(object sender, PointerRoutedEventArgs args) =>
        CancelPendingAdjustment();

    private void OnKeyInteraction(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key is VirtualKey.Up or VirtualKey.Down or VirtualKey.Left or VirtualKey.Right
            or VirtualKey.PageUp or VirtualKey.PageDown or VirtualKey.Home or VirtualKey.End)
            CancelPendingAdjustment();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelPendingAdjustment();
        _surface.PointerPressed -= OnPointerInteraction;
        _surface.PointerWheelChanged -= OnPointerInteraction;
        _surface.KeyDown -= OnKeyInteraction;
    }
}
