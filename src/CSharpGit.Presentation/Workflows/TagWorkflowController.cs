using System.Text;
using CSharpGit.Domain;
using CSharpGit.Presentation.Dialogs;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation.Workflows;

// Owns tag dialogues and tag interaction flow; Git mutations belong to TagsViewModel.
internal sealed class TagWorkflowController(
    OpenRepositoryViewModel viewModel,
    Func<XamlRoot?> xamlRoot,
    Func<ContentDialog, Task<ContentDialogResult>> showDialog,
    Func<bool> isShuttingDown,
    Func<string, string, Task> showError,
    Func<string, string, Task> showInformation,
    Action<Exception, string> reportUiFailure,
    Action<Func<Task>, string> runUiOperation)
{
    private readonly OpenRepositoryViewModel _viewModel = viewModel;
    private readonly Func<XamlRoot?> _xamlRoot = xamlRoot;
    private readonly Func<ContentDialog, Task<ContentDialogResult>> _showDialog = showDialog;
    private readonly Func<bool> _isShuttingDown = isShuttingDown;
    private readonly Func<string, string, Task> _showError = showError;
    private readonly Func<string, string, Task> _showInformation = showInformation;
    private readonly Action<Exception, string> _reportUiFailure = reportUiFailure;
    private readonly Action<Func<Task>, string> _runUiOperation = runUiOperation;
    private bool CanMutateTags() => _viewModel.Tags.CanMutate;

    internal async Task ShowCreateTagDialogAsync(string targetCommit, bool selectedCommit)
    {
        var repository = _viewModel.Repository;
        if (repository is null || !CanMutateTags()) return;

        var name = new TextBox { Style = UiStyles.Resolve<Style>("CompactTextBoxStyle"), Header = "Tag name", PlaceholderText = "v1.2.0" };
        var target = new TextBox
        { Style = UiStyles.Resolve<Style>("CompactTextBoxStyle"),
            Header = "Target commit",
            Text = targetCommit,
            IsReadOnly = true,
            IsTabStop = false
        };
        var type = new ComboBox
        { Style = UiStyles.Resolve<Style>("CompactComboBoxStyle"),
            Header = "Type",
            ItemsSource = new[] { GitTagKind.Annotated, GitTagKind.Lightweight },
            SelectedItem = GitTagKind.Annotated,
            MinWidth = 220
        };
        var message = new TextBox
        { Style = UiStyles.Resolve<Style>("CompactMultilineTextBoxStyle"),
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
            XamlRoot = _xamlRoot(),
            Title = selectedCommit ? "Create tag here" : "Create tag",
            Content = content,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await _showDialog(dialog) != ContentDialogResult.Primary
            || _isShuttingDown() || !ReferenceEquals(repository, _viewModel.Repository)
            || !CanMutateTags()) return;

        var kind = type.SelectedItem is GitTagKind selectedKind ? selectedKind : GitTagKind.Annotated;
        if (string.IsNullOrWhiteSpace(name.Text))
        {
            await _showError("Tag name required", "Enter a Git tag name.");
            return;
        }
        if (kind == GitTagKind.Annotated && string.IsNullOrWhiteSpace(message.Text))
        {
            await _showError("Tag message required", "Annotated tags require a non-empty message.");
            return;
        }

        await _viewModel.Tags.CreateTagAsync(
            repository,
            new CreateTagRequest(
                name.Text.Trim(),
                target.Text.Trim(),
                kind,
                kind == GitTagKind.Annotated ? message.Text : null));
    }

    internal async Task ShowTagDetailsAsync(GitTag tag)
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
        { Style = UiStyles.Resolve<Style>("CompactMultilineTextBoxStyle"),
            Text = text.ToString(),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinWidth = 520,
            MinHeight = tag.Kind == GitTagKind.Annotated ? 260 : 100
        };
        var dialog = new ContentDialog
        {
            XamlRoot = _xamlRoot(),
            Title = "Tag details",
            Content = display,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };
        await _showDialog(dialog);
    }

    internal async Task DeleteTagFromUiAsync(GitTag tag)
    {
        var repository = _viewModel.Repository;
        if (repository is null || !CanMutateTags()) return;

        var hasRemotes = _viewModel.RepositorySync.Remotes.Count > 0;
        var deleteRemote = new CheckBox
        { Style = UiStyles.Resolve<Style>("CompactCheckBoxStyle"),
            Content = "Also delete this tag from remote",
            IsChecked = hasRemotes,
            IsEnabled = hasRemotes
        };
        var remoteSelector = new ComboBox
        { Style = UiStyles.Resolve<Style>("CompactComboBoxStyle"),
            Header = "Remote",
            ItemsSource = _viewModel.RepositorySync.Remotes,
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
            XamlRoot = _xamlRoot(),
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

        if (await _showDialog(dialog) != ContentDialogResult.Primary
            || _isShuttingDown() || !ReferenceEquals(repository, _viewModel.Repository)
            || !CanMutateTags()) return;

        var selectedRemote = deleteRemote.IsChecked == true
            ? remoteSelector.SelectedItem as GitRemote
            : null;
        if (deleteRemote.IsChecked == true && selectedRemote is null) return;

        var result = await _viewModel.Tags.DeleteTagAsync(repository, tag, selectedRemote);
        if (result.Succeeded && result.RemoteWasMissing && result.RemoteName is { } remoteName)
            await _showInformation(
                "Tag deleted",
                $"The local tag was deleted.\nThe tag did not exist on '{remoteName}'.");
    }

    internal async Task PushTagFromUiAsync(GitTag tag)
    {
        var repository = _viewModel.Repository;
        if (repository is null) return;
        var remote = await SelectTagRemoteAsync("Push tag", $"Push '{tag.Name}' to remote");
        if (remote is null || _isShuttingDown()
            || !ReferenceEquals(repository, _viewModel.Repository) || !CanMutateTags()) return;

        var result = await _viewModel.Tags.PushTagAsync(repository, tag, remote);
        if (result is null) return;

        if (result.Kind == PushTagResultKind.Conflict && result.Conflict is { } conflict)
        {
            await ConfirmForceUpdateRemoteTagAsync(repository, conflict);
            return;
        }

        await _showInformation(
            result.Kind == PushTagResultKind.AlreadyUpToDate ? "Tag already up to date" : "Tag pushed",
            result.Message);
    }

    private async Task ConfirmForceUpdateRemoteTagAsync(
        Repository repository,
        RemoteTagConflictSnapshot snapshot)
    {
        if (!ReferenceEquals(_viewModel.Repository, repository)) return;
        var dialog = new ContentDialog
        {
            XamlRoot = _xamlRoot(),
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
        if (await _showDialog(dialog) != ContentDialogResult.Primary
            || _isShuttingDown() || !ReferenceEquals(repository, _viewModel.Repository)
            || !CanMutateTags()) return;

        await _viewModel.Tags.ForceUpdateRemoteTagAsync(repository, snapshot);
    }

    internal async Task DeleteRemoteTagFromUiAsync(GitTag tag)
    {
        var repository = _viewModel.Repository;
        if (repository is null || !CanMutateTags()) return;
        var remote = await SelectTagRemoteAsync("Delete remote tag", $"Delete '{tag.Name}' from remote");
        if (remote is null || _isShuttingDown()
            || !ReferenceEquals(repository, _viewModel.Repository) || !CanMutateTags()) return;

        var lookup = await _viewModel.Tags.ReadRemoteTagAsync(repository, remote, tag.Name);
        if (_isShuttingDown() || !ReferenceEquals(repository, _viewModel.Repository))
            return;
        if (!lookup.Succeeded)
        {
            await _showError("Could not read remote tag", lookup.ErrorMessage ?? "Unknown error.");
            return;
        }

        var remoteTag = lookup.Value;
        if (remoteTag is null)
        {
            await _showInformation("Remote tag not found", $"Tag '{tag.Name}' does not exist on '{remote.Name}'.");
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = _xamlRoot(),
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
        if (await _showDialog(dialog) != ContentDialogResult.Primary
            || _isShuttingDown() || !ReferenceEquals(repository, _viewModel.Repository)
            || !CanMutateTags()) return;

        await _viewModel.Tags.DeleteRemoteTagAsync(repository, remoteTag);
    }

    internal Task ShowRemoteTagsAsync() =>
        new RemoteTagsDialogController(
            _viewModel,
            _xamlRoot()!,
            _showDialog,
            _isShuttingDown,
            _reportUiFailure,
            (operation, description) => _runUiOperation(operation, description))
        .ShowAsync();

    internal async Task FetchTagsFromUiAsync()
    {
        var repository = _viewModel.Repository;
        if (repository is null) return;
        var remote = await SelectTagRemoteAsync("Fetch tags", "Fetch tags from remote");
        if (remote is null || _isShuttingDown() || !ReferenceEquals(repository, _viewModel.Repository))
            return;
        await _viewModel.Tags.FetchTagsAsync(repository, remote);
    }

    internal async Task PushAllTagsFromUiAsync()
    {
        var repository = _viewModel.Repository;
        if (repository is null) return;
        var remote = await SelectTagRemoteAsync("Push all tags", "Push all local tags to remote");
        if (remote is null || _isShuttingDown() || !ReferenceEquals(repository, _viewModel.Repository))
            return;
        await _viewModel.Tags.PushAllTagsAsync(repository, remote);
    }

    private GitRemote? GetPreferredTagRemote() => _viewModel.Tags.PreferredRemote;

    private async Task<GitRemote?> SelectTagRemoteAsync(string title, string prompt)
    {
        if (_viewModel.RepositorySync.Remotes.Count == 0)
        {
            await _showError(title, "This repository has no configured remotes.");
            return null;
        }

        var preferred = GetPreferredTagRemote();

        var selector = new ComboBox
        { Style = UiStyles.Resolve<Style>("CompactComboBoxStyle"),
            Header = "Remote",
            ItemsSource = _viewModel.RepositorySync.Remotes,
            DisplayMemberPath = nameof(GitRemote.Name),
            SelectedItem = preferred,
            MinWidth = 320
        };
        var content = new StackPanel { Width = 400, Spacing = 10 };
        content.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(selector);
        var dialog = new ContentDialog
        {
            XamlRoot = _xamlRoot(),
            Title = title,
            Content = content,
            PrimaryButtonText = "Continue",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await _showDialog(dialog) != ContentDialogResult.Primary) return null;
        if (selector.SelectedItem is GitRemote remote) return remote;

        await _showError("Remote required", "Select the remote explicitly.");
        return null;
    }


}
