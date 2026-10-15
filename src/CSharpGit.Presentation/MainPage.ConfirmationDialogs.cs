using System.ComponentModel;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private bool _confirmationDialogsInitialized;
    private bool _discardConfirmationOpen;
    private bool _emptyIndexConfirmationOpen;

    private void InitializeConfirmationDialogs()
    {
        if (_confirmationDialogsInitialized) return;
        _confirmationDialogsInitialized = true;
        _viewModel.CommitCreation.PropertyChanged += CommitCreationViewModel_PropertyChanged;
        _viewModel.WorkingTree.PropertyChanged += WorkingTreeConfirmation_PropertyChanged;
    }

    private void CommitCreationViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(CommitCreationViewModel.IsEmptyIndexChoiceOpen) &&
            _viewModel.CommitCreation.IsEmptyIndexChoiceOpen)
        {
            RunPageUiOperation(ShowEmptyIndexChoiceAsync, "Empty index confirmation");
        }
    }

    private void WorkingTreeConfirmation_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(WorkingTreeViewModel.BatchDiscardConfirmationMessage) &&
            !string.IsNullOrWhiteSpace(_viewModel.WorkingTree.BatchDiscardConfirmationMessage))
        {
            RunPageUiOperation(ShowDiscardConfirmationAsync, "Discard confirmation");
        }
    }

    private async Task ShowDiscardConfirmationAsync()
    {
        if (IsShuttingDown || _discardConfirmationOpen) return;
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
                XamlRoot = XamlRoot,
                Title = "Discard changes?",
                Content = content,
                PrimaryButtonText = "Discard",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };

            var result = await ShowPageDialogAsync(dialog);
            if (!IsShuttingDown && ReferenceEquals(repository, _viewModel.Repository))
                await ExecuteCommandAsync(result == ContentDialogResult.Primary
                    ? _viewModel.WorkingTree.ConfirmBatchDiscardCommand
                    : _viewModel.WorkingTree.CancelBatchDiscardCommand);
        }
        finally
        {
            try
            {
                if (!IsShuttingDown && ReferenceEquals(repository, _viewModel.Repository)
                    && !string.IsNullOrWhiteSpace(_viewModel.WorkingTree.BatchDiscardConfirmationMessage))
                    await ExecuteCommandAsync(_viewModel.WorkingTree.CancelBatchDiscardCommand);
            }
            finally { _discardConfirmationOpen = false; }
        }
    }

    private async Task ShowEmptyIndexChoiceAsync()
    {
        if (IsShuttingDown || _emptyIndexConfirmationOpen || !_viewModel.CommitCreation.IsEmptyIndexChoiceOpen) return;

        var repository = _viewModel.Repository;
        _emptyIndexConfirmationOpen = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Nothing is staged",
                Content = "There are working-tree changes, but nothing is staged for commit.",
                PrimaryButtonText = "Stage all and commit",
                SecondaryButtonText = "Create empty commit",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };

            var result = await ShowPageDialogAsync(dialog);
            if (!IsShuttingDown && ReferenceEquals(repository, _viewModel.Repository))
            {
                var command = result switch
                {
                    ContentDialogResult.Primary => _viewModel.CommitCreation.StageAllAndCommitCommand,
                    ContentDialogResult.Secondary => _viewModel.CommitCreation.ConfirmEmptyCommitCommand,
                    _ => _viewModel.CommitCreation.CancelCommitCommand
                };
                await ExecuteCommandAsync(command);
            }
        }
        finally
        {
            try
            {
                if (!IsShuttingDown && ReferenceEquals(repository, _viewModel.Repository)
                    && _viewModel.CommitCreation.IsEmptyIndexChoiceOpen)
                    await ExecuteCommandAsync(_viewModel.CommitCreation.CancelCommitCommand);
            }
            finally { _emptyIndexConfirmationOpen = false; }
        }
    }
}
