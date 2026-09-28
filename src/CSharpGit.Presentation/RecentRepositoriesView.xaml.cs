using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace CSharpGit.Presentation;

public sealed partial class RecentRepositoriesView : UserControl
{
    private RecentRepositoryItem? _pinnedDragSource;
    private GridViewItem? _pinnedDragTarget;

    public RecentRepositoriesView()
    {
        InitializeComponent();
        Loaded += RecentRepositoriesView_Loaded;
    }

    private void RecentRepositoriesView_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is RecentRepositoriesViewModel viewModel)
            viewModel.StartImageLoading();
    }

    private void RepositoryImage_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RecentRepositoryItem item })
            item.SetRepositoryImagePath(null);
    }

    private void RepositoryPreview_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RecentRepositoryItem item })
            item.SetRepositoryPreviewPath(null);
    }

    private void PinnedRepositoriesGridView_DragItemsStarting(
        object sender,
        DragItemsStartingEventArgs args)
    {
        _pinnedDragSource = null;
        SetPinnedDragTarget(null);

        if (DataContext is not RecentRepositoriesViewModel { IsSearchActive: false }
            || args.Items.Count != 1
            || args.Items[0] is not RecentRepositoryItem { IsPinned: true } item)
        {
            args.Cancel = true;
            return;
        }

        _pinnedDragSource = item;
        args.Data.RequestedOperation = DataPackageOperation.Move;
        args.Data.SetText(item.Path);
    }

    private void PinnedRepositoriesGridView_DragOver(object sender, DragEventArgs args)
    {
        if (_pinnedDragSource is null
            || DataContext is not RecentRepositoriesViewModel { IsSearchActive: false }
            || ResolvePinnedDropTarget(args) is not { IsPinned: true } target
            || ReferenceEquals(target, _pinnedDragSource))
        {
            args.AcceptedOperation = DataPackageOperation.None;
            SetPinnedDragTarget(null);
            return;
        }

        args.AcceptedOperation = DataPackageOperation.Move;
        SetPinnedDragTarget(FindGridViewItem(args.OriginalSource as DependencyObject));
    }

    private void PinnedRepositoriesGridView_DragLeave(object sender, DragEventArgs args) =>
        SetPinnedDragTarget(null);

    private async void PinnedRepositoriesGridView_Drop(object sender, DragEventArgs args)
    {
        var source = _pinnedDragSource;
        var target = ResolvePinnedDropTarget(args);
        SetPinnedDragTarget(null);

        if (source is null
            || target is null
            || ReferenceEquals(source, target)
            || target.PinnedOrder is null
            || DataContext is not RecentRepositoriesViewModel { IsSearchActive: false } viewModel)
        {
            args.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        args.AcceptedOperation = DataPackageOperation.Move;
        await viewModel.MovePinnedRepositoryAsync(source, target.PinnedOrder.Value);
    }

    private void PinnedRepositoriesGridView_DragItemsCompleted(
        object sender,
        DragItemsCompletedEventArgs args)
    {
        _pinnedDragSource = null;
        SetPinnedDragTarget(null);
    }

    private static RecentRepositoryItem? ResolvePinnedDropTarget(DragEventArgs args) =>
        FindGridViewItem(args.OriginalSource as DependencyObject)?.DataContext as RecentRepositoryItem;

    private static GridViewItem? FindGridViewItem(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is GridViewItem item) return item;
            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private void SetPinnedDragTarget(GridViewItem? target)
    {
        if (ReferenceEquals(_pinnedDragTarget, target)) return;

        if (_pinnedDragTarget is not null)
            _pinnedDragTarget.Opacity = 1d;

        _pinnedDragTarget = target;
        if (_pinnedDragTarget is not null)
            _pinnedDragTarget.Opacity = 0.7d;
    }
}
