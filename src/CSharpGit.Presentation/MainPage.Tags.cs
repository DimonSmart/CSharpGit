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
    private MenuFlyoutSubItem? _deleteTagSubItem;

    private void InitializeTagSupportIfNeeded()
    {
        if (_tagSupportInitialized) return;
        _tagSupportInitialized = true;

        if (_commitActionsFlyout is null) return;
        _createTagHereItem = new MenuFlyoutItem { Text = "Create tag here…" };
        _createTagHereItem.Click += async (_, _) =>
        {
            if (_viewModel.SelectedHistoryRow?.Commit.Hash is { } hash)
                await ShowCreateTagDialogAsync(hash, selectedCommit: true);
        };
        _commitActionsFlyout.Items.Insert(3, _createTagHereItem);

        _deleteTagSubItem = new MenuFlyoutSubItem { Text = "Delete tag" };
        var checkoutIndex = _commitActionsFlyout.Items.IndexOf(_checkoutCommitItem);
        _commitActionsFlyout.Items.Insert(checkoutIndex + 1, _deleteTagSubItem);

        _commitActionsFlyout.Opening += (_, _) =>
        {
            if (_createTagHereItem is not null)
                _createTagHereItem.IsEnabled = CanMutateTags() && _viewModel.SelectedHistoryRow is not null;
            UpdateDeleteTagSubmenu();
        };
    }

    private void UpdateDeleteTagSubmenu()
    {
        if (_deleteTagSubItem is null) return;

        _deleteTagSubItem.Items.Clear();

        var selectedCommitHash = _viewModel.SelectedHistoryRow?.Commit.Hash;
        if (string.IsNullOrWhiteSpace(selectedCommitHash))
        {
            _deleteTagSubItem.IsEnabled = false;
            return;
        }

        var canMutate = CanMutateTags();
        foreach (var tag in _viewModel.Tags)
        {
            if (!string.Equals(tag.TargetCommit, selectedCommitHash, StringComparison.Ordinal)) continue;

            var item = new MenuFlyoutItem
            {
                Text = tag.Name,
                IsEnabled = canMutate
            };
            item.Click += async (_, _) => await DeleteTagFromUiAsync(tag);
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
            AddMenuItem(flyout, "Create tag…", CanMutateTags(), () => ShowCreateTagDialogAsync("HEAD", selectedCommit: false));
            AddMenuItem(flyout, "Fetch tags…", CanMutateTags(), FetchTagsFromUiAsync);
            AddMenuItem(flyout, "Remote tags…", CanMutateTags(), ShowRemoteTagsAsync);
            AddMenuItem(flyout, "Push all tags…", CanMutateTags(), PushAllTagsFromUiAsync);
            flyout.ShowAt(source, args.GetPosition(source));
            args.Handled = true;
            return true;
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
            AddMenuItem(flyout, "Delete tag…", CanMutateTags(), () => DeleteTagFromUiAsync(tag));
            flyout.Items.Add(new MenuFlyoutSeparator());
            AddMenuItem(flyout, "Copy tag name", true, () => CopyTextAsync(tag.Name));
            flyout.ShowAt(source, args.GetPosition(source));
            args.Handled = true;
            return true;
        }

        return false;
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
            () => _tagService.CreateTagAsync(
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

    private async Task DeleteTagFromUiAsync(GitTag tag)
    {
        if (_viewModel.Repository is null || !CanMutateTags()) return;

        var repository = _viewModel.Repository;
        var hasRemotes = _viewModel.Remotes.Count > 0;
        var deleteRemote = new CheckBox
        {
            Content = "Also delete this tag from remote",
            IsChecked = hasRemotes,
            IsEnabled = hasRemotes
        };
        var remoteSelector = new ComboBox
        {
            Header = "Remote",
            ItemsSource = _viewModel.Remotes,
            DisplayMemberPath = nameof(GitRemote.Name),
            SelectedItem = GetPreferredTagRemote(),
            IsEnabled = hasRemotes,
            MinWidth = 320
        };
        var content = new StackPanel { Width = 440, Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = $"Tag: {tag.Name}\nTarget commit: {tag.TargetCommit}",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(deleteRemote);
        content.Children.Add(remoteSelector);
        content.Children.Add(new TextBlock
        {
            Text = "This will delete the local tag and, if selected, the corresponding tag from the selected remote.",
            TextWrapping = TextWrapping.Wrap
        });

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Delete tag?",
            Content = content,
            PrimaryButtonText = "Delete tag",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        void UpdateDeleteAvailability()
        {
            var deleteRemoteRequested = deleteRemote.IsChecked == true;
            remoteSelector.IsEnabled = hasRemotes && deleteRemoteRequested;
            dialog.IsPrimaryButtonEnabled = !deleteRemoteRequested || remoteSelector.SelectedItem is GitRemote;
        }

        deleteRemote.Checked += (_, _) => UpdateDeleteAvailability();
        deleteRemote.Unchecked += (_, _) => UpdateDeleteAvailability();
        remoteSelector.SelectionChanged += (_, _) => UpdateDeleteAvailability();
        UpdateDeleteAvailability();

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        if (deleteRemote.IsChecked != true)
        {
            await _viewModel.RunMutationAsync(
                () => _tagService.DeleteTagAsync(repository, tag.Name),
                "Could not delete tag");
            return;
        }

        if (remoteSelector.SelectedItem is not GitRemote remote) return;

        var remoteMissing = false;
        var succeeded = await _viewModel.RunMutationAsync(
            async () =>
            {
                var remoteTag = await _tagService.ReadRemoteTagAsync(repository, remote.Name, tag.Name);
                if (remoteTag is null)
                {
                    remoteMissing = true;
                    await _tagService.DeleteTagAsync(repository, tag.Name);
                    return;
                }

                if (!string.Equals(remoteTag.ObjectId, tag.ObjectId, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"The remote tag '{tag.Name}' no longer matches the local tag.\n\n" +
                        $"Local object: {tag.ObjectId}\nRemote object: {remoteTag.ObjectId}\n\n" +
                        "Nothing was deleted. Refresh and review the tags before retrying.");

                await _tagService.DeleteRemoteTagAsync(repository, remoteTag);
                await _tagService.DeleteTagAsync(repository, tag.Name);
            },
            "Could not delete tag");

        if (succeeded && remoteMissing)
            await ShowInformationAsync(
                "Tag deleted",
                $"The local tag was deleted.\nThe tag did not exist on '{remote.Name}'.");
    }

    private async Task PushTagFromUiAsync(GitTag tag)
    {
        if (_viewModel.Repository is null) return;
        var remote = await SelectTagRemoteAsync("Push tag", $"Push '{tag.Name}' to remote");
        if (remote is null) return;

        PushTagResult? result = null;
        var succeeded = await _viewModel.RunMutationAsync(
            async () => result = await _tagService.PushTagAsync(_viewModel.Repository, remote.Name, tag.Name),
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
            () => _tagService.ForceUpdateRemoteTagAsync(_viewModel.Repository, snapshot),
            "Could not force update remote tag",
            includeHistory: false);
    }

    private async Task DeleteRemoteTagFromUiAsync(GitTag tag)
    {
        if (_viewModel.Repository is null || !CanMutateTags()) return;
        var repository = _viewModel.Repository;
        var remote = await SelectTagRemoteAsync("Delete remote tag", $"Delete '{tag.Name}' from remote");
        if (remote is null) return;

        RemoteTagInfo? remoteTag;
        try
        {
            remoteTag = await _tagService.ReadRemoteTagAsync(repository, remote.Name, tag.Name);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ShowErrorAsync("Could not read remote tag", exception.Message);
            return;
        }

        if (remoteTag is null)
        {
            await ShowInformationAsync("Remote tag not found", $"Tag '{tag.Name}' does not exist on '{remote.Name}'.");
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Delete remote tag?",
            Content =
                $"Remote: {remoteTag.Remote}\n" +
                $"Tag: {remoteTag.Name}\n" +
                $"Target: {remoteTag.TargetCommit}\n" +
                $"Object: {remoteTag.ObjectId}\n\n" +
                "The local tag will remain.",
            PrimaryButtonText = "Delete remote tag",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        await _viewModel.RunMutationAsync(
            () => _tagService.DeleteRemoteTagAsync(repository, remoteTag),
            "Could not delete remote tag",
            includeHistory: false);
    }

    private async Task ShowRemoteTagsAsync()
    {
        if (_viewModel.Repository is null) return;
        if (_viewModel.Remotes.Count == 0)
        {
            await ShowInformationAsync("Remote tags", "This repository has no configured remotes.");
            return;
        }

        var repository = _viewModel.Repository;
        var selectedRemote = GetPreferredTagRemote();

        while (true)
        {
            IReadOnlyList<RemoteTagInfo> remoteTags = [];
            var remoteSelector = new ComboBox
            {
                Header = "Remote",
                ItemsSource = _viewModel.Remotes,
                DisplayMemberPath = nameof(GitRemote.Name),
                SelectedItem = selectedRemote,
                MinWidth = 320
            };
            var list = new ListView
            {
                MinHeight = 280,
                MaxHeight = 420,
                SelectionMode = ListViewSelectionMode.Single
            };
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var content = new StackPanel { Width = 520, Spacing = 10 };
            content.Children.Add(remoteSelector);
            content.Children.Add(status);
            content.Children.Add(list);

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Remote tags",
                Content = content,
                PrimaryButtonText = "Delete…",
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Close,
                IsPrimaryButtonEnabled = false
            };

            void UpdateDeleteAvailability() =>
                dialog.IsPrimaryButtonEnabled =
                    CanMutateTags() &&
                    list.SelectedIndex >= 0 &&
                    list.SelectedIndex < remoteTags.Count;

            async Task LoadRemoteTagsAsync()
            {
                if (remoteSelector.SelectedItem is not GitRemote remote)
                {
                    remoteTags = [];
                    list.ItemsSource = null;
                    status.Text = "Select a remote.";
                    UpdateDeleteAvailability();
                    return;
                }

                selectedRemote = remote;
                remoteSelector.IsEnabled = false;
                list.IsEnabled = false;
                dialog.IsPrimaryButtonEnabled = false;
                status.Text = "Loading remote tags…";
                try
                {
                    remoteTags = await _tagService.ReadRemoteTagsAsync(repository, remote.Name);
                    list.ItemsSource = remoteTags
                        .Select(remoteTag =>
                            $"{remoteTag.Name}    {(remoteTag.IsAnnotated ? "annotated" : "lightweight")}    " +
                            $"{remoteTag.TargetCommit[..Math.Min(10, remoteTag.TargetCommit.Length)]}")
                        .ToArray();
                    list.SelectedIndex = remoteTags.Count > 0 ? 0 : -1;
                    status.Text = remoteTags.Count == 0 ? "No tags on this remote." : $"{remoteTags.Count} tag(s).";
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    remoteTags = [];
                    list.ItemsSource = null;
                    status.Text = $"Could not read remote tags: {exception.Message}";
                }
                finally
                {
                    remoteSelector.IsEnabled = true;
                    list.IsEnabled = true;
                    UpdateDeleteAvailability();
                }
            }

            remoteSelector.SelectionChanged += async (_, _) => await LoadRemoteTagsAsync();
            list.SelectionChanged += (_, _) => UpdateDeleteAvailability();
            if (selectedRemote is not null) await LoadRemoteTagsAsync();
            else status.Text = "Select a remote.";

            var result = await dialog.ShowAsync();
            selectedRemote = remoteSelector.SelectedItem as GitRemote;
            if (result != ContentDialogResult.Primary) return;
            if (list.SelectedIndex < 0 || list.SelectedIndex >= remoteTags.Count) continue;

            var selectedTag = remoteTags[list.SelectedIndex];
            var confirmation = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Delete remote tag?",
                Content =
                    $"Remote: {selectedTag.Remote}\n" +
                    $"Tag: {selectedTag.Name}\n" +
                    $"Target: {selectedTag.TargetCommit}\n" +
                    $"Object: {selectedTag.ObjectId}",
                PrimaryButtonText = "Delete remote tag",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };
            if (await confirmation.ShowAsync() != ContentDialogResult.Primary) continue;

            await _viewModel.RunMutationAsync(
                () => _tagService.DeleteRemoteTagAsync(repository, selectedTag),
                "Could not delete remote tag",
                includeHistory: false);
        }
    }

    private async Task FetchTagsFromUiAsync()
    {
        if (_viewModel.Repository is null) return;
        var remote = await SelectTagRemoteAsync("Fetch tags", "Fetch tags from remote");
        if (remote is null) return;
        await _viewModel.RunMutationAsync(
            () => _tagService.FetchTagsAsync(_viewModel.Repository, remote.Name),
            "Could not fetch tags");
    }

    private async Task PushAllTagsFromUiAsync()
    {
        if (_viewModel.Repository is null) return;
        var remote = await SelectTagRemoteAsync("Push all tags", "Push all local tags to remote");
        if (remote is null) return;
        await _viewModel.RunMutationAsync(
            () => _tagService.PushAllTagsAsync(_viewModel.Repository, remote.Name),
            "Could not push all tags",
            includeHistory: false);
    }

    private GitRemote? GetPreferredTagRemote()
    {
        GitRemote? preferred = null;
        var upstream = _viewModel.LocalBranches.FirstOrDefault(branch => branch.IsCurrent)?.Upstream;
        if (!string.IsNullOrWhiteSpace(upstream))
        {
            var slash = upstream.IndexOf('/');
            var remoteName = slash > 0 ? upstream[..slash] : upstream;
            preferred = _viewModel.Remotes.FirstOrDefault(
                remote => string.Equals(remote.Name, remoteName, StringComparison.Ordinal));
        }

        if (preferred is null && _viewModel.Remotes.Count == 1)
            preferred = _viewModel.Remotes[0];

        return preferred;
    }

    private async Task<GitRemote?> SelectTagRemoteAsync(string title, string prompt)
    {
        if (_viewModel.Remotes.Count == 0)
        {
            await ShowErrorAsync(title, "This repository has no configured remotes.");
            return null;
        }

        var preferred = GetPreferredTagRemote();

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