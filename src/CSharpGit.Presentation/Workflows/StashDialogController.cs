using System.Windows.Input;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation.Workflows;

// Window-scoped owner of stash creation/selection and safe destructive confirmation.
internal sealed class StashDialogController(
    OpenRepositoryViewModel viewModel,
    Func<XamlRoot?> xamlRoot,
    Func<ContentDialog, Task<ContentDialogResult>> showDialog,
    Func<bool> isShuttingDown,
    Func<ICommand, Task> executeCommand)
{
    private readonly OpenRepositoryViewModel _viewModel = viewModel;
    private readonly Func<XamlRoot?> _xamlRoot = xamlRoot;
    private readonly Func<ContentDialog, Task<ContentDialogResult>> _showDialog = showDialog;
    private readonly Func<bool> _isShuttingDown = isShuttingDown;
    private readonly Func<ICommand, Task> _executeCommand = executeCommand;

    internal async Task ShowCreateAsync()
    {
        var repository = _viewModel.Repository;
        if (repository is null || !_viewModel.Stashes.CanCreateStash) return;

        var message = new TextBox
        { Style = UiStyles.Resolve<Style>("CompactTextBoxStyle"),
            Header = "Message",
            PlaceholderText = "Optional stash message"
        };
        var scopeLabel = new TextBlock
        {
            Text = "Scope",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        var allTracked = new RadioButton
        { Style = UiStyles.Resolve<Style>("CompactRadioButtonStyle"),
            Content = "All tracked changes",
            GroupName = "RepositoryStashScope",
            IsChecked = true
        };
        var stagedOnly = new RadioButton
        { Style = UiStyles.Resolve<Style>("CompactRadioButtonStyle"),
            Content = "Staged changes only",
            GroupName = "RepositoryStashScope"
        };
        var includeUntracked = new CheckBox
        { Style = UiStyles.Resolve<Style>("CompactCheckBoxStyle"),
            Content = "Include untracked files",
            IsChecked = false
        };
        var content = new StackPanel { Spacing = UiStyles.Resolve<double>("Spacing.L") };
        content.Children.Add(message);
        content.Children.Add(scopeLabel);
        content.Children.Add(allTracked);
        content.Children.Add(stagedOnly);
        content.Children.Add(includeUntracked);

        var dialog = new ContentDialog
        {
            XamlRoot = _xamlRoot(),
            Title = "Stash changes",
            Content = content,
            PrimaryButtonText = "Stash",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        void UpdateState()
        {
            var staged = stagedOnly.IsChecked == true;
            if (staged)
                includeUntracked.IsChecked = false;
            includeUntracked.IsEnabled = !staged;

            var scope = staged
                ? StashScope.StagedChangesOnly
                : StashScope.AllTrackedChanges;
            dialog.IsPrimaryButtonEnabled = _viewModel.Stashes.CanCreateStashRequest(
                scope,
                includeUntracked.IsChecked == true);
        }

        allTracked.Checked += (_, _) => UpdateState();
        stagedOnly.Checked += (_, _) => UpdateState();
        includeUntracked.Checked += (_, _) => UpdateState();
        includeUntracked.Unchecked += (_, _) => UpdateState();
        UpdateState();

        if (await _showDialog(dialog) != ContentDialogResult.Primary
            || _isShuttingDown() || !ReferenceEquals(repository, _viewModel.Repository)
            || !_viewModel.Stashes.CanCreateStash) return;

        var selectedScope = stagedOnly.IsChecked == true
            ? StashScope.StagedChangesOnly
            : StashScope.AllTrackedChanges;
        await _viewModel.Stashes.CreateStashAsync(
            new CreateStashRequest(
                message.Text,
                selectedScope,
                IncludeUntracked: includeUntracked.IsChecked == true));
    }

    internal async Task ShowCreateSelectedAsync(
        IReadOnlyCollection<WorkingTreeChange> changes)
    {
        var repository = _viewModel.Repository;
        var snapshot = changes.ToArray();
        if (!_viewModel.Stashes.CanCreateSelectedStash(snapshot)) return;

        var message = new TextBox
        { Style = UiStyles.Resolve<Style>("CompactTextBoxStyle"),
            Header = "Message",
            PlaceholderText = "Optional stash message"
        };
        var untrackedCount = snapshot.Count(change =>
            change.Kind == FileChangeKind.Untracked);

        var content = new StackPanel { Spacing = UiStyles.Resolve<double>("Spacing.L") };
        content.Children.Add(message);
        content.Children.Add(new TextBlock
        {
            Text = snapshot.Length == 1
                ? "1 file will be stashed."
                : $"{snapshot.Length} files will be stashed."
        });
        if (untrackedCount > 0)
        {
            content.Children.Add(new TextBlock
            {
                Text = untrackedCount == 1
                    ? "Includes 1 untracked file."
                    : $"Includes {untrackedCount} untracked files.",
                TextWrapping = TextWrapping.Wrap
            });
        }

        var dialog = new ContentDialog
        {
            XamlRoot = _xamlRoot(),
            Title = "Stash selected changes",
            Content = content,
            PrimaryButtonText = "Stash",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = snapshot.Length > 0
        };

        if (await _showDialog(dialog) != ContentDialogResult.Primary
            || _isShuttingDown() || !ReferenceEquals(repository, _viewModel.Repository)
            || !_viewModel.Stashes.CanCreateSelectedStash(snapshot)) return;
        await _viewModel.Stashes.CreateSelectedStashAsync(snapshot, message.Text);
    }

    internal async Task ConfirmDropAsync(GitStash stash)
    {
        var repository = _viewModel.Repository;
        if (repository is null || !_viewModel.Stashes.DropCommand.CanExecute(null)) return;

        var content = new StackPanel { Spacing = UiStyles.Resolve<double>("Spacing.L") };
        content.Children.Add(new TextBlock
        {
            Text = stash.Display,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        content.Children.Add(new TextBlock
        {
            Text = stash.Commit[..Math.Min(8, stash.Commit.Length)]
        });
        content.Children.Add(new TextBlock
        {
            Text = "This permanently removes the selected stash.",
            TextWrapping = TextWrapping.Wrap
        });

        var dialog = new ContentDialog
        {
            XamlRoot = _xamlRoot(),
            Title = "Drop stash?",
            Content = content,
            PrimaryButtonText = "Drop stash",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        if (await _showDialog(dialog) != ContentDialogResult.Primary
            || _isShuttingDown()
            || !ReferenceEquals(repository, _viewModel.Repository)
            || !string.Equals(stash.Commit, _viewModel.Stashes.SelectedStash?.Commit, StringComparison.Ordinal)
            || !_viewModel.Stashes.DropCommand.CanExecute(null))
            return;
        await _executeCommand(_viewModel.Stashes.DropCommand);
    }
}
