using System.Text;
using CSharpGit.Domain;
using CSharpGit.Presentation.Dialogs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private bool _tagSupportInitialized;
    private MenuFlyoutItem? _createTagHereItem;
    private MenuFlyoutSubItem? _deleteTagSubItem;

    private void InitializeTagSupportIfNeeded()
    {
        if (_tagSupportInitialized) return;
        _tagSupportInitialized = true;

        if (_commitActionsFlyout is null) return;
        _createTagHereItem = new MenuFlyoutItem { Text = "Create tag here…" };
        _createTagHereItem.Click += (_, _) =>
        {
            if (_viewModel.History.SelectedRow?.Commit.Hash is { } hash)
                RunPageUiOperation(() => _tagWorkflows.ShowCreateTagDialogAsync(hash, selectedCommit: true), "Create tag here");
        };
        _commitActionsFlyout.Items.Insert(3, _createTagHereItem);

        _deleteTagSubItem = new MenuFlyoutSubItem { Text = "Delete tag" };
        var checkoutIndex = _commitActionsFlyout.Items.IndexOf(_checkoutCommitItem);
        _commitActionsFlyout.Items.Insert(checkoutIndex + 1, _deleteTagSubItem);

        _commitActionsFlyout.Opening += (_, _) =>
        {
            if (_createTagHereItem is not null)
                _createTagHereItem.IsEnabled = CanMutateTags() && _viewModel.History.SelectedRow is not null;
            UpdateDeleteTagSubmenu();
        };
    }

    private void UpdateDeleteTagSubmenu()
    {
        if (_deleteTagSubItem is null) return;

        _deleteTagSubItem.Items.Clear();

        var selectedCommitHash = _viewModel.History.SelectedRow?.Commit.Hash;
        if (string.IsNullOrWhiteSpace(selectedCommitHash))
        {
            _deleteTagSubItem.IsEnabled = false;
            return;
        }

        var canMutate = CanMutateTags();
        foreach (var tag in _viewModel.Tags.Items)
        {
            if (!string.Equals(tag.TargetCommit, selectedCommitHash, StringComparison.Ordinal)) continue;

            var item = new MenuFlyoutItem
            {
                Text = tag.Name,
                IsEnabled = canMutate
            };
            item.Click += (_, _) =>
                RunPageUiOperation(() => _tagWorkflows.DeleteTagFromUiAsync(tag), "Delete tag");
            _deleteTagSubItem.Items.Add(item);
        }

        _deleteTagSubItem.IsEnabled = canMutate && _deleteTagSubItem.Items.Count > 0;
    }

    private void ApplyTagOrderingToRepositoryTree()
    {
        SynchronizeRepositoryTree();
    }

    private bool TryShowTagContextMenu(FrameworkElement source, RightTappedRoutedEventArgs args)
    {
        var node = ResolveNode(source.DataContext);
        if (node is null) return false;

        if (node.Kind == RepositoryTreeNodeKind.Group && node.Name == "Tags")
        {
            var flyout = new MenuFlyout();
            AddMenuItem(flyout, "Create tag…", () => CanMutateTags(), () => _tagWorkflows.ShowCreateTagDialogAsync("HEAD", selectedCommit: false));
            AddMenuItem(flyout, "Fetch tags…", () => CanMutateTags(), _tagWorkflows.FetchTagsFromUiAsync);
            AddMenuItem(flyout, "Remote tags…", () => CanMutateTags(), _tagWorkflows.ShowRemoteTagsAsync);
            AddMenuItem(flyout, "Push all tags…", () => CanMutateTags(), _tagWorkflows.PushAllTagsFromUiAsync);
            flyout.ShowAt(source, args.GetPosition(source));
            args.Handled = true;
            return true;
        }

        if (node.Kind == RepositoryTreeNodeKind.Tag && node.Value is GitTag tag)
        {
            var flyout = new MenuFlyout();
            AddMenuItem(flyout, "Tag details…", true, () => _tagWorkflows.ShowTagDetailsAsync(tag));
            AddMenuItem(flyout, "Show history up to tag", () => _viewModel.CanStartUserGitOperation,
                () => ShowReferenceHistoryAsync($"refs/tags/{tag.Name}", $"Tag: {tag.Name}"));
            AddMenuItem(flyout, "Create branch from here…", () => CanMutateTags(),
                () => CreateBranchFromReferenceAsync($"refs/tags/{tag.Name}", tag.TargetCommit));
            AddMenuItem(flyout, "Checkout detached", () => CanMutateTags(), () => ConfirmCheckoutTagDetachedAsync(tag));
            flyout.Items.Add(new MenuFlyoutSeparator());
            AddMenuItem(flyout, "Push tag…", () => CanMutateTags(), () => _tagWorkflows.PushTagFromUiAsync(tag));
            AddMenuItem(flyout, "Delete from remote…", () => CanMutateTags(), () => _tagWorkflows.DeleteRemoteTagFromUiAsync(tag));
            AddMenuItem(flyout, "Delete tag…", () => CanMutateTags(), () => _tagWorkflows.DeleteTagFromUiAsync(tag));
            flyout.Items.Add(new MenuFlyoutSeparator());
            AddMenuItem(flyout, "Copy tag name", true, () => CopyTextAsync(tag.Name));
            flyout.ShowAt(source, args.GetPosition(source));
            args.Handled = true;
            return true;
        }

        return false;
    }

    private bool CanMutateTags() => _viewModel.Tags.CanMutate;

    private async Task ShowInformationAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };
        await ShowPageDialogAsync(dialog);
    }
}