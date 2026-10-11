using CSharpGit.Presentation.Controls;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace CSharpGit.Presentation;

public sealed partial class RecentRepositoriesView : UserControl
{
    private RecentRepositoryItem? _pinnedDragSource;
    private GridView? _pinnedDragGrid;
    private GridViewItem? _pinnedDragTarget;

    public RecentRepositoriesView()
    {
        InitializeComponent();
        Loaded += RecentRepositoriesView_Loaded;
    }

    private RecentRepositoriesViewModel? Model => DataContext as RecentRepositoriesViewModel;

    private void RecentRepositoriesView_Loaded(object sender, RoutedEventArgs e) =>
        Model?.StartImageLoading();

    private void RepositoryGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is GridView grid) UpdateRepositoryGridMetrics(grid);
    }

    private void RepositoryGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is GridView grid) UpdateRepositoryGridMetrics(grid);
    }

    private static void UpdateRepositoryGridMetrics(GridView grid)
    {
        if (grid.ItemsPanelRoot is not VariableSizedWrapGrid panel) return;
        var (_, cellWidth) = RepositoryTileGridMetrics.Calculate(grid.ActualWidth);
        if (cellWidth <= 0 || Math.Abs(panel.ItemWidth - cellWidth) < 0.05) return;
        panel.ItemWidth = cellWidth;
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

    private async Task ShowErrorAsync(string action, Exception exception)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = action,
            Content = exception is ArgumentException ? exception.Message
                : $"Could not save repository groups: {exception.Message}",
            CloseButtonText = "OK",
            DefaultButton = ContentDialogButton.Close
        };
        await dialog.ShowAsync();
    }

    private async Task RunActionAsync(string title, Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { await ShowErrorAsync(title, exception); }
    }

    private static MenuFlyoutItem MenuAction(string text, bool enabled, Action handler)
    {
        var item = new MenuFlyoutItem { Text = text, IsEnabled = enabled };
        item.Click += (_, _) => handler();
        return item;
    }

    private async Task<string?> RequestNameAsync(string title, string initial, string action)
    {
        var input = new TextBox { Text = initial, MaxLength = 128,
            PlaceholderText = "Group name", SelectionStart = initial.Length };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = input,
            PrimaryButtonText = action,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? input.Text : null;
    }

    private async void NewGroup_Click(object sender, RoutedEventArgs e)
    {
        if (Model is not { } vm) return;
        var name = await RequestNameAsync("New repository group", "", "Create");
        if (name is null) return;
        await RunActionAsync("Could not create group", async () =>
        {
            await vm.CreateGroupAsync(name);
            vm.SearchText = string.Empty;
        });
    }

    private async void GroupExpand_Click(object sender, RoutedEventArgs e)
    {
        if (Model is not { IsSearchActive: false } vm
            || sender is not Button { DataContext: RepositoryGroupViewModel group }) return;
        await RunActionAsync("Could not change group expansion",
            () => vm.SetGroupExpandedAsync(group.Id, !group.IsExpanded));
    }

    private void GroupActions_Click(object sender, RoutedEventArgs e)
    {
        if (Model is not { } vm
            || sender is not Button { DataContext: RepositoryGroupViewModel group } button) return;
        var flyout = new MenuFlyout();
        flyout.Items.Add(MenuAction("Rename group", true, () => _ = RenameGroupAsync(vm, group)));
        flyout.Items.Add(MenuAction("Move group up", group.CanMoveUp,
            () => _ = RunActionAsync("Could not move group", () => vm.MoveGroupAsync(group.Id, group.Order - 1))));
        flyout.Items.Add(MenuAction("Move group down", group.CanMoveDown,
            () => _ = RunActionAsync("Could not move group", () => vm.MoveGroupAsync(group.Id, group.Order + 1))));
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(MenuAction("Delete group", group.CanDelete, () => _ = DeleteGroupAsync(vm, group)));
        flyout.ShowAt(button);
    }

    private async Task RenameGroupAsync(RecentRepositoriesViewModel vm, RepositoryGroupViewModel group)
    {
        var name = await RequestNameAsync("Rename group", group.Name, "Rename");
        if (name is not null)
            await RunActionAsync("Could not rename group", () => vm.RenameGroupAsync(group.Id, name));
    }

    private async Task DeleteGroupAsync(RecentRepositoriesViewModel vm, RepositoryGroupViewModel group)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Delete {group.Name}?",
            Content = "Repositories in this group will be moved to Favorites; no repositories will be deleted.",
            PrimaryButtonText = "Delete group",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await RunActionAsync("Could not delete group", () => vm.DeleteGroupAsync(group.Id));
    }

    private void ShowGroupChoices(FrameworkElement anchor, RecentRepositoryItem repository, bool moving)
    {
        if (Model is not { } vm) return;
        var flyout = new MenuFlyout();
        foreach (var group in vm.Groups)
        {
            var selected = repository.IsPinned && repository.GroupId == group.Id;
            flyout.Items.Add(MenuAction(selected ? $"{group.Name} (current)" : group.Name,
                !selected, () => _ = RunActionAsync("Could not assign repository to group",
                    () => vm.PinToGroupAsync(repository, group.Id))));
        }
        flyout.ShowAt(anchor);
    }

    private async void RepositoryPin_Click(object sender, RoutedEventArgs e)
    {
        if (Model is not { } vm
            || sender is not Button { DataContext: RecentRepositoryItem repository } button) return;
        if (!repository.IsPinned && vm.Groups.Count > 1)
        {
            ShowGroupChoices(button, repository, moving: false);
            return;
        }
        await RunActionAsync("Could not change repository pin", () => vm.TogglePinAsync(repository));
    }

    private void RepositoryContextFlyout_Opening(object sender, object args)
    {
        if (sender is not MenuFlyout flyout
            || flyout.Target is not FrameworkElement { DataContext: RecentRepositoryItem repo }
            || Model is not { } vm) return;
        flyout.Items.Clear();
        flyout.Items.Add(MenuAction("Open", repo.IsAvailable,
            () => { if (repo.OpenCommand.CanExecute(null)) repo.OpenCommand.Execute(null); }));
        flyout.Items.Add(MenuAction(repo.OpenFolderActionLabel, repo.IsAvailable,
            () => { if (repo.OpenFolderCommand.CanExecute(null)) repo.OpenFolderCommand.Execute(null); }));
        flyout.Items.Add(MenuAction("Copy repository path", true,
            () => { if (repo.CopyPathCommand.CanExecute(null)) repo.CopyPathCommand.Execute(null); }));
        flyout.Items.Add(new MenuFlyoutSeparator());

        if (repo.IsPinned)
        {
            flyout.Items.Add(MenuAction("Unpin repository", true,
                () => _ = RunActionAsync("Could not unpin repository", () => vm.UnpinAsync(repo))));
            var move = new MenuFlyoutSubItem { Text = "Move to group" };
            foreach (var group in vm.Groups)
            {
                var selected = repo.GroupId == group.Id;
                move.Items.Add(MenuAction(selected ? $"{group.Name} (current)" : group.Name, !selected,
                    () => _ = RunActionAsync("Could not move repository", () => vm.PinToGroupAsync(repo, group.Id))));
            }
            flyout.Items.Add(move);
            flyout.Items.Add(MenuAction("Move earlier", repo.CanMoveEarlier,
                () => { if (repo.MoveEarlierCommand.CanExecute(null)) repo.MoveEarlierCommand.Execute(null); }));
            flyout.Items.Add(MenuAction("Move later", repo.CanMoveLater,
                () => { if (repo.MoveLaterCommand.CanExecute(null)) repo.MoveLaterCommand.Execute(null); }));
        }
        else if (vm.Groups.Count > 1)
        {
            var pin = new MenuFlyoutSubItem { Text = "Pin to" };
            foreach (var group in vm.Groups)
                pin.Items.Add(MenuAction(group.Name, true,
                    () => _ = RunActionAsync("Could not pin repository", () => vm.PinToGroupAsync(repo, group.Id))));
            flyout.Items.Add(pin);
        }
        else flyout.Items.Add(MenuAction("Pin repository", true,
            () => _ = RunActionAsync("Could not pin repository", () => vm.TogglePinAsync(repo))));

        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(MenuAction("Remove from recent", true,
            () => { if (repo.RemoveCommand.CanExecute(null)) repo.RemoveCommand.Execute(null); }));
    }

    private void ClearDrag()
    {
        _pinnedDragSource = null;
        _pinnedDragGrid = null;
        SetPinnedDragTarget(null);
    }

    private void PinnedRepositoriesGridView_DragItemsStarting(object sender, DragItemsStartingEventArgs args)
    {
        ClearDrag();
        if (Model is not { IsSearchActive: false }
            || sender is not GridView { DataContext: RepositoryGroupViewModel group } grid
            || !group.EffectiveIsExpanded
            || args.Items.Count != 1
            || args.Items[0] is not RecentRepositoryItem { IsPinned: true } item
            || item.GroupId != group.Id)
        {
            args.Cancel = true;
            return;
        }
        _pinnedDragSource = item;
        _pinnedDragGrid = grid;
        args.Data.RequestedOperation = DataPackageOperation.Move;
        args.Data.SetText(item.Path);
    }

    private bool CanDrop(object sender, DragEventArgs args, out RecentRepositoryItem? target)
    {
        target = FindGridViewItem(args.OriginalSource as DependencyObject)?.DataContext as RecentRepositoryItem;
        return sender is GridView grid
            && ReferenceEquals(grid, _pinnedDragGrid)
            && _pinnedDragSource is { IsPinned: true } source
            && grid.DataContext is RepositoryGroupViewModel { EffectiveIsExpanded: true } group
            && Model is { IsSearchActive: false }
            && target is { IsPinned: true }
            && source.GroupId == group.Id
            && target.GroupId == group.Id
            && grid.Items.Contains(target)
            && !ReferenceEquals(source, target);
    }

    private void PinnedRepositoriesGridView_DragOver(object sender, DragEventArgs args)
    {
        if (!CanDrop(sender, args, out _))
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
        var valid = CanDrop(sender, args, out var target);
        var source = _pinnedDragSource;
        ClearDrag();
        if (!valid || source is null || target?.PinnedOrder is null || Model is not { } vm)
        {
            args.AcceptedOperation = DataPackageOperation.None;
            return;
        }
        args.AcceptedOperation = DataPackageOperation.Move;
        await RunActionAsync("Could not reorder repositories",
            () => vm.MovePinnedRepositoryAsync(source, target.PinnedOrder.Value));
    }

    private void PinnedRepositoriesGridView_DragItemsCompleted(object sender, DragItemsCompletedEventArgs args) =>
        ClearDrag();

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
        if (_pinnedDragTarget is not null) _pinnedDragTarget.Opacity = 1d;
        _pinnedDragTarget = target;
        if (_pinnedDragTarget is not null) _pinnedDragTarget.Opacity = 0.7d;
    }
}
