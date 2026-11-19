using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation.Windowing;

/// <summary>
/// Owns commit-draft close confirmation. The window's dialog gate supplies XamlRoot
/// and serializes modal dialogs until the window dialog host is extracted.
/// </summary>
internal sealed class CommitDraftCloseConfirmationDialog(
    OpenRepositoryViewModel repository,
    Func<ContentDialog, Task<ContentDialogResult>> showDialog,
    Func<bool> isShuttingDown) : ICloseConfirmationDialog
{
    public async Task<bool> ConfirmDiscardCommitMessageAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || isShuttingDown()) return false;
        if (!repository.CommitCreation.HasUnappliedCommitMessage) return true;

        var originalRepository = repository.Repository;
        var dialog = new ContentDialog
        {
            Title = "Discard commit message?",
            Content = "The commit message has not been applied. Close the window and discard it?",
            PrimaryButtonText = "Discard and close",
            CloseButtonText = "Keep editing",
            DefaultButton = ContentDialogButton.Close
        };

        var result = await showDialog(dialog);
        return result == ContentDialogResult.Primary
               && !cancellationToken.IsCancellationRequested
               && !isShuttingDown()
               && ReferenceEquals(originalRepository, repository.Repository);
    }
}
