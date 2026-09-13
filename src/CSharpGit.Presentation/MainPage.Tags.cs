using System.Text;
using CSharpGit.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private bool _tagSupportInitialized;
    private MenuFlyoutItem? _createTagHereItem;

    private void InitializeTagSupportIfNeeded()
    {
        if (_tagSupportInitialized) return;
        _tagSupportInitialized = true;

        RepositoryTree.RightTapped -= RepositoryTree_RightTapped;
        RepositoryTree.RightTapped += RepositoryTree_TagAwareRightTapped;

        if (_commitActionsFlyout is null) return;
        _createTagHereItem = new MenuFlyoutItem { Text = "Create tag here…" };
        _createTagHereItem.Click += async (_, _) =>
        {
            if (_viewModel.SelectedHistoryRow?.Commit.Hash is { } hash)
                await ShowCreateTagDialogAsync(hash, selectedCommit: true);
        };
        _commitActionsFlyout.Items.Insert(3, _createTagHereItem);
        _commitActionsFlyout.Opening += (_, _) =>
        {
            if (_createTagHereItem is not null)
                _createTagHereItem.IsEnabled = CanMutateTags() && _viewModel.SelectedHistoryRow is not null;
        };
    }

    private void ApplyTagOrderingToRepositoryTree()
    {
        SynchronizeRepositoryTree();
    }

    private void RepositoryTree_TagAwareRightTapped(object sender, RightTappedRoutedEventArgs args)
    {
        var source = args.OriginalSource as FrameworkElement;
        var node = ResolveNode(source?.DataContext);
        if (source is null || node is null)
        {
            RepositoryTree_RightTapped(sender, args);
            return;
        }

        if (node.Kind == RepositoryTreeNodeKind.Group && node.Name == "Tags")
        {
            var flyout = new MenuFlyout();
            AddMenuItem(flyout, "Create tag…", CanMutateTags(), () => ShowCreateTagDialogAsync("HEAD", selectedCommit: false));
            AddMenuItem(flyout, "Fetch tags…", CanMutateTags(), FetchTagsFromUiAsync);
            AddMenuItem(flyout, "Push all tags…", CanMutateTags(), PushAllTagsFromUiAsync);
            flyout.ShowAt(source, args.GetPosition(source));
            args.Handled = true;
            return;
        }

        if (node.Kind == RepositoryTreeNodeKind.Tag && node.Value is GitTag tag)
        {
            var flyout = new MenuFlyout();
            AddMenuItem(flyout, "Tag details…", true, () => ShowTagDetailsAsync(tag));
            AddMenuItem(flyout, "Show history up to tag", !_viewModel.IsBusy,
                () => ShowReferenceHistoryAsync($"refs/tags/{tag.Name}", $"Tag: {tag.Name}"));
            AddMenuItem(flyout, "Create branch from here…", CanMutateTags(),
                () => CreateBranchFromReferenceAsync($"refs/tags/{tag.Name}", tag.TargetCommit));
            AddMenuItem(flyout, "Checkout detached", CanMutateTags(), () => CheckoutTagAsync(tag));
            flyout.Items.Add(new MenuFlyoutSeparator());
            AddMenuItem(flyout, "Push tag…", CanMutateTags(), () => PushTagFromUiAsync(tag));
            AddMenuItem(flyout, "Delete from remote…", CanMutateTags(), () => DeleteRemoteTagFromUiAsync(tag));
            AddMenuItem(flyout, "Delete local tag…", CanMutateTags(), () => DeleteLocalTagFromUiAsync(tag));
            flyout.Items.Add(new MenuFlyoutSeparator());
            AddMenuItem(flyout, "Copy tag name", true, () => CopyTextAsync(tag.Name));
            flyout.ShowAt(source, args.GetPosition(source));
            args.Handled = true;
            return;
        }

        RepositoryTree_RightTapped(sender, args);
    }

    private bool CanMutateTags() =>
        _viewModel.Repository is not null &&
        !_viewModel.IsBusy &&
        _viewModel.CurrentOperation == RepositoryOperation.None;

    private async Task ShowCreateTagDialogAsync(string targetCommit, bool selectedCommit)
    {
        if (_viewModel.Repository is null || !CanMutateTags()) return;

        var name = new TextBox { Header = "Tag name", PlaceholderText = "v1.2.0" };
        var target = new TextBox
        {
            Header = "Target commit",
            Text = targetCommit,
            IsReadOnly = true,
            IsTabStop = false
        };
        var type = new ComboBox
        {
            Header = "Type",
            ItemsSource = new[] { GitTagKind.Annotated, GitTagKind.Lightweight },
            SelectedItem = GitTagKind.Annotated,
            MinWidth = 220
        };
        var message = new TextBox
        {
            Header = "Message",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 90,
            PlaceholderText = "Release/tag message"
        };
        type.SelectionChanged += (_, _) => message.IsEnabled = type.SelectedItem is GitTagKind.Annotated;

        var content = new StackPanel { Width = 460, Spacing = 10 };
        content.Children.Add(name);
        content.Children.Add(target);
        content.Children.Add(type);
        content.Children.Add(message);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = selectedCommit ? "Create tag here" : "Create tag",
            Content = content,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var kind = type.SelectedItem is GitTagKind selectedKind ? selectedKind : GitTagKind.Annotated;
        if (string.IsNullOrWhiteSpace(name.Text))
        {
            await ShowErrorAsync("Tag name required", "Enter a Git tag name.");
            return;
        }
        if (kind == GitTagKind.Annotated && string.IsNullOrWhiteSpace(message.Text))
        {
            await ShowErrorAsync("Tag message required", "Annotated tags require a non-empty message.");
            return;
        }

        await _viewModel.RunMutationAsync(
            () => _referenceService.CreateTagAsync(
                _viewModel.Repository,
                new CreateTagRequest(name.Text.Trim(), target.Text.Trim(), kind, kind == GitTagKind.Annotated ? message.Text : null)),
            "Could not create tag");
    }

    private async Task ShowTagDetailsAsync(GitTag tag)
    {
        var text = new StringBuilder()
            .AppendLine($"Name: {tag.Name}")
            .AppendLine($"Type: {(tag.Kind == GitTagKind.Annotated ? "Annotated" : "Lightweight")}")
            .AppendLine($"Target commit: {tag.TargetCommit}");
        if (tag.Kind == GitTagKind.Annotated)
        {
            text.AppendLine($"Tag object: {tag.TagObjectId}");
            var tagger = tag.TaggerName ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(tag.TaggerEmail))
                tagger = tagger.Length == 0 ? tag.TaggerEmail : $"{tagger} <{tag.TaggerEmail}>";
            text.AppendLine($"Tagger: {tagger}");
            text.AppendLine($"Date: {tag.TaggedAt?.ToString("u") ?? string.Empty}");
            text.AppendLine().AppendLine("Message:").Append(tag.Message ?? string.Empty);
        }

        var display = new TextBox
        {
            Text = text.ToString(),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinWidth = 520,
            MinHeight = tag.Kind == GitTagKind.Annotated ? 260 : 100
        };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Tag details",
            Content = display,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };
        await dialog.ShowAsync();
    }

    private async Task CheckoutTagAsync(GitTag tag)
    {
        if (_viewModel.Repository is null) return;
        await _viewModel.RunMutationAsync(
            () => _referenceService.CheckoutAsync(_viewModel.Repository, $"refs/tags/{tag.Name}"),
            "Could not checkout tag");
    }

    private async Task DeleteLocalTagFromUiAsync(GitTag tag)
    {
        if (_viewModel.Repository is null) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Delete local tag?",
            Content = $"Tag: {tag.Name}\nTarget commit: {tag.TargetCommit}\n\nThis deletes only the local tag.\nRemote tags are not deleted.",
            PrimaryButtonText = "Delete local tag",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        await _viewModel.RunMutationAsync(
            () => _referenceService.DeleteTagAsync(_viewModel.Repository, tag.Name),
            "Could not delete local tag");
    }

    private async Task PushTagFromUiAsync(GitTag tag)
    {
        if (_viewModel.Repository is null) return;
        var remote = await SelectTagRemoteAsync("Push tag", $"Push '{tag.Name}' to remote");
        if (remote is null) return;

        PushTagResult? result = null;
        var succeeded = await _viewModel.RunMutationAsync(
            async () => result = await _referenceService.PushTagAsync(_viewModel.Repository, remote.Name, tag.Name),
            "Could not push tag",
            includeHistory: false);
        if (!succeeded || result is null) return;

        if (result.Kind == PushTagResultKind.Conflict && result.Conflict is { } conflict)
        {
            await ConfirmForceUpdateRemoteTagAsync(conflict);
            return;
        }

        await ShowInformationAsync(
            result.Kind == PushTagResultKind.AlreadyUpToDate ? "Tag already up to date" : "Tag pushed",
            result.Message);
    }

    private async Task ConfirmForceUpdateRemoteTagAsync(RemoteTagConflictSnapshot snapshot)
    {
        if (_viewModel.Repository is null) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Force update remote tag?",
            Content =
                $"Remote: {snapshot.Remote}\n" +
                $"Tag: {snapshot.TagName}\n" +
                $"Current remote target: {snapshot.CurrentRemoteTarget}\n" +
                $"New local target: {snapshot.NewLocalTarget}\n\n" +
                "Moving an already published tag may break consumers relying on that release/tag.",
            PrimaryButtonText = "Force update remote tag",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        await _viewModel.RunMutationAsync(
            () => _referenceService.ForceUpdateRemoteTagAsync(_viewModel.Repository, snapshot),
            "Could not force update remote tag",
            includeHistory: false);
    }

    private async Task DeleteRemoteTagFromUiAsync(GitTag tag)
    {
        if (_viewModel.Repository is null) return;
        var remote = await SelectTagRemoteAsync("Delete remote tag", $"Delete '{tag.Name}' from remote");
        if (remote is null) return;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Delete remote tag?",
            Content = $"Remote: {remote.Name}\nTag: {tag.Name}\n\nThe local tag will remain.",
            PrimaryButtonText = "Delete remote tag",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        await _viewModel.RunMutationAsync(
            () => _referenceService.DeleteRemoteTagAsync(_viewModel.Repository, remote.Name, tag.Name),
            "Could not delete remote tag",
            includeHistory: false);
    }

    private async Task FetchTagsFromUiAsync()
    {
        if (_viewModel.Repository is null) return;
        var remote = await SelectTagRemoteAsync("Fetch tags", "Fetch tags from remote");
        if (remote is null) return;
        await _viewModel.RunMutationAsync(
            () => _referenceService.FetchTagsAsync(_viewModel.Repository, remote.Name),
            "Could not fetch tags");
    }

    private async Task PushAllTagsFromUiAsync()
    {
        if (_viewModel.Repository is null) return;
        var remote = await SelectTagRemoteAsync("Push all tags", "Push all local tags to remote");
        if (remote is null) return;
        await _viewModel.RunMutationAsync(
            () => _referenceService.PushAllTagsAsync(_viewModel.Repository, remote.Name),
            "Could not push all tags",
            includeHistory: false);
    }

    private async Task<GitRemote?> SelectTagRemoteAsync(string title, string prompt)
    {
        if (_viewModel.Remotes.Count == 0)
        {
            await ShowErrorAsync(title, "This repository has no configured remotes.");
            return null;
        }

        GitRemote? preferred = null;
        var upstream = _viewModel.LocalBranches.FirstOrDefault(branch => branch.IsCurrent)?.Upstream;
        if (!string.IsNullOrWhiteSpace(upstream))
        {
            var slash = upstream.IndexOf('/');
            var remoteName = slash > 0 ? upstream[..slash] : upstream;
            preferred = _viewModel.Remotes.FirstOrDefault(remote => string.Equals(remote.Name, remoteName, StringComparison.Ordinal));
        }
        if (preferred is null && _viewModel.Remotes.Count == 1) preferred = _viewModel.Remotes[0];

        var selector = new ComboBox
        {
            Header = "Remote",
            ItemsSource = _viewModel.Remotes,
            DisplayMemberPath = nameof(GitRemote.Name),
            SelectedItem = preferred,
            MinWidth = 320
        };
        var content = new StackPanel { Width = 400, Spacing = 10 };
        content.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(selector);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = content,
            PrimaryButtonText = "Continue",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        if (selector.SelectedItem is GitRemote remote) return remote;

        await ShowErrorAsync("Remote required", "Select the remote explicitly.");
        return null;
    }

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
        await dialog.ShowAsync();
    }
}