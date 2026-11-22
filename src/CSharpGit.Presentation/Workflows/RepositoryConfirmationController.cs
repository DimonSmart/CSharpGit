using System.ComponentModel;
using System.Windows.Input;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation.Workflows;

// Per-window owner of discard and empty-index confirmation dialogs and subscriptions.
internal sealed class RepositoryConfirmationController(
    OpenRepositoryViewModel viewModel,
    Func<XamlRoot?> xamlRoot,
    Func<ContentDialog, Task<ContentDialogResult>> showDialog,
    Action<Func<Task>, string> runUiOperation,
    Func<ICommand, Task> executeCommand,
    Func<bool> isShuttingDown)
{
    private readonly OpenRepositoryViewModel _viewModel = viewModel;
    private readonly Func<XamlRoot?> _xamlRoot = xamlRoot;
    private readonly Func<ContentDialog, Task<ContentDialogResult>> _showDialog = showDialog;
    private readonly Action<Func<Task>, string> _runUiOperation = runUiOperation;
    private readonly Func<ICommand, Task> _executeCommand = executeCommand;
    private readonly Func<bool> _isShuttingDown = isShuttingDown;

    private bool _confirmationDialogsInitialized;
    private bool _discardConfirmationOpen;
    private bool _emptyIndexConfirmationOpen;

    internal void Initialize()
    {
        if (_confirmationDialogsInitialized) return;
        _confirmationDialogsInitialized = true;
        _viewModel.CommitCreation.PropertyChanged += CommitCreationViewModel_PropertyChanged;
        _viewModel.WorkingTree.PropertyChanged += WorkingTreeConfirmation_PropertyChanged;
    }

    internal void Shutdown()
    {
        if (!_confirmationDialogsInitialized) return;
        _confirmationDialogsInitialized = false;
        _viewModel.CommitCreation.PropertyChanged -= CommitCreationViewModel_PropertyChanged;
        _viewModel.WorkingTree.PropertyChanged -= WorkingTreeConfirmation_PropertyChanged;
    }

    private void CommitCreationViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(CommitCreationViewModel.IsEmptyIndexChoiceOpen) &&
            _viewModel.CommitCreation.IsEmptyIndexChoiceOpen)
        {
            _runUiOperation(ShowEmptyIndexChoiceAsync, "Empty index confirmation");
        }
    }

    private void WorkingTreeConfirmation_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(WorkingTreeViewModel.BatchDiscardConfirmationMessage) &&
            !string.IsNullOrWhiteSpace(_viewModel.WorkingTree.BatchDiscardConfirmationMessage))
        {
            _runUiOperation(ShowDiscardConfirmationAsync, "Discard confirmation");
        }
    }

    private async Task ShowDiscardConfirmationAsync()
    {
        if (_isShuttingDown() || _discardConfirmationOpen) return;
        var message = _viewModel.WorkingTree.BatchDiscardConfirmationMessage;
        if (string.IsNullOrWhiteSpace(message)) return;

        var repository = _viewModel.Repository;
        _discardConfirmationOpen = true;
        try
        {
            var content = new StackPanel { Spacing = 8 };
            var lines = message.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            content.Children.Add(new TextBlock
            {
                Text = lines[0],
                TextWrapping = TextWrapping.Wrap
            });
            foreach (var warning in lines.Skip(1).Where(line => !string.IsNullOrWhiteSpace(line)))
            {
                content.Children.Add(new TextBlock
                {
                    Text = warning,
                    TextWrapping = TextWrapping.Wrap,
                    FontWeight = FontWeights.SemiBold
                });
            }

            var dialog = new ContentDialog
            {
                XamlRoot = _xamlRoot(),
                Title = "Discard changes?",
                Content = content,
                PrimaryButtonText = "Discard",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };

            var result = await _showDialog(dialog);
            if (!_isShuttingDown() && ReferenceEquals(repository, _viewModel.Repository))
                await _executeCommand(result == ContentDialogResult.Primary
                    ? _viewModel.WorkingTree.ConfirmBatchDiscardCommand
                    : _viewModel.WorkingTree.CancelBatchDiscardCommand);
        }
        finally
        {
            try
            {
                if (!_isShuttingDown() && ReferenceEquals(repository, _viewModel.Repository)
                    && !string.IsNullOrWhiteSpace(_viewModel.WorkingTree.BatchDiscardConfirmationMessage))
                    await _executeCommand(_viewModel.WorkingTree.CancelBatchDiscardCommand);
            }
            finally { _discardConfirmationOpen = false; }
        }
    }

    private async Task ShowEmptyIndexChoiceAsync()
    {
        if (_isShuttingDown() || _emptyIndexConfirmationOpen || !_viewModel.CommitCreation.IsEmptyIndexChoiceOpen) return;

        var repository = _viewModel.Repository;
        _emptyIndexConfirmationOpen = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = _xamlRoot(),
                Title = "Nothing is staged",
                Content = "There are working-tree changes, but nothing is staged for commit.",
                PrimaryButtonText = "Stage all and commit",
                SecondaryButtonText = "Create empty commit",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };

            var result = await _showDialog(dialog);
            if (!_isShuttingDown() && ReferenceEquals(repository, _viewModel.Repository))
            {
                var command = result switch
                {
                    ContentDialogResult.Primary => _viewModel.CommitCreation.StageAllAndCommitCommand,
                    ContentDialogResult.Secondary => _viewModel.CommitCreation.ConfirmEmptyCommitCommand,
                    _ => _viewModel.CommitCreation.CancelCommitCommand
                };
                await _executeCommand(command);
            }
        }
        finally
        {
            try
            {
                if (!_isShuttingDown() && ReferenceEquals(repository, _viewModel.Repository)
                    && _viewModel.CommitCreation.IsEmptyIndexChoiceOpen)
                    await _executeCommand(_viewModel.CommitCreation.CancelCommitCommand);
            }
            finally { _emptyIndexConfirmationOpen = false; }
        }
    }
}
