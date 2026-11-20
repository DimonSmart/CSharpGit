using System.ComponentModel;
using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation;
using CSharpGit.Presentation.Dialogs;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation.Workflows;

/// <summary>Owns the repository cloning UI workflow and its admission/disposal lifecycle for one window.</summary>
internal sealed class RepositoryCloneWorkflow(
    OpenRepositoryViewModel viewModel,
    CloneRepositoryViewModel workflowViewModel,
    Func<bool> otherWorkflowActive,
    Func<bool> isShuttingDown,
    Func<Task<bool>> confirmDiscardDraft,
    Func<string, bool, OpenRepositoryViewModel.UserGitOperationLease?, Task<bool>> switchRepository,
    Func<XamlRoot?> xamlRoot,
    Func<ContentDialog, Task<ContentDialogResult>> showDialog,
    Action<Func<Task>, string> runUiOperation,
    GitCommandFeedbackController feedback)
{
    private readonly OpenRepositoryViewModel _viewModel = viewModel;
    private readonly CloneRepositoryViewModel _cloneRepositoryViewModel = workflowViewModel;
    private readonly Func<bool> _otherWorkflowActive = otherWorkflowActive;
    private readonly Func<bool> _isShuttingDown = isShuttingDown;
    private readonly Func<Task<bool>> _confirmDiscardDraft = confirmDiscardDraft;
    private readonly Func<string, bool, OpenRepositoryViewModel.UserGitOperationLease?, Task<bool>> _switchRepository = switchRepository;
    private readonly Func<XamlRoot?> _xamlRoot = xamlRoot;
    private readonly Func<ContentDialog, Task<ContentDialogResult>> _showDialog = showDialog;
    private readonly Action<Func<Task>, string> _runUiOperation = runUiOperation;
    private readonly GitCommandFeedbackController _feedback = feedback;
    private bool _active;
    internal bool IsActive => _active;
    private OpenRepositoryViewModel.UserGitOperationLease? _cloneWorkflowAdmission;

    internal async Task ShowAsync()
    {
        if (_isShuttingDown() || _active
            || _otherWorkflowActive()
            || _viewModel.IsBusy
            || !_viewModel.CanChangeRepository)
            return;

        _active = true;
        try
        {
            var discardDraft = false;
            if (_viewModel.Repository is not null
                && _viewModel.CommitCreation.HasUnappliedCommitMessage)
            {
                discardDraft = await _confirmDiscardDraft();
                if (!discardDraft)
                    return;
            }

            if (_isShuttingDown()) return;
            _cloneRepositoryViewModel.Reset();
            var cloned = await ShowCloneRepositoryDialogAsync();
            if (!cloned || _isShuttingDown())
                return;

            var path = _cloneRepositoryViewModel.ClonedPath!;
            var opened = await _switchRepository(path, discardDraft, _cloneWorkflowAdmission);
            if (opened || _isShuttingDown())
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
            _cloneWorkflowAdmission?.Dispose();
            _cloneWorkflowAdmission = null;
            _active = false;
        }
    }

    private async Task<bool> ShowCloneRepositoryDialogAsync()
    {
        var repositoryUrlBox = new TextBox
        { Style = UiStyles.Resolve<Style>("CompactTextBoxStyle"),
            Header = "Repository URL",
            PlaceholderText = "https://github.com/owner/repository.git",
            Text = _cloneRepositoryViewModel.RepositoryUrl,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var localDirectoryBox = new TextBox
        { Style = UiStyles.Resolve<Style>("CompactTextBoxStyle"),
            Header = "Local directory",
            Text = _cloneRepositoryViewModel.LocalDirectory,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var browseButton = new Button
        { Style = UiStyles.Resolve<Style>("CompactButtonStyle"),
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

        var cancelButton = new Button { Style = UiStyles.Resolve<Style>("CompactButtonStyle"), Content = "Cancel" };
        var cloneButton = new Button { Style = UiStyles.Resolve<Style>("CompactButtonStyle"), Content = "Clone" };
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
        var cloneFeedback = _feedback.CreateCloneGitCommandFeedback();
        if (cloneFeedback is not null)
            content.Children.Add(cloneFeedback);
        content.Children.Add(error);
        content.Children.Add(actions);

        var dialog = new ContentDialog
        {
            XamlRoot = _xamlRoot(),
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
        browseButton.Click += (_, _) =>
            _runUiOperation(BrowseForCloneAsync, "Clone repository folder picker");

        async Task BrowseForCloneAsync()
        {
            try { await _cloneRepositoryViewModel.BrowseAsync(); }
            catch (OperationCanceledException)
            {
                // Closing the native folder picker is expected user cancellation.
            }
        }
        cancelButton.Click += (_, _) =>
        {
            if (_cloneRepositoryViewModel.IsBusy)
                _cloneRepositoryViewModel.Cancel();
            else
                dialog.Hide();
        };
        cloneButton.Click += (_, _) =>
            _runUiOperation(CloneFromDialogAsync, "Clone repository");

        async Task CloneFromDialogAsync()
        {
            if (!_cloneRepositoryViewModel.CanClone || _isShuttingDown())
                return;

            OpenRepositoryViewModel.UserGitOperationLease? admission =
                _viewModel.TryBeginRepositoryLifecycle();
            if (admission is null) return;

            try
            {
                if (await _cloneRepositoryViewModel.CloneAsync() && !_isShuttingDown())
                {
                    _cloneWorkflowAdmission = admission;
                    admission = null;
                    succeeded = true;
                    dialog.Hide();
                }
            }
            catch (OperationCanceledException)
            {
                if (!_isShuttingDown())
                    dialog.Hide();
            }
            finally
            {
                admission?.Dispose();
            }
        }
        dialog.Closing += (_, args) =>
        {
            if (!_cloneRepositoryViewModel.IsBusy)
                return;
            args.Cancel = true;
            _cloneRepositoryViewModel.Cancel();
        };

        _cloneRepositoryViewModel.PropertyChanged += changed;
        _feedback.CloneDialogFeedbackActive = cloneFeedback is not null;
        try
        {
            Synchronize();
            await _showDialog(dialog);
            return succeeded && !_isShuttingDown();
        }
        finally
        {
            _feedback.CloneDialogFeedbackActive = false;
            _cloneRepositoryViewModel.PropertyChanged -= changed;
            if (cloneFeedback is not null)
            {
                _feedback.ReleaseCloneGitCommandFeedback(cloneFeedback);
            }
        }
    }

    private async Task ShowRepositoryCloneMessageAsync(string title, string message)
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
