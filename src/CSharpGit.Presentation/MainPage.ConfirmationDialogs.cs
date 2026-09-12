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

    private void ConfirmationDialogs_Loaded(object sender, RoutedEventArgs args)
    {
        if (_confirmationDialogsInitialized) return;
        _confirmationDialogsInitialized = true;
        _viewModel.PropertyChanged += ConfirmationDialogs_PropertyChanged;
    }

    private void ConfirmationDialogs_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(OpenRepositoryViewModel.BatchDiscardConfirmationMessage) &&
            !string.IsNullOrWhiteSpace(_viewModel.BatchDiscardConfirmationMessage))
        {
            _ = ShowDiscardConfirmationAsync();
        }
        else if (args.PropertyName == nameof(OpenRepositoryViewModel.IsEmptyIndexChoiceOpen) &&
                 _viewModel.IsEmptyIndexChoiceOpen)
        {
            _ = ShowEmptyIndexChoiceAsync();
        }
    }

    private async Task ShowDiscardConfirmationAsync()
    {
        if (_discardConfirmationOpen) return;
        var message = _viewModel.BatchDiscardConfirmationMessage;
        if (string.IsNullOrWhiteSpace(message)) return;

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

            var result = await dialog.ShowAsync();
            await ExecuteCommandAsync(result == ContentDialogResult.Primary
                ? _viewModel.ConfirmBatchDiscardCommand
                : _viewModel.CancelBatchDiscardCommand);
        }
        finally
        {
            _discardConfirmationOpen = false;
            if (!string.IsNullOrWhiteSpace(_viewModel.BatchDiscardConfirmationMessage))
                await ExecuteCommandAsync(_viewModel.CancelBatchDiscardCommand);
        }
    }

    private async Task ShowEmptyIndexChoiceAsync()
    {
        if (_emptyIndexConfirmationOpen || !_viewModel.IsEmptyIndexChoiceOpen) return;

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

            var command = (await dialog.ShowAsync()) switch
            {
                ContentDialogResult.Primary => _viewModel.StageAllAndCommitCommand,
                ContentDialogResult.Secondary => _viewModel.ConfirmEmptyCommitCommand,
                _ => _viewModel.CancelCommitCommand
            };
            await ExecuteCommandAsync(command);
        }
        finally
        {
            _emptyIndexConfirmationOpen = false;
            if (_viewModel.IsEmptyIndexChoiceOpen)
                await ExecuteCommandAsync(_viewModel.CancelCommitCommand);
        }
    }
}
