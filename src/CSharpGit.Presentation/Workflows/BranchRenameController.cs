using Microsoft.UI.Xaml;
using CSharpGit.Domain;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

using CSharpGit.Presentation.ViewModels;
using CSharpGit.Presentation.Views;

namespace CSharpGit.Presentation.Workflows;

// One window-owned workflow for F2, context-menu and drag-drop branch renaming.
internal sealed class BranchRenameController(
    OpenRepositoryViewModel viewModel,
    WorktreesViewModel worktreesViewModel,
    TreeView tree,
    Func<XamlRoot?> xamlRoot,
    Func<ContentDialog, Task<ContentDialogResult>> showDialog,
    Func<bool> isShuttingDown,
    Action<Func<Task>, string> runUiOperation)
{
    private readonly OpenRepositoryViewModel _viewModel = viewModel;
    private readonly WorktreesViewModel _worktreesViewModel = worktreesViewModel;
    private readonly TreeView _tree = tree;
    private readonly Func<XamlRoot?> _xamlRoot = xamlRoot;
    private readonly Func<ContentDialog, Task<ContentDialogResult>> _showDialog = showDialog;
    private readonly Func<bool> _isShuttingDown = isShuttingDown;
    private readonly Action<Func<Task>, string> _runUiOperation = runUiOperation;
    private TreeView RepositoryTree => _tree;
    private static RepositoryTreeNode? ResolveNode(object? value) => RepositoryExplorerView.ResolveNode(value);

    internal Task RenameAsync(GitBranch branch) => RenameBranchAsync(branch);
    internal Task ExecuteAsync(GitBranch branch, string newName) => ExecuteBranchRenameAsync(branch, newName);

    internal void Shutdown() => RepositoryTree.KeyDown -= RepositoryTree_KeyDown;

    internal void Initialize()
    {
        RepositoryTree.KeyDown -= RepositoryTree_KeyDown;
        RepositoryTree.KeyDown += RepositoryTree_KeyDown;
    }

    internal bool CanRenameBranch() => _viewModel.Branches.CanRename;

    private void RepositoryTree_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.F2) return;

        var node = ResolveNode(RepositoryTree.SelectedItem);
        if (node?.Kind != RepositoryTreeNodeKind.LocalBranch || node.Value is not GitBranch branch) return;

        e.Handled = true;
        if (!CanRenameBranch()) return;

        _runUiOperation(() => RenameBranchAsync(branch), "Rename branch");
    }

    private async Task RenameBranchAsync(GitBranch branch)
    {
        ArgumentNullException.ThrowIfNull(branch);
        if (!CanRenameBranch()) return;
        var repository = _viewModel.Repository;

        var oldName = branch.Name;
        var nameBox = new TextBox
        { Style = UiStyles.Resolve<Style>("CompactTextBoxStyle"),
            Header = "Name",
            Text = oldName,
            MinWidth = 420
        };
        var dialog = new ContentDialog
        {
            XamlRoot = _xamlRoot(),
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

        if (await _showDialog(dialog) != ContentDialogResult.Primary
            || _isShuttingDown() || !ReferenceEquals(repository, _viewModel.Repository)
            || !CanRenameBranch()) return;

        var newName = nameBox.Text.Trim();
        if (newName.Length == 0 || string.Equals(oldName, newName, StringComparison.Ordinal)) return;

        await ExecuteBranchRenameAsync(branch, newName);
    }

    private async Task ExecuteBranchRenameAsync(GitBranch branch, string newName)
    {
        ArgumentNullException.ThrowIfNull(branch);
        ArgumentException.ThrowIfNullOrEmpty(newName);
        if (!CanRenameBranch()) return;

        var oldName = branch.Name;
        if (string.Equals(oldName, newName, StringComparison.Ordinal)) return;

        var repository = _viewModel.Repository;
        if (repository is null) return;

        var succeeded = await _viewModel.Branches.RenameBranchAsync(
            repository,
            branch,
            newName,
            () =>
            {
                _viewModel.History.HandleReferenceRenamed(
                    oldName,
                    newName,
                    $"Branch: {newName}");
            });

        if (succeeded)
            await _worktreesViewModel.RefreshAsync();
    }
}
