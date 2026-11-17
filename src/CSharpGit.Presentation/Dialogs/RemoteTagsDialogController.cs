using CSharpGit.Presentation.ViewModels;
using CSharpGit.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation.Dialogs;

/// <summary>
/// Owns the remote-tag browser dialog, including asynchronous remote switching,
/// stale-response fencing and explicit confirmation of destructive actions.
/// </summary>
internal sealed class RemoteTagsDialogController(
    OpenRepositoryViewModel viewModel,
    XamlRoot? xamlRoot,
    Func<ContentDialog, Task<ContentDialogResult>> showDialog,
    Func<bool> isShuttingDown,
    Action<Exception, string> reportFailure,
    Action<Func<Task>, string> runUiOperation)
{
    private readonly OpenRepositoryViewModel _viewModel = viewModel;
    private readonly XamlRoot? _xamlRoot = xamlRoot;
    private readonly Func<ContentDialog, Task<ContentDialogResult>> _showDialog = showDialog;
    private readonly Func<bool> _isShuttingDown = isShuttingDown;
    private readonly Action<Exception, string> _reportFailure = reportFailure;
    private readonly Action<Func<Task>, string> _runUiOperation = runUiOperation;

    internal async Task ShowAsync()
    {
        var repository = _viewModel.Repository;
        if (repository is null) return;
        if (_viewModel.RepositorySync.Remotes.Count == 0)
        {
            await ShowInformationAsync("Remote tags", "This repository has no configured remotes.");
            return;
        }

        var selectedRemote = _viewModel.Tags.PreferredRemote;

        while (!_isShuttingDown() && ReferenceEquals(repository, _viewModel.Repository))
        {
            IReadOnlyList<RemoteTagInfo> remoteTags = [];
            var remoteSelector = new ComboBox
            { Style = UiStyles.Resolve<Style>("CompactComboBoxStyle"),
                Header = "Remote",
                ItemsSource = _viewModel.RepositorySync.Remotes,
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
                XamlRoot = _xamlRoot,
                Title = "Remote tags",
                Content = content,
                PrimaryButtonText = "Delete…",
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Close,
                IsPrimaryButtonEnabled = false
            };

            void UpdateDeleteAvailability() =>
                dialog.IsPrimaryButtonEnabled =
                    _viewModel.Tags.CanMutate &&
                    list.SelectedIndex >= 0 &&
                    list.SelectedIndex < remoteTags.Count;

            var dialogActive = true;
            var lookupRevision = 0;
            async Task LoadRemoteTagsAsync()
            {
                var revision = ++lookupRevision;
                if (!dialogActive || _isShuttingDown() || !ReferenceEquals(repository, _viewModel.Repository))
                    return;
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
                    var query = await _viewModel.Tags.ReadRemoteTagsAsync(repository, remote);
                    if (!dialogActive || _isShuttingDown() || revision != lookupRevision
                        || !ReferenceEquals(repository, _viewModel.Repository)) return;
                    if (!query.Succeeded)
                    {
                        remoteTags = [];
                        list.ItemsSource = null;
                        status.Text = $"Could not read remote tags: {query.ErrorMessage}";
                        return;
                    }

                    remoteTags = query.Value;
                    list.ItemsSource = remoteTags
                        .Select(remoteTag =>
                            $"{remoteTag.Name}    {(remoteTag.IsAnnotated ? "annotated" : "lightweight")}    " +
                            $"{remoteTag.TargetCommit[..Math.Min(10, remoteTag.TargetCommit.Length)]}")
                        .ToArray();
                    list.SelectedIndex = remoteTags.Count > 0 ? 0 : -1;
                    status.Text = remoteTags.Count == 0 ? "No tags on this remote." : $"{remoteTags.Count} tag(s).";
                }
                catch (OperationCanceledException) when (_isShuttingDown() || !dialogActive)
                {
                }
                catch (Exception exception)
                {
                    _reportFailure(exception, "Remote tag lookup");
                    if (dialogActive && !_isShuttingDown() && revision == lookupRevision
                        && ReferenceEquals(repository, _viewModel.Repository))
                        status.Text = "Could not read remote tags.";
                }
                finally
                {
                    if (dialogActive && !_isShuttingDown() && revision == lookupRevision
                        && ReferenceEquals(repository, _viewModel.Repository))
                    {
                        remoteSelector.IsEnabled = true;
                        list.IsEnabled = true;
                        UpdateDeleteAvailability();
                    }
                }
            }

            remoteSelector.SelectionChanged += (_, _) =>
                _runUiOperation(LoadRemoteTagsAsync, "Remote tag lookup");
            list.SelectionChanged += (_, _) => UpdateDeleteAvailability();
            if (selectedRemote is not null) await LoadRemoteTagsAsync();
            else status.Text = "Select a remote.";

            ContentDialogResult result;
            try { result = await _showDialog(dialog); }
            finally { dialogActive = false; }

            if (_isShuttingDown() || !ReferenceEquals(repository, _viewModel.Repository)) return;
            selectedRemote = remoteSelector.SelectedItem as GitRemote;
            if (result != ContentDialogResult.Primary) return;
            if (list.SelectedIndex < 0 || list.SelectedIndex >= remoteTags.Count) continue;

            var selectedTag = remoteTags[list.SelectedIndex];
            var confirmation = new ContentDialog
            {
                XamlRoot = _xamlRoot,
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
            if (await _showDialog(confirmation) != ContentDialogResult.Primary) continue;
            if (_isShuttingDown() || !ReferenceEquals(repository, _viewModel.Repository) || !_viewModel.Tags.CanMutate)
                return;

            await _viewModel.Tags.DeleteRemoteTagAsync(repository, selectedTag);
        }
    }


    private async Task ShowInformationAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = _xamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };
        await _showDialog(dialog);
    }
}
