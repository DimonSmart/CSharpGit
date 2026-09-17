using CSharpGit.Domain;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private void InitializeBranchRename()
    {
        RepositoryTree.KeyDown -= RepositoryTree_KeyDown;
        RepositoryTree.KeyDown += RepositoryTree_KeyDown;
    }

    private bool CanRenameBranch() =>
        !_viewModel.IsBusy
        && _viewModel.Repository is not null
        && _viewModel.CurrentOperation == RepositoryOperation.None;

    private async void RepositoryTree_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.F2) return;

        var node = ResolveNode(RepositoryTree.SelectedItem);
        if (node?.Kind != RepositoryTreeNodeKind.LocalBranch || node.Value is not GitBranch branch) return;

        e.Handled = true;
        if (!CanRenameBranch()) return;

        await RenameBranchAsync(branch);
    }

    private async Task RenameBranchAsync(GitBranch branch)
    {
        ArgumentNullException.ThrowIfNull(branch);
        if (!CanRenameBranch()) return;

        var oldName = branch.Name;
        var nameBox = new TextBox
        {
            Header = "Name",
            Text = oldName,
            MinWidth = 420
        };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Rename branch",
            Content = nameBox,
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        void UpdatePrimaryButton()
        {
            var candidate = nameBox.Text.Trim();
            dialog.IsPrimaryButtonEnabled = candidate.Length > 0
                && !string.Equals(candidate, oldName, StringComparison.Ordinal);
        }

        nameBox.TextChanged += (_, _) => UpdatePrimaryButton();
        UpdatePrimaryButton();

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var newName = nameBox.Text.Trim();
        if (newName.Length == 0 || string.Equals(oldName, newName, StringComparison.Ordinal)) return;
        if (!CanRenameBranch()) return;

        var succeeded = await _viewModel.RunMutationAsync(
            async () =>
            {
                await _referenceService.RenameBranchAsync(
                    _viewModel.Repository!,
                    oldName,
                    newName);

                if (string.Equals(_activeReference, oldName, StringComparison.Ordinal))
                {
                    _activeReference = newName;
                    ActiveReferenceText.Text = $"Branch: {newName}";
                }
            },
            "Could not rename local branch");

        if (succeeded)
            QueueWorktreeRefresh();
    }
}
