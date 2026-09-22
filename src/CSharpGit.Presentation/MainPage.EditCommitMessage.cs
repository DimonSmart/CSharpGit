using CSharpGit.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private string? _pendingEditedCommitHash;
    private string? _pendingOriginalEditedCommitHash;

    private bool ShouldRestorePendingEditedCommitSelection =>
        _pendingOriginalEditedCommitHash is not null &&
        _viewModel.CurrentOperation == RepositoryOperation.None;

    private async void EditCommitMessage_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetCommitActionContext(out var repository, out var commit)) return;

        var originalMessage = commit.Message;
        var messageBox = new TextBox
        {
            Text = originalMessage,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinWidth = 520,
            MinHeight = 180,
            MaxHeight = 420
        };

        var isHead = _viewModel.LocalBranches.Any(branch =>
            branch.IsCurrent &&
            string.Equals(branch.Commit, commit.Hash, StringComparison.Ordinal));
        var warning = new TextBlock
        {
            Text = isHead
                ? "Changing the message will replace the current HEAD commit."
                : "Changing this commit rewrites Git history.\n" +
                  "Commit hashes from this point to HEAD may change.",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 560
        };

        var content = new StackPanel { Width = 560, Spacing = 12 };
        content.Children.Add(messageBox);
        content.Children.Add(warning);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Edit commit message",
            Content = content,
            PrimaryButtonText = "Change message",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        void UpdatePrimaryButton()
        {
            var candidate = messageBox.Text;
            dialog.IsPrimaryButtonEnabled =
                !string.IsNullOrWhiteSpace(candidate) &&
                !string.Equals(originalMessage, candidate, StringComparison.Ordinal) &&
                !_viewModel.IsBusy &&
                _viewModel.CurrentOperation == RepositoryOperation.None;
        }

        messageBox.TextChanged += (_, _) => UpdatePrimaryButton();
        UpdatePrimaryButton();

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var newMessage = messageBox.Text;
        if (string.IsNullOrWhiteSpace(newMessage) ||
            string.Equals(originalMessage, newMessage, StringComparison.Ordinal))
            return;
        if (!ReferenceEquals(repository, _viewModel.Repository) ||
            _viewModel.IsBusy ||
            _viewModel.CurrentOperation != RepositoryOperation.None)
            return;

        EditCommitMessageResult? result = null;
        var succeeded = await _viewModel.RunMutationAsync(
            async () => result = await _commitActionService.EditCommitMessageAsync(
                repository,
                commit.Hash,
                newMessage),
            "Could not edit commit message");
        if (!succeeded || result is null) return;

        switch (result.Kind)
        {
            case EditCommitMessageResultKind.Completed:
                ClearPendingEditedCommitSelection();
                if (!await RestoreCommitActionSelectionAsync(result.NewCommit))
                    await RestoreCommitActionSelectionAsync(result.OldCommit);
                break;

            case EditCommitMessageResultKind.Conflicts:
                _pendingEditedCommitHash = result.NewCommit;
                _pendingOriginalEditedCommitHash = result.OldCommit;
                break;

            case EditCommitMessageResultKind.Failed:
                ClearPendingEditedCommitSelection();
                await ShowErrorAsync("Commit message was not changed", result.Message);
                break;
        }
    }

    private async Task RestorePendingEditedCommitSelectionAsync()
    {
        if (!ShouldRestorePendingEditedCommitSelection) return;

        var rewritten = _pendingEditedCommitHash;
        var original = _pendingOriginalEditedCommitHash;
        ClearPendingEditedCommitSelection();

        if (await RestoreCommitActionSelectionAsync(rewritten)) return;
        await RestoreCommitActionSelectionAsync(original);
    }

    private void ClearPendingEditedCommitSelection()
    {
        _pendingEditedCommitHash = null;
        _pendingOriginalEditedCommitHash = null;
    }
}
