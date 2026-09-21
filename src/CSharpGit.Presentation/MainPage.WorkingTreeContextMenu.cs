using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private void UnstagedChangesTree_RightTapped(object sender, RightTappedRoutedEventArgs args) =>
        ShowWorkingTreeFileContextMenu(args, WorkingTreeDiffKind.Unstaged);

    private void StagedChangesTree_RightTapped(object sender, RightTappedRoutedEventArgs args) =>
        ShowWorkingTreeFileContextMenu(args, WorkingTreeDiffKind.Staged);

    private void ShowWorkingTreeFileContextMenu(
        RightTappedRoutedEventArgs args,
        WorkingTreeDiffKind kind)
    {
        var source = args.OriginalSource as FrameworkElement;
        var node = ResolveWorkingTreeNode(source?.DataContext);
        if (source is null || node?.Change is not { } change) return;

        SelectWorkingTreeContextTarget(node, kind);

        var flyout = new MenuFlyout();
        if (kind == WorkingTreeDiffKind.Unstaged)
        {
            AddMenuItem(
                flyout,
                "Stage",
                _viewModel.StageSelectedCommand.CanExecute(null),
                () => ExecuteCommandAsync(_viewModel.StageSelectedCommand));
            flyout.Items.Add(new MenuFlyoutSeparator());
            AddMenuItem(
                flyout,
                "Discard changes…",
                _viewModel.RequestDiscardSelectedCommand.CanExecute(null),
                () => ExecuteCommandAsync(_viewModel.RequestDiscardSelectedCommand));
        }
        else
        {
            AddMenuItem(
                flyout,
                "Unstage",
                _viewModel.UnstageSelectedCommand.CanExecute(null),
                () => ExecuteCommandAsync(_viewModel.UnstageSelectedCommand));
            flyout.Items.Add(new MenuFlyoutSeparator());
            AddMenuItem(
                flyout,
                "Discard changes…",
                CanDiscardStagedFile(change),
                () => ConfirmDiscardStagedFileAsync(change));
        }

        flyout.ShowAt(source, args.GetPosition(source));
        args.Handled = true;
    }

    private void SelectWorkingTreeContextTarget(
        WorkingTreeTreeNode node,
        WorkingTreeDiffKind kind)
    {
        var roots = kind == WorkingTreeDiffKind.Unstaged ? _unstagedTreeRoots : _stagedTreeRoots;
        var selection = kind == WorkingTreeDiffKind.Unstaged ? _unstagedTreeSelection : _stagedTreeSelection;
        selection.SelectSingle(node, roots);
        _viewModel.SetWorkingTreeSelection(kind, [node.Change!]);
        SelectWorkingTreeChange(node.Change!, kind);
    }

    private bool CanDiscardStagedFile(WorkingTreeChange change) =>
        _viewModel.Repository is not null &&
        !_viewModel.IsBusy &&
        change.IsStaged &&
        !change.IsConflicted;

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
        await _viewModel.DiscardAllFileChangesAsync(change);
    }
}
