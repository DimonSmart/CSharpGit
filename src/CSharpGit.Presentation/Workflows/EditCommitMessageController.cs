using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation.Workflows;

// Owns commit-message rewrite dialog and pending selection across a rebase conflict.
internal sealed class EditCommitMessageController(
    OpenRepositoryViewModel viewModel,
    Func<XamlRoot?> xamlRoot,
    Func<ContentDialog, Task<ContentDialogResult>> showDialog,
    Func<bool> isShuttingDown,
    Func<CommitActionExecutionResult, Task> handleResult,
    Func<string?, Task<bool>> restoreSelection)
{
    private readonly OpenRepositoryViewModel _viewModel = viewModel;
    private readonly Func<XamlRoot?> _xamlRoot = xamlRoot;
    private readonly Func<ContentDialog, Task<ContentDialogResult>> _showDialog = showDialog;
    private readonly Func<bool> _isShuttingDown = isShuttingDown;
    private readonly Func<CommitActionExecutionResult, Task> _handleResult = handleResult;
    private readonly Func<string?, Task<bool>> _restoreSelection = restoreSelection;

    private string? _pendingEditedCommitHash;
    private string? _pendingOriginalEditedCommitHash;

    internal bool ShouldRestorePendingEditedCommitSelection =>
        _pendingOriginalEditedCommitHash is not null &&
        _viewModel.RepositoryOperations.CurrentOperation == RepositoryOperation.None;

    internal async Task EditAsync()
    {
        var repository = _viewModel.Repository;
        var commit = _viewModel.History.SelectedRow?.Commit;
        if (_isShuttingDown() || repository is null || commit is null
            || !_viewModel.CommitActions.CanMutateCommit(commit)) return;

        var originalMessage = commit.Message;
        var messageBox = new TextBox
        { Style = UiStyles.Resolve<Style>("CompactMultilineTextBoxStyle"),
            Text = originalMessage,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinWidth = 520,
            MinHeight = 180,
            MaxHeight = 420
        };

        var isHead = _viewModel.Branches.LocalBranches.Any(branch =>
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
            XamlRoot = _xamlRoot(),
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
                _viewModel.RepositoryOperations.CurrentOperation == RepositoryOperation.None;
        }

        messageBox.TextChanged += (_, _) => UpdatePrimaryButton();
        UpdatePrimaryButton();

        if (await _showDialog(dialog) != ContentDialogResult.Primary) return;

        var newMessage = messageBox.Text;
        if (string.IsNullOrWhiteSpace(newMessage) ||
            string.Equals(originalMessage, newMessage, StringComparison.Ordinal))
            return;
        if (!ReferenceEquals(repository, _viewModel.Repository) ||
            !_viewModel.CommitActions.CanMutateCommit(commit))
            return;

        var result = await _viewModel.CommitActions.EditCommitMessageAsync(
            repository, commit, newMessage);
        if (result.ConflictOldCommit is not null)
        {
            _pendingEditedCommitHash = result.ConflictNewCommit;
            _pendingOriginalEditedCommitHash = result.ConflictOldCommit;
            return;
        }

        ClearPendingEditedCommitSelection();
        await _handleResult(result);
    }

    internal async Task RestorePendingEditedCommitSelectionAsync()
    {
        if (!ShouldRestorePendingEditedCommitSelection) return;

        var rewritten = _pendingEditedCommitHash;
        var original = _pendingOriginalEditedCommitHash;
        ClearPendingEditedCommitSelection();

        if (await _restoreSelection(rewritten)) return;
        await _restoreSelection(original);
    }

    private void ClearPendingEditedCommitSelection()
    {
        _pendingEditedCommitHash = null;
        _pendingOriginalEditedCommitHash = null;
    }
}
