using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;
using CSharpGit.Presentation.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace CSharpGit.Presentation.Workflows;

// Owns History context actions and their dialog choreography. Git semantics remain in ViewModels.
internal sealed class CommitActionController(
    OpenRepositoryViewModel viewModel,
    HistoryView historyView,
    EditCommitMessageController editCommitMessage,
    TagWorkflowController tagWorkflows,
    Func<XamlRoot?> xamlRoot,
    Func<ContentDialog, Task<ContentDialogResult>> showDialog,
    Func<string, string, Task> showError,
    Func<bool> isShuttingDown,
    Func<Task> showInteractiveRebaseEditor,
    Action<Func<Task>, string> runUiOperation)
{
    private readonly OpenRepositoryViewModel _viewModel = viewModel;
    private readonly HistoryView _historyView = historyView;
    private readonly EditCommitMessageController _editCommitMessage = editCommitMessage;
    private readonly TagWorkflowController _tagWorkflows = tagWorkflows;
    private readonly Func<XamlRoot?> _xamlRoot = xamlRoot;
    private readonly Func<ContentDialog, Task<ContentDialogResult>> _showDialog = showDialog;
    private readonly Func<string, string, Task> _showError = showError;
    private readonly Func<bool> _isShuttingDown = isShuttingDown;
    private readonly Func<Task> _showInteractiveRebaseEditor = showInteractiveRebaseEditor;
    private readonly Action<Func<Task>, string> _runUiOperation = runUiOperation;

    private MenuFlyout? _flyout;
    private MenuFlyoutItem? _copyHashItem;
    private MenuFlyoutItem? _createBranchHereItem;
    private MenuFlyoutItem? _checkoutCommitItem;
    private MenuFlyoutItem? _cherryPickItem;
    private MenuFlyoutItem? _revertItem;
    private MenuFlyoutItem? _editCommitMessageItem;
    private MenuFlyoutItem? _fixupIntoPreviousCommitItem;
    private MenuFlyoutItem? _interactiveRebaseFromHereItem;
    private MenuFlyoutSubItem? _resetItem;
    private MenuFlyoutSubItem? _deleteTagSubItem;
    private bool _initialized;

    internal void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

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

        _deleteTagSubItem = new MenuFlyoutSubItem { Text = "Delete tag" };

        _flyout = new MenuFlyout();
        _flyout.Items.Add(_copyHashItem);
        _flyout.Items.Add(new MenuFlyoutSeparator());
        _flyout.Items.Add(_createBranchHereItem);
        _flyout.Items.Add(CreateCreateTagItem());
        _flyout.Items.Add(_checkoutCommitItem);
        _flyout.Items.Add(_deleteTagSubItem);
        _flyout.Items.Add(new MenuFlyoutSeparator());
        _flyout.Items.Add(_cherryPickItem);
        _flyout.Items.Add(_revertItem);
        _flyout.Items.Add(_editCommitMessageItem);
        _flyout.Items.Add(_fixupIntoPreviousCommitItem);
        _flyout.Items.Add(_interactiveRebaseFromHereItem);
        _flyout.Items.Add(new MenuFlyoutSeparator());
        _flyout.Items.Add(_resetItem);
        _flyout.Opening += Flyout_Opening;

        _historyView.List.ContextFlyout = _flyout;
        _historyView.List.RightTapped += HistoryList_RightTapped;
    }

    private MenuFlyoutItem CreateCreateTagItem()
    {
        var item = new MenuFlyoutItem { Text = "Create tag here…" };
        item.Click += (_, _) =>
        {
            if (_viewModel.History.SelectedRow?.Commit.Hash is { } hash)
                _runUiOperation(() => _tagWorkflows.ShowCreateTagDialogAsync(hash, selectedCommit: true), "Create tag here");
        };
        return item;
    }

    private void Flyout_Opening(object? sender, object args)
    {
        UpdateAvailability();
        UpdateDeleteTagSubmenu();
    }

    private void HistoryList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var current = e.OriginalSource as DependencyObject;
        while (current is not null && current is not ListViewItem)
            current = VisualTreeHelper.GetParent(current);

        if (current is ListViewItem { Content: HistoryRow row })
        {
            _historyView.List.SelectedItem = row;
            _viewModel.History.SelectedRow = row;
        }

        UpdateAvailability();
    }

    private void UpdateAvailability()
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
        if (_interactiveRebaseFromHereItem is not null)
            _interactiveRebaseFromHereItem.IsEnabled = canMutate && hasLocalBranch;
        if (_resetItem is not null) _resetItem.IsEnabled = _viewModel.CommitActions.CanReset(commit);
    }

    private void UpdateDeleteTagSubmenu()
    {
        if (_deleteTagSubItem is null) return;
        _deleteTagSubItem.Items.Clear();

        var selectedCommitHash = _viewModel.History.SelectedRow?.Commit.Hash;
        if (string.IsNullOrWhiteSpace(selectedCommitHash))
        {
            _deleteTagSubItem.IsEnabled = false;
            return;
        }

        var canMutate = _viewModel.Tags.CanMutate;
        foreach (var tag in _viewModel.Tags.Items)
        {
            if (!string.Equals(tag.TargetCommit, selectedCommitHash, StringComparison.Ordinal)) continue;
            var item = new MenuFlyoutItem { Text = tag.Name, IsEnabled = canMutate };
            item.Click += (_, _) => _runUiOperation(() => _tagWorkflows.DeleteTagFromUiAsync(tag), "Delete tag");
            _deleteTagSubItem.Items.Add(item);
        }

        _deleteTagSubItem.IsEnabled = canMutate && _deleteTagSubItem.Items.Count > 0;
    }

    private void CopyCommitHash_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.History.SelectedRow?.Commit.Hash is not { } hash) return;
        var package = new DataPackage();
        package.SetText(hash);
        Clipboard.SetContent(package);
    }

    private void EditCommitMessage_Click(object sender, RoutedEventArgs e) =>
        _runUiOperation(_editCommitMessage.EditAsync, "EditCommitMessage_Click");

    private void CreateBranchHere_Click(object sender, RoutedEventArgs e) =>
        _runUiOperation(CreateBranchHereAsync, "CreateBranchHere");

    private async Task CreateBranchHereAsync()
    {
        if (!TryGetContext(out _, out var commit)) return;
        await CreateBranchFromReferenceAsync(commit.Hash, commit.Hash);
    }

    internal async Task CreateBranchFromReferenceAsync(string startPoint, string? restoreSelectionCommit = null)
    {
        if (_viewModel.Repository is null || _viewModel.IsBusy
            || _viewModel.RepositoryOperations.CurrentOperation != RepositoryOperation.None) return;

        var branchName = new TextBox
        {
            Style = UiStyles.Resolve<Style>("CompactTextBoxStyle"),
            Header = "Name",
            PlaceholderText = "feature/foo"
        };
        var switchToBranch = new CheckBox
        {
            Style = UiStyles.Resolve<Style>("CompactCheckBoxStyle"),
            Content = "Switch to the new branch",
            IsChecked = true
        };
        var content = new StackPanel { Width = 420, Spacing = 10 };
        content.Children.Add(branchName);
        content.Children.Add(switchToBranch);
        var dialog = new ContentDialog
        {
            XamlRoot = _xamlRoot(),
            Title = "Create branch",
            Content = content,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        var originalRepository = _viewModel.Repository;
        if (await _showDialog(dialog) != ContentDialogResult.Primary
            || _isShuttingDown() || !ReferenceEquals(originalRepository, _viewModel.Repository)
            || _viewModel.IsBusy) return;
        if (string.IsNullOrWhiteSpace(branchName.Text))
        {
            await _showError("Branch name required", "Enter a branch name.");
            return;
        }

        var repository = _viewModel.Repository;
        if (repository is null) return;
        if (await _viewModel.Branches.CreateBranchAsync(
                repository, branchName.Text.Trim(), startPoint, switchToBranch.IsChecked == true)
            && !string.IsNullOrWhiteSpace(restoreSelectionCommit))
            await RestoreSelectionAsync(restoreSelectionCommit);
    }

    private void CheckoutCommit_Click(object sender, RoutedEventArgs e) =>
        _runUiOperation(CheckoutCommitAsync, "CheckoutCommit");

    private async Task CheckoutCommitAsync()
    {
        if (!TryGetContext(out var repository, out var commit)) return;
        var hash = commit.Hash;
        var dialog = new ContentDialog
        {
            XamlRoot = _xamlRoot(),
            Title = $"Checkout commit {ShortOid(hash)}?",
            Content = "This will leave the current branch and switch to a detached HEAD.",
            PrimaryButtonText = "Checkout",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await _showDialog(dialog) != ContentDialogResult.Primary || !CanExecute(repository, commit)) return;
        await HandleExecutionResultAsync(await _viewModel.CommitActions.CheckoutAsync(repository, commit));
    }

    private void CherryPickCommit_Click(object sender, RoutedEventArgs e) =>
        _runUiOperation(CherryPickCommitAsync, "CherryPickCommit");

    private async Task CherryPickCommitAsync()
    {
        if (!TryGetContext(out var repository, out var commit)) return;
        var mainline = await SelectMainlineParentAsync(commit, "Cherry-pick merge commit");
        if ((commit.Parents.Count > 1 && mainline is null) || !CanExecute(repository, commit)) return;
        await HandleExecutionResultAsync(await _viewModel.CommitActions.CherryPickAsync(repository, commit, mainline));
    }

    private void RevertCommit_Click(object sender, RoutedEventArgs e) =>
        _runUiOperation(RevertCommitAsync, "RevertCommit");

    private async Task RevertCommitAsync()
    {
        if (!TryGetContext(out var repository, out var commit)) return;
        var mainline = await SelectMainlineParentAsync(commit, "Revert merge commit");
        if ((commit.Parents.Count > 1 && mainline is null) || !CanExecute(repository, commit)) return;
        await HandleExecutionResultAsync(await _viewModel.CommitActions.RevertAsync(repository, commit, mainline));
    }

    private void FixupIntoPreviousCommit_Click(object sender, RoutedEventArgs e) =>
        _runUiOperation(FixupIntoPreviousCommitAsync, "FixupIntoPreviousCommit");

    private async Task FixupIntoPreviousCommitAsync()
    {
        if (!TryGetContext(out var repository, out var commit) || !CanExecute(repository, commit)) return;
        await HandleExecutionResultAsync(await _viewModel.CommitActions.FixupAsync(repository, commit));
    }

    private void InteractiveRebaseFromHere_Click(object sender, RoutedEventArgs e) =>
        _runUiOperation(InteractiveRebaseFromHereAsync, "InteractiveRebaseFromHere");

    private async Task InteractiveRebaseFromHereAsync()
    {
        if (!TryGetContext(out _, out var commit)) return;
        if (await _viewModel.InteractiveRebase.PrepareInteractiveRebaseFromCommitAsync(commit.Hash))
            await _showInteractiveRebaseEditor();
    }

    private void ResetCommit_Click(object sender, RoutedEventArgs e) =>
        _runUiOperation(() => ResetCommitAsync(sender), "Reset commit");

    private async Task ResetCommitAsync(object sender)
    {
        if (sender is not MenuFlyoutItem { Tag: ResetMode mode }) return;
        if (!TryGetContext(out var repository, out var commit)) return;

        var hash = commit.Hash;
        var (title, body, action) = mode switch
        {
            ResetMode.Soft => ($"Soft reset to {ShortOid(hash)}?",
                "Move the current branch to the selected commit.\n\nIndex and working-tree changes will be preserved.", "Soft reset"),
            ResetMode.Mixed => ($"Mixed reset to {ShortOid(hash)}?",
                "Move the current branch to the selected commit.\n\nThe index will be reset. Working-tree files will be preserved.", "Mixed reset"),
            ResetMode.Hard => ($"Hard reset to {ShortOid(hash)}?",
                "The current branch, index and tracked working-tree files will be reset to the selected commit.\n\nUncommitted tracked changes will be lost. Untracked files will not be deleted.", "Hard reset"),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

        var dialog = new ContentDialog
        {
            XamlRoot = _xamlRoot(),
            Title = title,
            Content = body,
            PrimaryButtonText = action,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await _showDialog(dialog) != ContentDialogResult.Primary || !CanExecute(repository, commit)) return;
        await HandleExecutionResultAsync(await _viewModel.CommitActions.ResetAsync(repository, commit, mode));
    }

    private bool CanExecute(Repository repository, CommitHistoryItem commit) =>
        !_isShuttingDown()
        && ReferenceEquals(repository, _viewModel.Repository)
        && _viewModel.CommitActions.CanMutateCommit(commit);

    private bool TryGetContext(out Repository repository, out CommitHistoryItem commit)
    {
        repository = _viewModel.Repository!;
        commit = _viewModel.History.SelectedRow?.Commit!;
        return !_isShuttingDown()
               && repository is not null
               && commit is not null
               && _viewModel.CommitActions.CanMutateCommit(commit);
    }

    internal async Task HandleExecutionResultAsync(CommitActionExecutionResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            await _showError(result.ErrorTitle ?? "Git operation failed", result.ErrorMessage);
            return;
        }

        if (!result.LifecycleSucceeded || string.IsNullOrWhiteSpace(result.SelectionCommit)) return;
        if (!await RestoreSelectionAsync(result.SelectionCommit)
            && !string.IsNullOrWhiteSpace(result.FallbackSelectionCommit))
            await RestoreSelectionAsync(result.FallbackSelectionCommit);
    }

    private async Task<int?> SelectMainlineParentAsync(CommitHistoryItem commit, string title)
    {
        if (commit.Parents.Count <= 1) return null;
        var choices = commit.Parents
            .Select((hash, index) => new MainlineChoice(index + 1, $"{index + 1}  {ShortOid(hash)}"))
            .ToArray();
        var selector = new ComboBox
        {
            Style = UiStyles.Resolve<Style>("CompactComboBoxStyle"),
            Header = "Mainline parent",
            ItemsSource = choices,
            DisplayMemberPath = nameof(MainlineChoice.Label),
            SelectedIndex = 0,
            MinWidth = 300
        };
        var dialog = new ContentDialog
        {
            XamlRoot = _xamlRoot(),
            Title = title,
            Content = selector,
            PrimaryButtonText = title.StartsWith("Cherry", StringComparison.Ordinal) ? "Cherry-pick" : "Revert",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        return await _showDialog(dialog) == ContentDialogResult.Primary
               && selector.SelectedItem is MainlineChoice choice ? choice.Number : null;
    }

    internal async Task<bool> RestoreSelectionAsync(string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash)) return false;
        var repository = _viewModel.Repository;
        var row = await _viewModel.History.NavigateToCommitAsync(hash);
        if (_isShuttingDown() || !ReferenceEquals(repository, _viewModel.Repository) || row is null)
            return false;

        _historyView.List.SelectedItem = row;
        _historyView.List.ScrollIntoView(row);
        return true;
    }

    internal void Shutdown()
    {
        if (!_initialized) return;
        _initialized = false;
        _historyView.List.RightTapped -= HistoryList_RightTapped;
        if (ReferenceEquals(_historyView.List.ContextFlyout, _flyout))
            _historyView.List.ContextFlyout = null;
        if (_flyout is not null) _flyout.Opening -= Flyout_Opening;
    }

    private static string ShortOid(string oid) => oid[..Math.Min(10, oid.Length)];
    private sealed record MainlineChoice(int Number, string Label);
}
