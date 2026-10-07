using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private MenuFlyout? _commitActionsFlyout;
    private MenuFlyoutItem? _copyHashItem;
    private MenuFlyoutItem? _createBranchHereItem;
    private MenuFlyoutItem? _checkoutCommitItem;
    private MenuFlyoutItem? _cherryPickItem;
    private MenuFlyoutItem? _revertItem;
    private MenuFlyoutItem? _editCommitMessageItem;
    private MenuFlyoutItem? _fixupIntoPreviousCommitItem;
    private MenuFlyoutItem? _interactiveRebaseFromHereItem;
    private MenuFlyoutSubItem? _resetItem;

    private void InitializeCommitActions()
    {
        if (_commitActionsFlyout is not null) return;

        _copyHashItem = new MenuFlyoutItem { Text = "Copy hash" };
        _copyHashItem.Click += CopyCommitHash_Click;
        _createBranchHereItem = new MenuFlyoutItem { Text = "Create branch here…" };
        _createBranchHereItem.Click += CreateBranchHere_Click;
        _checkoutCommitItem = new MenuFlyoutItem { Text = "Checkout this commit" };
        _checkoutCommitItem.Click += CheckoutCommit_Click;
        _cherryPickItem = new MenuFlyoutItem { Text = "Cherry-pick" };
        _cherryPickItem.Click += CherryPickCommit_Click;
        _revertItem = new MenuFlyoutItem { Text = "Revert" };
        _revertItem.Click += RevertCommit_Click;
        _editCommitMessageItem = new MenuFlyoutItem { Text = "Edit commit message…" };
        _editCommitMessageItem.Click += EditCommitMessage_Click;
        _fixupIntoPreviousCommitItem = new MenuFlyoutItem { Text = "Fixup into previous commit" };
        _fixupIntoPreviousCommitItem.Click += FixupIntoPreviousCommit_Click;
        _interactiveRebaseFromHereItem = new MenuFlyoutItem { Text = "Interactive rebase from here…" };
        _interactiveRebaseFromHereItem.Click += InteractiveRebaseFromHere_Click;

        _resetItem = new MenuFlyoutSubItem { Text = "Reset current branch to here" };
        foreach (var (mode, label) in new[]
                 {
                     (ResetMode.Soft, "Soft…"),
                     (ResetMode.Mixed, "Mixed…"),
                     (ResetMode.Hard, "Hard…")
                 })
        {
            var item = new MenuFlyoutItem { Text = label, Tag = mode };
            item.Click += ResetCommit_Click;
            _resetItem.Items.Add(item);
        }

        _commitActionsFlyout = new MenuFlyout();
        _commitActionsFlyout.Items.Add(_copyHashItem);
        _commitActionsFlyout.Items.Add(new MenuFlyoutSeparator());
        _commitActionsFlyout.Items.Add(_createBranchHereItem);
        _commitActionsFlyout.Items.Add(_checkoutCommitItem);
        _commitActionsFlyout.Items.Add(new MenuFlyoutSeparator());
        _commitActionsFlyout.Items.Add(_cherryPickItem);
        _commitActionsFlyout.Items.Add(_revertItem);
        _commitActionsFlyout.Items.Add(_editCommitMessageItem);
        _commitActionsFlyout.Items.Add(_fixupIntoPreviousCommitItem);
        _commitActionsFlyout.Items.Add(_interactiveRebaseFromHereItem);
        _commitActionsFlyout.Items.Add(new MenuFlyoutSeparator());
        _commitActionsFlyout.Items.Add(_resetItem);
        _commitActionsFlyout.Opening += (_, _) => UpdateCommitActionAvailability();

        HistoryList.ContextFlyout = _commitActionsFlyout;
        HistoryList.RightTapped += HistoryList_RightTapped;
        InitializeTagSupportIfNeeded();
    }

    private void HistoryList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var current = e.OriginalSource as DependencyObject;
        while (current is not null && current is not ListViewItem)
            current = VisualTreeHelper.GetParent(current);

        if (current is ListViewItem { Content: HistoryRow row })
        {
            HistoryList.SelectedItem = row;
            _viewModel.History.SelectedRow = row;
        }

        UpdateCommitActionAvailability();
    }

    private void UpdateCommitActionAvailability()
    {
        var commit = _viewModel.History.SelectedRow?.Commit;
        var hasCommit = commit is not null;
        var canMutate = _viewModel.CommitActions.CanMutateCommit(commit);
        var hasLocalBranch = _viewModel.CurrentBranchName is not null;

        if (_copyHashItem is not null) _copyHashItem.IsEnabled = hasCommit;
        if (_createBranchHereItem is not null) _createBranchHereItem.IsEnabled = canMutate;
        if (_checkoutCommitItem is not null) _checkoutCommitItem.IsEnabled = _viewModel.CommitActions.CanCheckout(commit);
        if (_cherryPickItem is not null) _cherryPickItem.IsEnabled = _viewModel.CommitActions.CanCherryPick(commit);
        if (_revertItem is not null) _revertItem.IsEnabled = _viewModel.CommitActions.CanRevert(commit);
        if (_editCommitMessageItem is not null) _editCommitMessageItem.IsEnabled = canMutate;
        if (_fixupIntoPreviousCommitItem is not null)
            _fixupIntoPreviousCommitItem.IsEnabled = _viewModel.CommitActions.CanFixup(commit);
        if (_interactiveRebaseFromHereItem is not null) _interactiveRebaseFromHereItem.IsEnabled = canMutate && hasLocalBranch;
        if (_resetItem is not null) _resetItem.IsEnabled = _viewModel.CommitActions.CanReset(commit);
    }

    private void CopyCommitHash_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.History.SelectedRow?.Commit.Hash is not { } hash) return;
        var package = new DataPackage();
        package.SetText(hash);
        Clipboard.SetContent(package);
    }

    private async void CreateBranchHere_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetCommitActionContext(out _, out var commit)) return;
        await CreateBranchFromReferenceAsync(commit.Hash, commit.Hash);
    }

    private async Task CreateBranchFromReferenceAsync(string startPoint, string? restoreSelectionCommit = null)
    {
        if (_viewModel.Repository is null || _viewModel.IsBusy || _viewModel.CurrentOperation != RepositoryOperation.None) return;

        var branchName = new TextBox { Header = "Name", PlaceholderText = "feature/foo" };
        var switchToBranch = new CheckBox { Content = "Switch to the new branch", IsChecked = true };
        var content = new StackPanel { Width = 420, Spacing = 10 };
        content.Children.Add(branchName);
        content.Children.Add(switchToBranch);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Create branch",
            Content = content,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (string.IsNullOrWhiteSpace(branchName.Text))
        {
            await ShowErrorAsync("Branch name required", "Enter a branch name.");
            return;
        }

        var repository = _viewModel.Repository;
        if (repository is null) return;
        var switched = switchToBranch.IsChecked == true;
        if (await _viewModel.Branches.CreateBranchAsync(
                repository,
                branchName.Text.Trim(),
                startPoint,
                switched) &&
            !string.IsNullOrWhiteSpace(restoreSelectionCommit))
            await RestoreCommitActionSelectionAsync(restoreSelectionCommit);
    }

    private async void CheckoutCommit_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetCommitActionContext(out var repository, out var commit)) return;
        var hash = commit.Hash;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Checkout commit {ShortOid(hash)}?",
            Content = "This will leave the current branch and switch to a detached HEAD.",
            PrimaryButtonText = "Checkout",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var result = await _viewModel.CommitActions.CheckoutAsync(repository, commit);
        await HandleCommitActionExecutionResultAsync(result);
    }

    private async void CherryPickCommit_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetCommitActionContext(out var repository, out var commit)) return;
        var mainline = await SelectMainlineParentAsync(commit, "Cherry-pick merge commit");
        if (commit.Parents.Count > 1 && mainline is null) return;

        var result = await _viewModel.CommitActions.CherryPickAsync(
            repository,
            commit,
            mainline);
        await HandleCommitActionExecutionResultAsync(result);
    }

    private async void RevertCommit_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetCommitActionContext(out var repository, out var commit)) return;
        var mainline = await SelectMainlineParentAsync(commit, "Revert merge commit");
        if (commit.Parents.Count > 1 && mainline is null) return;

        var result = await _viewModel.CommitActions.RevertAsync(
            repository,
            commit,
            mainline);
        await HandleCommitActionExecutionResultAsync(result);
    }

    private async void FixupIntoPreviousCommit_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetCommitActionContext(out var repository, out var commit)) return;

        var result = await _viewModel.CommitActions.FixupAsync(repository, commit);
        await HandleCommitActionExecutionResultAsync(result);
    }

    private async void InteractiveRebaseFromHere_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetCommitActionContext(out _, out var commit)) return;

        var hash = commit.Hash;
        if (!await _viewModel.PrepareInteractiveRebaseFromCommitAsync(hash))
            return;

        await ShowInteractiveRebaseEditorAsync();
    }
    private async void ResetCommit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: ResetMode mode }) return;
        if (!TryGetCommitActionContext(out var repository, out var commit)) return;

        var hash = commit.Hash;
        var (title, body, action) = mode switch
        {
            ResetMode.Soft => (
                $"Soft reset to {ShortOid(hash)}?",
                "Move the current branch to the selected commit.\n\nIndex and working-tree changes will be preserved.",
                "Soft reset"),
            ResetMode.Mixed => (
                $"Mixed reset to {ShortOid(hash)}?",
                "Move the current branch to the selected commit.\n\nThe index will be reset. Working-tree files will be preserved.",
                "Mixed reset"),
            ResetMode.Hard => (
                $"Hard reset to {ShortOid(hash)}?",
                "The current branch, index and tracked working-tree files will be reset to the selected commit.\n\nUncommitted tracked changes will be lost. Untracked files will not be deleted.",
                "Hard reset"),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = body,
            PrimaryButtonText = action,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var result = await _viewModel.CommitActions.ResetAsync(
            repository,
            commit,
            mode);
        await HandleCommitActionExecutionResultAsync(result);
    }

    private bool TryGetCommitActionContext(out Repository repository, out CommitHistoryItem commit)
    {
        repository = _viewModel.Repository!;
        commit = _viewModel.History.SelectedRow?.Commit!;
        return repository is not null &&
               commit is not null &&
               _viewModel.CommitActions.CanMutateCommit(commit);
    }

    private async Task HandleCommitActionExecutionResultAsync(CommitActionExecutionResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            await ShowErrorAsync(
                result.ErrorTitle ?? "Git operation failed",
                result.ErrorMessage);
            return;
        }

        if (!result.LifecycleSucceeded ||
            string.IsNullOrWhiteSpace(result.SelectionCommit))
        {
            return;
        }

        await RestoreCommitActionSelectionAsync(result.SelectionCommit);
    }

    private async Task<int?> SelectMainlineParentAsync(CommitHistoryItem commit, string title)
    {
        if (commit.Parents.Count <= 1) return null;

        var choices = commit.Parents
            .Select((hash, index) => new MainlineChoice(index + 1, $"{index + 1}  {ShortOid(hash)}"))
            .ToArray();
        var selector = new ComboBox
        {
            Header = "Mainline parent",
            ItemsSource = choices,
            DisplayMemberPath = nameof(MainlineChoice.Label),
            SelectedIndex = 0,
            MinWidth = 300
        };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = selector,
            PrimaryButtonText = title.StartsWith("Cherry", StringComparison.Ordinal) ? "Cherry-pick" : "Revert",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary && selector.SelectedItem is MainlineChoice choice
            ? choice.Number
            : null;
    }

    private async Task<bool> RestoreCommitActionSelectionAsync(string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash)) return false;

        var row = await _viewModel.History.NavigateToCommitAsync(hash);
        if (row is null) return false;

        HistoryList.SelectedItem = row;
        HistoryList.ScrollIntoView(row);
        return true;
    }

    private sealed record MainlineChoice(int Number, string Label);
}