using System.ComponentModel;
using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private readonly CloneRepositoryViewModel _cloneRepositoryViewModel = null!;
    private bool _repositoryCloningWorkflowActive;

    private async void CloneRepository_Click(object sender, RoutedEventArgs e) =>
        await ShowCloneRepositoryAsync();

    private async Task ShowCloneRepositoryAsync()
    {
        if (_repositoryCloningWorkflowActive
            || _repositoryCreationWorkflowActive
            || _viewModel.IsBusy
            || !_viewModel.CanChangeRepository)
            return;

        _repositoryCloningWorkflowActive = true;
        try
        {
            var discardDraft = false;
            if (_viewModel.Repository is not null
                && _viewModel.CommitCreation.HasUnappliedCommitMessage)
            {
                discardDraft = await ConfirmDiscardCommitMessageAsync(closing: false);
                if (!discardDraft)
                    return;
            }

            _cloneRepositoryViewModel.Reset();
            var cloned = await ShowCloneRepositoryDialogAsync();
            if (!cloned)
                return;

            var path = _cloneRepositoryViewModel.ClonedPath!;
            var opened = await SwitchRepositoryCoreAsync(path, discardDraft);
            if (opened)
                return;

            await ShowRepositoryCloneMessageAsync(
                "Repository cloned, but could not be opened",
                "Repository cloned, but could not be opened."
                + $"\n\n{path}"
                + (string.IsNullOrWhiteSpace(_viewModel.ErrorMessage)
                    ? string.Empty
                    : $"\n\n{_viewModel.ErrorMessage}"));
        }
        finally
        {
            _repositoryCloningWorkflowActive = false;
        }
    }

    private async Task<bool> ShowCloneRepositoryDialogAsync()
    {
        var repositoryUrlBox = new TextBox
        {
            Header = "Repository URL",
            PlaceholderText = "https://github.com/owner/repository.git",
            Text = _cloneRepositoryViewModel.RepositoryUrl,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var localDirectoryBox = new TextBox
        {
            Header = "Local directory",
            Text = _cloneRepositoryViewModel.LocalDirectory,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var browseButton = new Button
        {
            Content = "Browse…",
            VerticalAlignment = VerticalAlignment.Bottom
        };
        var localDirectoryRow = new Grid { ColumnSpacing = 8 };
        localDirectoryRow.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        localDirectoryRow.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto
        });
        localDirectoryRow.Children.Add(localDirectoryBox);
        Grid.SetColumn(browseButton, 1);
        localDirectoryRow.Children.Add(browseButton);

        var progress = new ProgressRing
        {
            Width = 20,
            Height = 20,
            IsActive = false
        };
        var progressTitle = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center
        };
        var progressSource = new TextBlock
        {
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap
        };
        var progressText = new StackPanel { Spacing = 2 };
        progressText.Children.Add(progressTitle);
        progressText.Children.Add(progressSource);
        var progressRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Visibility = Visibility.Collapsed
        };
        progressRow.Children.Add(progress);
        progressRow.Children.Add(progressText);

        var error = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };

        var cancelButton = new Button { Content = "Cancel" };
        var cloneButton = new Button { Content = "Clone" };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        actions.Children.Add(cancelButton);
        actions.Children.Add(cloneButton);

        var content = new StackPanel
        {
            Width = 560,
            Spacing = 12
        };
        content.Children.Add(repositoryUrlBox);
        content.Children.Add(localDirectoryRow);
        content.Children.Add(progressRow);
        content.Children.Add(error);
        content.Children.Add(actions);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Clone repository",
            Content = content
        };

        var succeeded = false;
        var synchronizing = false;

        void Synchronize()
        {
            synchronizing = true;
            try
            {
                if (!string.Equals(
                        repositoryUrlBox.Text,
                        _cloneRepositoryViewModel.RepositoryUrl,
                        StringComparison.Ordinal))
                    repositoryUrlBox.Text = _cloneRepositoryViewModel.RepositoryUrl;
                if (!string.Equals(
                        localDirectoryBox.Text,
                        _cloneRepositoryViewModel.LocalDirectory,
                        StringComparison.Ordinal))
                    localDirectoryBox.Text = _cloneRepositoryViewModel.LocalDirectory;
            }
            finally
            {
                synchronizing = false;
            }

            var busy = _cloneRepositoryViewModel.IsBusy;
            repositoryUrlBox.IsEnabled = !busy;
            localDirectoryBox.IsEnabled = !busy;
            browseButton.IsEnabled = !busy;
            cloneButton.IsEnabled = _cloneRepositoryViewModel.CanClone;
            progress.IsActive = busy;
            progressRow.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            progressTitle.Text = $"Cloning {_cloneRepositoryViewModel.RepositoryDisplayName}…";
            progressSource.Text = _cloneRepositoryViewModel.RepositoryUrl;

            error.Text = _cloneRepositoryViewModel.ErrorMessage ?? string.Empty;
            error.Visibility =
                string.IsNullOrWhiteSpace(_cloneRepositoryViewModel.ErrorMessage)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
        }

        PropertyChangedEventHandler changed = (_, _) => Synchronize();
        repositoryUrlBox.TextChanged += (_, _) =>
        {
            if (!synchronizing)
                _cloneRepositoryViewModel.RepositoryUrl = repositoryUrlBox.Text;
        };
        localDirectoryBox.TextChanged += (_, _) =>
        {
            if (!synchronizing)
                _cloneRepositoryViewModel.LocalDirectory = localDirectoryBox.Text;
        };
        browseButton.Click += async (_, _) =>
        {
            try
            {
                await _cloneRepositoryViewModel.BrowseAsync();
            }
            catch (OperationCanceledException)
            {
            }
        };
        cancelButton.Click += (_, _) =>
        {
            if (_cloneRepositoryViewModel.IsBusy)
                _cloneRepositoryViewModel.Cancel();
            else
                dialog.Hide();
        };
        cloneButton.Click += async (_, _) =>
        {
            if (!_cloneRepositoryViewModel.CanClone)
                return;

            try
            {
                if (await _cloneRepositoryViewModel.CloneAsync())
                {
                    succeeded = true;
                    dialog.Hide();
                }
            }
            catch (OperationCanceledException)
            {
                dialog.Hide();
            }
        };
        dialog.Closing += (_, args) =>
        {
            if (!_cloneRepositoryViewModel.IsBusy)
                return;
            args.Cancel = true;
            _cloneRepositoryViewModel.Cancel();
        };

        _cloneRepositoryViewModel.PropertyChanged += changed;
        try
        {
            Synchronize();
            await dialog.ShowAsync();
            return succeeded;
        }
        finally
        {
            _cloneRepositoryViewModel.PropertyChanged -= changed;
        }
    }

    private async Task ShowRepositoryCloneMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap
            },
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };
        await dialog.ShowAsync();
    }
}
