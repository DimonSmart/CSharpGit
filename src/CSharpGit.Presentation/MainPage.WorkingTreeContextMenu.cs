using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private void UnstagedChangesTree_RightTapped(object sender, RightTappedRoutedEventArgs args) =>
        ShowWorkingTreeContextMenu(args, WorkingTreeDiffKind.Unstaged);

    private void StagedChangesTree_RightTapped(object sender, RightTappedRoutedEventArgs args) =>
        ShowWorkingTreeContextMenu(args, WorkingTreeDiffKind.Staged);

    private void ShowWorkingTreeContextMenu(
        RightTappedRoutedEventArgs args,
        WorkingTreeDiffKind kind)
    {
        var source = args.OriginalSource as FrameworkElement;
        var node = ResolveWorkingTreeNode(source?.DataContext);
        if (source is null || node is null) return;

        var flyout = new MenuFlyout();
        if (node.Change is not null)
        {
            var selection = SelectWorkingTreeContextTarget(node, kind);
            AddWorkingTreeFileContextMenuItems(flyout, kind, selection);
        }
        else if (node.IsFolder)
        {
            var changes = node.GetDescendantChanges();
            AddWorkingTreeFolderContextMenuItems(flyout, kind, changes);
        }
        else
        {
            return;
        }

        flyout.ShowAt(source, args.GetPosition(source));
        args.Handled = true;
    }

    private void AddWorkingTreeFileContextMenuItems(
        MenuFlyout flyout,
        WorkingTreeDiffKind kind,
        IReadOnlyList<WorkingTreeChange> changes)
    {
        if (changes.Count == 0) return;

        var count = changes.Count;
        if (kind == WorkingTreeDiffKind.Unstaged)
        {
            AddMenuItem(
                flyout,
                count == 1 ? "Stage" : $"Stage {count} files",
                _viewModel.WorkingTree.StageSelectedCommand.CanExecute(null),
                () => ExecuteCommandAsync(_viewModel.WorkingTree.StageSelectedCommand));
            AddMenuItem(
                flyout,
                count == 1 ? "Stash selected…" : $"Stash {count} files…",
                _viewModel.CanCreateSelectedStash(changes),
                () => ShowCreateSelectedStashDialogAsync(changes));
            flyout.Items.Add(new MenuFlyoutSeparator());
            AddMenuItem(
                flyout,
                count == 1 ? "Discard changes…" : $"Discard {count} files…",
                _viewModel.WorkingTree.RequestDiscardSelectedCommand.CanExecute(null),
                () => ExecuteCommandAsync(_viewModel.WorkingTree.RequestDiscardSelectedCommand));
        }
        else
        {
            AddMenuItem(
                flyout,
                count == 1 ? "Unstage" : $"Unstage {count} files",
                _viewModel.WorkingTree.UnstageSelectedCommand.CanExecute(null),
                () => ExecuteCommandAsync(_viewModel.WorkingTree.UnstageSelectedCommand));
            AddMenuItem(
                flyout,
                count == 1 ? "Stash selected…" : $"Stash {count} files…",
                _viewModel.CanCreateSelectedStash(changes),
                () => ShowCreateSelectedStashDialogAsync(changes));

            if (count == 1)
            {
                flyout.Items.Add(new MenuFlyoutSeparator());
                var change = changes[0];
                AddMenuItem(
                    flyout,
                    "Discard changes…",
                    CanDiscardStagedFile(change),
                    () => ConfirmDiscardStagedFileAsync(change));
            }
        }
    }

    private void AddWorkingTreeFolderContextMenuItems(
        MenuFlyout flyout,
        WorkingTreeDiffKind kind,
        IReadOnlyList<WorkingTreeChange> changes)
    {
        if (kind == WorkingTreeDiffKind.Unstaged)
        {
            AddMenuItem(
                flyout,
                "Stage",
                _viewModel.WorkingTree.CanStageChanges(changes),
                () => _viewModel.WorkingTree.StageChangesAsync(changes, "Could not stage folder"));
        }
        else
        {
            AddMenuItem(
                flyout,
                "Unstage",
                _viewModel.WorkingTree.CanUnstageChanges(changes),
                () => _viewModel.WorkingTree.UnstageChangesAsync(changes, "Could not unstage folder"));
        }
    }

    private IReadOnlyList<WorkingTreeChange> SelectWorkingTreeContextTarget(
        WorkingTreeTreeNode node,
        WorkingTreeDiffKind kind)
    {
        var roots = kind == WorkingTreeDiffKind.Unstaged ? _unstagedTreeRoots : _stagedTreeRoots;
        var selection = kind == WorkingTreeDiffKind.Unstaged ? _unstagedTreeSelection : _stagedTreeSelection;

        if (!selection.IsSelected(node.Path))
            selection.SelectSingle(node, roots);

        var snapshot = selection
            .GetSelectedLeaves(roots)
            .Select(selected => selected.Change!)
            .ToArray();

        _viewModel.WorkingTree.SetSelection(kind, snapshot);
        SelectWorkingTreeChange(node.Change!, kind);
        return snapshot;
    }

    private bool CanDiscardStagedFile(WorkingTreeChange change) =>
        _viewModel.WorkingTree.CanDiscardAllFileChanges(change);

    private async Task ConfirmDiscardStagedFileAsync(WorkingTreeChange change)
    {
        if (!CanDiscardStagedFile(change)) return;

        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock
        {
            Text = $"Discard all changes in '{change.Path}'?",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = "The staged changes will be permanently discarded and the path will be restored to its committed state.",
            TextWrapping = TextWrapping.Wrap
        });

        if (change.IsUnstaged)
        {
            content.Children.Add(new TextBlock
            {
                Text = "This file also has unstaged changes. They will be permanently discarded too.",
                TextWrapping = TextWrapping.Wrap,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
        }

        if (change.IndexStatus == 'A')
        {
            content.Children.Add(new TextBlock
            {
                Text = "A newly added file has no committed version and will be removed from the working tree.",
                TextWrapping = TextWrapping.Wrap,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Discard changes?",
            Content = content,
            PrimaryButtonText = "Discard",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        await _viewModel.WorkingTree.DiscardAllFileChangesAsync(change);
    }
}
