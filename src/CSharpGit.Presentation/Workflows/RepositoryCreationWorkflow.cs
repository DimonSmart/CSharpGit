using System.ComponentModel;
using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation;
using CSharpGit.Presentation.Dialogs;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation.Workflows;

/// <summary>Owns the repository creation UI workflow and its admission/disposal lifecycle for one window.</summary>
internal sealed class RepositoryCreationWorkflow(
    OpenRepositoryViewModel viewModel,
    CreateRepositoryViewModel workflowViewModel,
    Func<bool> otherWorkflowActive,
    Func<bool> isShuttingDown,
    Func<Task<bool>> confirmDiscardDraft,
    Func<string, bool, OpenRepositoryViewModel.UserGitOperationLease?, Task<bool>> switchRepository,
    Func<XamlRoot?> xamlRoot,
    Func<ContentDialog, Task<ContentDialogResult>> showDialog,
    Action<Func<Task>, string> runUiOperation)
{
    private readonly OpenRepositoryViewModel _viewModel = viewModel;
    private readonly CreateRepositoryViewModel _createRepositoryViewModel = workflowViewModel;
    private readonly Func<bool> _otherWorkflowActive = otherWorkflowActive;
    private readonly Func<bool> _isShuttingDown = isShuttingDown;
    private readonly Func<Task<bool>> _confirmDiscardDraft = confirmDiscardDraft;
    private readonly Func<string, bool, OpenRepositoryViewModel.UserGitOperationLease?, Task<bool>> _switchRepository = switchRepository;
    private readonly Func<XamlRoot?> _xamlRoot = xamlRoot;
    private readonly Func<ContentDialog, Task<ContentDialogResult>> _showDialog = showDialog;
    private readonly Action<Func<Task>, string> _runUiOperation = runUiOperation;
    private bool _active;
    internal bool IsActive => _active;

    internal async Task ShowAsync()
    {
        if (_isShuttingDown() || _active
            || _otherWorkflowActive()
            || _viewModel.IsBusy)
            return;

        _active = true;
        try
        {
            _createRepositoryViewModel.Reset();
            if (!await ShowCreateRepositoryDialogAsync() || _isShuttingDown())
                return;

            var opensWorkspace =
                _createRepositoryViewModel.RepositoryType
                == RepositoryCreationKind.WorkingTree;
            var discardDraft = false;
            if (opensWorkspace
                && _viewModel.Repository is not null
                && _viewModel.CommitCreation.HasUnappliedCommitMessage)
            {
                discardDraft =
                    await _confirmDiscardDraft();
                if (!discardDraft || _isShuttingDown())
                    return;
            }

            if (_isShuttingDown()) return;
            using var admission = _viewModel.TryBeginRepositoryLifecycle();
            if (admission is null) return;

            bool created;
            try
            {
                created = await _createRepositoryViewModel.CreateAsync();
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (_isShuttingDown()) return;
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

            var opened = await _switchRepository(path, discardDraft, admission);
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
        }
        finally
        {
            _active = false;
        }
    }

    private async Task<bool> ShowCreateRepositoryDialogAsync()
    {
        var directoryBox = new TextBox
        { Style = UiStyles.Resolve<Style>("CompactTextBoxStyle"),
            Header = "Directory",
            PlaceholderText = @"C:\work\MyProject",
            Text = _createRepositoryViewModel.Directory,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var browseButton = new Button
        { Style = UiStyles.Resolve<Style>("CompactButtonStyle"),
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
        { Style = UiStyles.Resolve<Style>("CompactRadioButtonStyle"),
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
        { Style = UiStyles.Resolve<Style>("CompactRadioButtonStyle"),
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
            XamlRoot = _xamlRoot(),
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
        browseButton.Click += (_, _) =>
            _runUiOperation(BrowseForCreationAsync, "Create repository folder picker");

        async Task BrowseForCreationAsync()
        {
            try { await _createRepositoryViewModel.BrowseAsync(); }
            catch (OperationCanceledException)
            {
                // Native folder selection was canceled by the user.
            }
        }
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
            return await _showDialog(dialog) == ContentDialogResult.Primary
                && !_isShuttingDown();
        }
        finally
        {
            _createRepositoryViewModel.PropertyChanged -= creationChanged;
            _viewModel.PropertyChanged -= repositoryChanged;
        }
    }

    private async Task ShowRepositoryCreationMessageAsync(
        string title,
        string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = _xamlRoot(),
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap
            },
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };
        await _showDialog(dialog);
    }
}
