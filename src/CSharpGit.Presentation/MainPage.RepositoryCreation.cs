using System.ComponentModel;
using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private readonly CreateRepositoryViewModel _createRepositoryViewModel = null!;
    private bool _repositoryCreationWorkflowActive;

    private async void CreateRepository_Click(object sender, RoutedEventArgs e) =>
        await ShowCreateRepositoryAsync();

    private async Task ShowCreateRepositoryAsync()
    {
        if (_repositoryCreationWorkflowActive || _viewModel.IsBusy)
            return;

        _repositoryCreationWorkflowActive = true;
        try
        {
            _createRepositoryViewModel.Reset();
            if (!await ShowCreateRepositoryDialogAsync())
                return;

            var opensWorkspace =
                _createRepositoryViewModel.RepositoryType
                == RepositoryCreationKind.WorkingTree;
            var discardDraft = false;
            if (opensWorkspace
                && _viewModel.Repository is not null
                && _viewModel.HasUnappliedCommitMessage)
            {
                discardDraft =
                    await ConfirmDiscardCommitMessageForRepositorySwitchAsync();
                if (!discardDraft)
                    return;
            }

            bool created;
            try
            {
                created = await _createRepositoryViewModel.CreateAsync();
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!created)
            {
                await ShowRepositoryCreationMessageAsync(
                    "Could not create repository",
                    _createRepositoryViewModel.ErrorMessage
                    ?? "Could not create repository.");
                return;
            }

            var path = _createRepositoryViewModel.CreatedPath!;
            if (!opensWorkspace)
            {
                await ShowRepositoryCreationMessageAsync(
                    "Repository created successfully",
                    $"Repository created successfully.\n\n{path}");
                return;
            }

            var opened = await _viewModel.OpenRepositoryPathAsync(path);
            if (!opened)
            {
                await ShowRepositoryCreationMessageAsync(
                    "Repository created, but could not be opened",
                    "Repository was created successfully, but CSharpGit could not open it."
                    + $"\n\n{path}"
                    + (string.IsNullOrWhiteSpace(_viewModel.ErrorMessage)
                        ? string.Empty
                        : $"\n\n{_viewModel.ErrorMessage}"));
                return;
            }

            if (discardDraft)
                _viewModel.CommitMessage = string.Empty;

            RefreshPresentationCollections();
        }
        finally
        {
            _repositoryCreationWorkflowActive = false;
        }
    }

    private async Task<bool> ShowCreateRepositoryDialogAsync()
    {
        var directoryBox = new TextBox
        {
            Header = "Directory",
            PlaceholderText = @"C:\work\MyProject",
            Text = _createRepositoryViewModel.Directory,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var browseButton = new Button
        {
            Content = "Browse…",
            VerticalAlignment = VerticalAlignment.Bottom
        };

        var directoryRow = new Grid
        {
            ColumnSpacing = 8
        };
        directoryRow.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        directoryRow.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto
        });
        directoryRow.Children.Add(directoryBox);
        Grid.SetColumn(browseButton, 1);
        directoryRow.Children.Add(browseButton);

        var personal = new RadioButton
        {
            Content = "Personal repository",
            GroupName = "RepositoryCreationType",
            IsChecked = true
        };
        var centralLabel = new StackPanel
        {
            Spacing = 2
        };
        centralLabel.Children.Add(new TextBlock
        {
            Text = "Central repository, no working directory"
        });
        centralLabel.Children.Add(new TextBlock
        {
            Text = "--bare --shared=all",
            Opacity = 0.7
        });
        var central = new RadioButton
        {
            Content = centralLabel,
            GroupName = "RepositoryCreationType"
        };

        var error = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };

        var content = new StackPanel
        {
            Spacing = 12,
            Width = 520
        };
        content.Children.Add(directoryRow);
        content.Children.Add(new TextBlock
        {
            Text = "Repository type"
        });
        content.Children.Add(personal);
        content.Children.Add(central);
        content.Children.Add(error);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Create new repository",
            Content = content,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = _createRepositoryViewModel.CanCreate
                                      && !_viewModel.IsBusy
        };

        void Synchronize()
        {
            if (!string.Equals(
                    directoryBox.Text,
                    _createRepositoryViewModel.Directory,
                    StringComparison.Ordinal))
            {
                directoryBox.Text = _createRepositoryViewModel.Directory;
            }

            browseButton.IsEnabled = !_createRepositoryViewModel.IsBusy;
            personal.IsEnabled = !_createRepositoryViewModel.IsBusy;
            central.IsEnabled = !_createRepositoryViewModel.IsBusy;
            dialog.IsPrimaryButtonEnabled =
                _createRepositoryViewModel.CanCreate
                && !_viewModel.IsBusy;

            error.Text = _createRepositoryViewModel.ErrorMessage ?? string.Empty;
            error.Visibility =
                string.IsNullOrWhiteSpace(_createRepositoryViewModel.ErrorMessage)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
        }

        PropertyChangedEventHandler creationChanged = (_, _) => Synchronize();
        PropertyChangedEventHandler repositoryChanged = (_, args) =>
        {
            if (args.PropertyName == nameof(OpenRepositoryViewModel.IsBusy))
                Synchronize();
        };

        directoryBox.TextChanged += (_, _) =>
            _createRepositoryViewModel.Directory = directoryBox.Text;
        browseButton.Click += async (_, _) =>
        {
            try
            {
                await _createRepositoryViewModel.BrowseAsync();
            }
            catch (OperationCanceledException)
            {
            }
        };
        personal.Checked += (_, _) =>
            _createRepositoryViewModel.RepositoryType =
                RepositoryCreationKind.WorkingTree;
        central.Checked += (_, _) =>
            _createRepositoryViewModel.RepositoryType =
                RepositoryCreationKind.BareShared;

        _createRepositoryViewModel.PropertyChanged += creationChanged;
        _viewModel.PropertyChanged += repositoryChanged;
        try
        {
            Synchronize();
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        finally
        {
            _createRepositoryViewModel.PropertyChanged -= creationChanged;
            _viewModel.PropertyChanged -= repositoryChanged;
        }
    }

    private async Task<bool> ConfirmDiscardCommitMessageForRepositorySwitchAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Discard commit message?",
            Content =
                "Creating and opening another repository will discard the current commit message.",
            PrimaryButtonText = "Discard and continue",
            CloseButtonText = "Keep editing",
            DefaultButton = ContentDialogButton.Close
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task ShowRepositoryCreationMessageAsync(
        string title,
        string message)
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
