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

        _resetItem = new MenuFlyoutSubItem { Text = "Reset current branch to here" };
        foreach (var mode in Enum.GetValues<ResetMode>())
        {
            var item = new MenuFlyoutItem { Text = $"{mode}…", Tag = mode };
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
        _commitActionsFlyout.Items.Add(new MenuFlyoutSeparator());
        _commitActionsFlyout.Items.Add(_resetItem);
        _commitActionsFlyout.Opening += (_, _) => UpdateCommitActionAvailability();

        HistoryList.ContextFlyout = _commitActionsFlyout;
        HistoryList.RightTapped += HistoryList_RightTapped;
    }

    private void HistoryList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var current = e.OriginalSource as DependencyObject;
        while (current is not null && current is not ListViewItem)
            current = VisualTreeHelper.GetParent(current);

        if (current is ListViewItem { Content: HistoryRow row })
        {
            HistoryList.SelectedItem = row;
            _viewModel.SelectedHistoryRow = row;
        }

        UpdateCommitActionAvailability();
    }

    private void UpdateCommitActionAvailability()
    {
        var hasCommit = _viewModel.SelectedHistoryRow is not null;
        var canMutate = hasCommit &&
                        _viewModel.Repository is not null &&
                        !_viewModel.IsBusy &&
                        _viewModel.CurrentOperation == RepositoryOperation.None;
        var hasLocalBranch = _viewModel.LocalBranches.Any(branch => branch.IsCurrent);

        if (_copyHashItem is not null) _copyHashItem.IsEnabled = hasCommit;
        if (_createBranchHereItem is not null) _createBranchHereItem.IsEnabled = canMutate;
        if (_checkoutCommitItem is not null) _checkoutCommitItem.IsEnabled = canMutate;
        if (_cherryPickItem is not null) _cherryPickItem.IsEnabled = canMutate;
        if (_revertItem is not null) _revertItem.IsEnabled = canMutate;
        if (_resetItem is not null) _resetItem.IsEnabled = canMutate && hasLocalBranch;
    }

    private void CopyCommitHash_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedHistoryRow?.Commit.Hash is not { } hash) return;
        var package = new DataPackage();
        package.SetText(hash);
        Clipboard.SetContent(package);
    }

    private async void CreateBranchHere_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetCommitActionContext(out var repository, out var commit)) return;

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

        var hash = commit.Hash;
        var switched = switchToBranch.IsChecked == true;
        if (await _viewModel.RunMutationAsync(
                () => _referenceService.CreateBranchAsync(repository, branchName.Text.Trim(), hash, switched),
                "Could not create branch"))
            await RestoreCommitActionSelectionAsync(hash);
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

        if (await _viewModel.RunMutationAsync(
                () => _referenceService.CheckoutAsync(repository, hash),
                "Could not checkout commit"))
            await RestoreCommitActionSelectionAsync(hash);
    }

    private async void CherryPickCommit_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetCommitActionContext(out var repository, out var commit)) return;
        var mainline = await SelectMainlineParentAsync(commit, "Cherry-pick merge commit");
        if (commit.Parents.Count > 1 && mainline is null) return;

        ApplyCommitResult? result = null;
        var succeeded = await _viewModel.RunMutationAsync(
            async () => result = await _referenceService.CherryPickAsync(repository, commit.Hash, mainline),
            "Could not cherry-pick commit");
        if (!succeeded || result is null) return;

        if (result.Kind == ApplyCommitResultKind.Failed)
        {
            await ShowErrorAsync("Cherry-pick failed", result.Message);
            return;
        }

        var selection = result.Kind == ApplyCommitResultKind.Completed ? result.HeadCommit : commit.Hash;
        await RestoreCommitActionSelectionAsync(selection);
    }

    private async void RevertCommit_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetCommitActionContext(out var repository, out var commit)) return;
        var mainline = await SelectMainlineParentAsync(commit, "Revert merge commit");
        if (commit.Parents.Count > 1 && mainline is null) return;

        ApplyCommitResult? result = null;
        var succeeded = await _viewModel.RunMutationAsync(
            async () => result = await _referenceService.RevertAsync(repository, commit.Hash, mainline),
            "Could not revert commit");
        if (!succeeded || result is null) return;

        if (result.Kind == ApplyCommitResultKind.Failed)
        {
            await ShowErrorAsync("Revert failed", result.Message);
            return;
        }

        var selection = result.Kind == ApplyCommitResultKind.Completed ? result.HeadCommit : commit.Hash;
        await RestoreCommitActionSelectionAsync(selection);
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

        if (await _viewModel.RunMutationAsync(
                () => _referenceService.ResetAsync(repository, hash, mode),
                $"Could not {mode.ToString().ToLowerInvariant()} reset"))
            await RestoreCommitActionSelectionAsync(hash);
    }

    private bool TryGetCommitActionContext(out Repository repository, out CommitHistoryItem commit)
    {
        repository = _viewModel.Repository!;
        commit = _viewModel.SelectedHistoryRow?.Commit!;
        return repository is not null &&
               commit is not null &&
               !_viewModel.IsBusy &&
               _viewModel.CurrentOperation == RepositoryOperation.None;
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

    private async Task RestoreCommitActionSelectionAsync(string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash)) return;

        if (_activeReference is not null)
            await LoadScopedHistoryAsync(true);

        var rows = _activeReference is null ? _viewModel.History : _scopedHistory;
        var row = rows.FirstOrDefault(candidate => string.Equals(candidate.Commit.Hash, hash, StringComparison.Ordinal));
        if (row is null) return;

        _viewModel.SelectedHistoryRow = row;
        HistoryList.SelectedItem = row;
        HistoryList.ScrollIntoView(row);
    }

    private sealed record MainlineChoice(int Number, string Label);
}
