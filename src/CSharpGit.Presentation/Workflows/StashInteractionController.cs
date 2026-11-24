using CSharpGit.Presentation.ViewModels;
using CSharpGit.Presentation.Views;
using Microsoft.UI.Xaml;

namespace CSharpGit.Presentation.Workflows;

// Owns the two stash UI entry points that are not command-bound.
internal sealed class StashInteractionController(
    OpenRepositoryViewModel viewModel,
    StashDialogController dialogs,
    HistoryWorkspaceView historyWorkspace,
    WorkingTreeCommitEditorView commitEditor,
    Action<Func<Task>, string> runUiOperation)
{
    private readonly OpenRepositoryViewModel _viewModel = viewModel;
    private readonly StashDialogController _dialogs = dialogs;
    private readonly HistoryWorkspaceView _historyWorkspace = historyWorkspace;
    private readonly WorkingTreeCommitEditorView _commitEditor = commitEditor;
    private readonly Action<Func<Task>, string> _runUiOperation = runUiOperation;
    private bool _initialized;

    internal void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        _historyWorkspace.DropSelectedStash_ClickCallback = DropSelectedStash_Click;
        _commitEditor.StashRequested += CreateStash_Click;
    }

    private void CreateStash_Click(object sender, RoutedEventArgs args) =>
        _runUiOperation(_dialogs.ShowCreateAsync, "Create stash");

    private void DropSelectedStash_Click(object sender, RoutedEventArgs args)
    {
        if (_viewModel.Stashes.SelectedStash is { } stash)
            _runUiOperation(() => _dialogs.ConfirmDropAsync(stash), "Drop stash");
    }

    internal void Shutdown()
    {
        if (!_initialized) return;
        _initialized = false;
        _historyWorkspace.DropSelectedStash_ClickCallback = null;
        _commitEditor.StashRequested -= CreateStash_Click;
    }
}
