using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private const double HistoryLoadMoreThreshold = 68;
    private ScrollViewer? _historyScrollViewer;
    private bool _isInfiniteHistoryLoading;

    private void HistoryList_Loaded(object sender, RoutedEventArgs e)
    {
        var scrollViewer = FindHistoryScrollViewer(HistoryList);
        if (ReferenceEquals(_historyScrollViewer, scrollViewer)) return;

        if (_historyScrollViewer is not null)
            _historyScrollViewer.ViewChanged -= HistoryScrollViewer_ViewChanged;

        _historyScrollViewer = scrollViewer;
        if (_historyScrollViewer is not null)
            _historyScrollViewer.ViewChanged += HistoryScrollViewer_ViewChanged;
    }

    private async void HistoryScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer
            || _isInfiniteHistoryLoading
            || scrollViewer.ScrollableHeight <= 0
            || scrollViewer.ScrollableHeight - scrollViewer.VerticalOffset > HistoryLoadMoreThreshold)
            return;

        if (_activeReference is not null)
        {
            if (!_scopedHasMore || _isScopedHistoryLoading) return;
        }
        else if (!_viewModel.HasMore || _viewModel.IsBusy)
        {
            return;
        }

        _isInfiniteHistoryLoading = true;
        try
        {
            if (_activeReference is not null)
                await LoadScopedHistoryAsync(false);
            else
                await ExecuteCommandAsync(_viewModel.LoadMoreCommand);
        }
        finally
        {
            _isInfiniteHistoryLoading = false;
        }
    }

    private static ScrollViewer? FindHistoryScrollViewer(DependencyObject root)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer scrollViewer) return scrollViewer;

            var nested = FindHistoryScrollViewer(child);
            if (nested is not null) return nested;
        }

        return null;
    }
}
