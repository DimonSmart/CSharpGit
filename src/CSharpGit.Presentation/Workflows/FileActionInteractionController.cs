using System.ComponentModel;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;
using CSharpGit.Presentation.Previewing;
using CSharpGit.Presentation.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace CSharpGit.Presentation.Workflows;

// Owns commit/working-tree file-action interaction, subscriptions and context menus.
// FileActionsViewModel remains the semantic owner of immutable requests and execution.
internal sealed class FileActionInteractionController(
    OpenRepositoryViewModel viewModel,
    FileActionsViewModel fileActions,
    FileActionAvailabilityController availability,
    TreeView changedFilesTree,
    HistoryWorkspaceView historyWorkspace,
    WorkingTreeView workingTree,
    Func<XamlRoot?> xamlRoot,
    Func<ContentDialog, Task<ContentDialogResult>> showDialog,
    Func<string, string, Task> showError,
    Action<Exception, string> reportUiFailure,
    Action openGitToolsSettings,
    Func<bool> isShuttingDown,
    Action<Func<Task>, string> runUiOperation)
{
    private readonly OpenRepositoryViewModel _viewModel = viewModel;
    private readonly FileActionsViewModel _fileActions = fileActions;
    private readonly FileActionAvailabilityController _availability = availability;
    private readonly TreeView _changedFilesTree = changedFilesTree;
    private readonly HistoryWorkspaceView _historyWorkspace = historyWorkspace;
    private readonly WorkingTreeView _workingTree = workingTree;
    private readonly Func<XamlRoot?> _xamlRoot = xamlRoot;
    private readonly Func<ContentDialog, Task<ContentDialogResult>> _showDialog = showDialog;
    private readonly Func<string, string, Task> _showError = showError;
    private readonly Action<Exception, string> _reportUiFailure = reportUiFailure;
    private readonly Action _openGitToolsSettings = openGitToolsSettings;
    private readonly Func<bool> _isShuttingDown = isShuttingDown;
    private readonly Action<Func<Task>, string> _runUiOperation = runUiOperation;
    private bool _initialized;

    internal void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        _changedFilesTree.RightTapped += ChangedFilesTree_RightTapped;
        _changedFilesTree.DoubleTapped += ChangedFilesTree_DoubleTapped;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.CommitDetails.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.History.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.WorkingTree.PropertyChanged += WorkingTree_PropertyChanged;
        _fileActions.PropertyChanged += FileActions_PropertyChanged;

        _historyWorkspace.CommitOpenOriginal_ClickCallback = CommitOpenOriginal_Click;
        _historyWorkspace.CommitOpenChanged_ClickCallback = CommitOpenChanged_Click;
        _historyWorkspace.CommitExternalDiff_ClickCallback = CommitExternalDiff_Click;
        _historyWorkspace.CommitReveal_ClickCallback = CommitReveal_Click;
        _workingTree.WorkingTreeOpenOriginal_ClickCallback = WorkingTreeOpenOriginal_Click;
        _workingTree.WorkingTreeOpenChanged_ClickCallback = WorkingTreeOpenChanged_Click;
        _workingTree.WorkingTreeExternalDiff_ClickCallback = WorkingTreeExternalDiff_Click;
        _workingTree.WorkingTreeReveal_ClickCallback = WorkingTreeReveal_Click;

        _runUiOperation(RefreshCommitStateAsync, "Commit file action state");
        _runUiOperation(RefreshWorkingTreeStateAsync, "Working-tree file action state");
    }

    private void FileActions_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(FileActionsViewModel.Commit))
            _availability.UpdateCommitButtons();
        else if (args.PropertyName == nameof(FileActionsViewModel.WorkingTree))
            _availability.UpdateWorkingTreeButtons();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(OpenRepositoryViewModel.Repository))
            _fileActions.Invalidate();

        if (args.PropertyName is nameof(CommitDetailsViewModel.SelectedFile)
            or nameof(HistoryViewModel.SelectedRow)
            or nameof(OpenRepositoryViewModel.Repository))
            _runUiOperation(RefreshCommitStateAsync, "Commit file action state");

        if (args.PropertyName == nameof(OpenRepositoryViewModel.Repository))
            _runUiOperation(RefreshWorkingTreeStateAsync, "Working-tree file action state");
    }

    private void WorkingTree_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(WorkingTreeViewModel.SelectedChange)
            or nameof(WorkingTreeViewModel.SelectedDiffKind))
            _runUiOperation(RefreshWorkingTreeStateAsync, "Working-tree file action state");
    }

    internal CommitFileActionRequest? CaptureCommitRequest(string? path = null)
    {
        var repository = _viewModel.Repository;
        var commit = _viewModel.History.SelectedRow?.Commit;
        path ??= _viewModel.CommitDetails.SelectedFile?.Path;
        return repository is null || commit is null || path is null
            ? null
            : new CommitFileActionRequest(repository, commit.Hash, path);
    }

    internal WorkingTreeFileActionRequest? CaptureWorkingTreeRequest(
        WorkingTreeChange? change = null,
        WorkingTreeDiffKind? kind = null)
    {
        var repository = _viewModel.Repository;
        change ??= _viewModel.WorkingTree.SelectedChange;
        kind ??= _viewModel.WorkingTree.SelectedDiffKind;
        return repository is null || change is null || kind is null
            ? null
            : new WorkingTreeFileActionRequest(repository, change, kind.Value);
    }

    private Task RefreshCommitStateAsync()
    {
        _fileActions.SelectCommitLazy(CaptureCommitRequest());
        return Task.CompletedTask;
    }

    private Task RefreshWorkingTreeStateAsync() =>
        _fileActions.SelectWorkingTreeAsync(CaptureWorkingTreeRequest());

    private async Task ExecuteAsync(
        FileActionRequest? request,
        FileActionKind action,
        string errorTitle,
        PreparedFileActions? prepared = null)
    {
        if (request is null) return;
        try
        {
            var result = await _fileActions.ExecuteAsync(request, action, prepared);
            switch (result.Kind)
            {
                case FileActionOutcomeKind.NotConfigured:
                    await ShowDiffToolNotConfiguredAsync();
                    break;
                case FileActionOutcomeKind.Failed:
                case FileActionOutcomeKind.Unavailable:
                    if (_fileActions.IsCurrent(request))
                        await _showError(errorTitle, result.Reason ?? "The file operation failed.");
                    break;
            }
        }
        catch (OperationCanceledException) when (_isShuttingDown() || !_fileActions.IsCurrent(request))
        {
        }
        catch (Exception exception)
        {
            _reportUiFailure(exception, "File action");
            if (!_isShuttingDown() && _fileActions.IsCurrent(request))
                await _showError(errorTitle, FileActionsViewModel.UserFacingFileError(exception));
        }
    }

    private void CommitOpenOriginal_Click(object sender, RoutedEventArgs args) =>
        _runUiOperation(() => ExecuteAsync(CaptureCommitRequest(), FileActionKind.Original,
            "Could not open original version"), "CommitOpenOriginal_Click");

    private void CommitOpenChanged_Click(object sender, RoutedEventArgs args) =>
        _runUiOperation(() => ExecuteAsync(CaptureCommitRequest(), FileActionKind.Changed,
            "Could not open changed version"), "CommitOpenChanged_Click");

    private void CommitExternalDiff_Click(object sender, RoutedEventArgs args) =>
        _runUiOperation(() => ExecuteAsync(CaptureCommitRequest(), FileActionKind.ExternalDiff,
            "Could not open external diff tool"), "CommitExternalDiff_Click");

    private void CommitReveal_Click(object sender, RoutedEventArgs args) =>
        _runUiOperation(() => ExecuteAsync(CaptureCommitRequest(), FileActionKind.Reveal,
            "Could not reveal file"), "CommitReveal_Click");

    private void WorkingTreeOpenOriginal_Click(object sender, RoutedEventArgs args) =>
        _runUiOperation(() => ExecuteAsync(CaptureWorkingTreeRequest(), FileActionKind.Original,
            "Could not open original version"), "WorkingTreeOpenOriginal_Click");

    private void WorkingTreeOpenChanged_Click(object sender, RoutedEventArgs args) =>
        _runUiOperation(() => ExecuteAsync(CaptureWorkingTreeRequest(), FileActionKind.Changed,
            "Could not open changed version"), "WorkingTreeOpenChanged_Click");

    private void WorkingTreeExternalDiff_Click(object sender, RoutedEventArgs args) =>
        _runUiOperation(() => ExecuteAsync(CaptureWorkingTreeRequest(), FileActionKind.ExternalDiff,
            "Could not open external diff tool"), "WorkingTreeExternalDiff_Click");

    private void WorkingTreeReveal_Click(object sender, RoutedEventArgs args) =>
        _runUiOperation(() => ExecuteAsync(CaptureWorkingTreeRequest(), FileActionKind.Reveal,
            "Could not reveal file"), "WorkingTreeReveal_Click");

    private async Task ShowDiffToolNotConfiguredAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = _xamlRoot(),
            Title = "Diff tool is not configured",
            Content = "Configure a Git diff tool in Settings → Git Tools.",
            PrimaryButtonText = "Open Git Tools Settings",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await _showDialog(dialog) == ContentDialogResult.Primary)
            _openGitToolsSettings();
    }

    private void ChangedFilesTree_DoubleTapped(object sender, DoubleTappedRoutedEventArgs args) =>
        _runUiOperation(() => ChangedFilesTree_DoubleTappedCoreAsync(args), "ChangedFilesTree_DoubleTapped");

    private async Task ChangedFilesTree_DoubleTappedCoreAsync(DoubleTappedRoutedEventArgs args)
    {
        var source = args.OriginalSource as FrameworkElement;
        var node = CommitChangedFileTreeController.ResolveChangedFileNode(source?.DataContext);
        if (node?.Entry is null) return;

        var request = CaptureCommitRequest(node.Entry.File.Path);
        if (request is null) return;
        _viewModel.CommitDetails.SelectedFile = node.Entry.File;
        args.Handled = true;
        await ExecuteAsync(request, FileActionKind.Preferred, "Could not open file");
    }

    private void ChangedFilesTree_RightTapped(object sender, RightTappedRoutedEventArgs args) =>
        _runUiOperation(() => ChangedFilesTree_RightTappedCoreAsync(args), "ChangedFilesTree_RightTapped");

    private async Task ChangedFilesTree_RightTappedCoreAsync(RightTappedRoutedEventArgs args)
    {
        var source = args.OriginalSource as FrameworkElement;
        var node = CommitChangedFileTreeController.ResolveChangedFileNode(source?.DataContext);
        if (source is null || node?.Entry is null) return;

        var request = CaptureCommitRequest(node.Entry.File.Path);
        if (request is null) return;
        _viewModel.CommitDetails.SelectedFile = node.Entry.File;
        args.Handled = true;
        await ShowMenuAsync(source, request);
    }

    internal async Task ShowMenuAsync(FrameworkElement source, FileActionRequest request)
    {
        try
        {
            var prepared = await _fileActions.PrepareAsync(request);
            if (_isShuttingDown() || prepared is null || !_fileActions.IsCurrent(request)
                || source.XamlRoot is null || !IsMenuSelectionCurrent(request))
                return;

            if (request is CommitFileActionRequest commit)
                _fileActions.PublishCommitPreview(commit, prepared.Pair);
            else if (request is WorkingTreeFileActionRequest change)
                _fileActions.PublishWorkingTreePreview(change, prepared.Pair);

            BuildMenu(request, prepared).ShowAt(source);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (_fileActions.IsCurrent(request) && !_isShuttingDown())
                await _showError("Could not read file versions",
                    FileActionsViewModel.UserFacingFileError(exception));
        }
    }

    internal bool IsMenuSelectionCurrent(FileActionRequest request)
    {
        if (!_fileActions.IsCurrent(request)) return false;
        return request switch
        {
            CommitFileActionRequest commit =>
                CaptureCommitRequest() is { } current && Equals(current, commit),
            WorkingTreeFileActionRequest change =>
                CaptureWorkingTreeRequest() is { } current
                && ReferenceEquals(current.Repository, change.Repository)
                && current.Kind == change.Kind
                && WorkingTreeView.SameWorkingTreeChange(current.Change, change.Change),
            _ => false
        };
    }

    internal MenuFlyout BuildMenu(
        FileActionRequest request,
        PreparedFileActions prepared,
        bool separateReveal = true)
    {
        var pair = prepared.Pair;
        var flyout = new MenuFlyout();
        flyout.Items.Add(CreateMenuItem("Open changed", pair.Changed,
            () => ExecuteAsync(request, FileActionKind.Changed, "Could not open changed version", prepared)));
        flyout.Items.Add(CreateMenuItem("Open original", pair.Original,
            () => ExecuteAsync(request, FileActionKind.Original, "Could not open original version", prepared)));

        var externalItem = new MenuFlyoutItem
        {
            Text = "Open in Diff Tool",
            IsEnabled = prepared.CanExternalDiff
        };
        ToolTipService.SetToolTip(externalItem, prepared.CanExternalDiff
            ? externalItem.Text
            : FileActionsViewModel.ExternalDiffUnavailableReason(pair));
        externalItem.Click += (_, _) => _runUiOperation(
            () => ExecuteAsync(request, FileActionKind.ExternalDiff, "Could not open external diff tool", prepared),
            "External file diff");
        flyout.Items.Add(externalItem);

        if (separateReveal) flyout.Items.Add(new MenuFlyoutSeparator());

        var revealItem = new MenuFlyoutItem
        {
            Text = _fileActions.RevealDescription,
            IsEnabled = prepared.CanReveal
        };
        ToolTipService.SetToolTip(revealItem, prepared.CanReveal
            ? revealItem.Text
            : "The file is not present in the current working tree.");
        revealItem.Click += (_, _) => _runUiOperation(
            () => ExecuteAsync(request, FileActionKind.Reveal, "Could not reveal file", prepared),
            "Reveal file");
        flyout.Items.Add(revealItem);
        return flyout;
    }

    private MenuFlyoutItem CreateMenuItem(string text, DiffFileVersion version, Func<Task> action)
    {
        var item = new MenuFlyoutItem { Text = text, IsEnabled = version.CanOpen };
        ToolTipService.SetToolTip(item, version.CanOpen
            ? text
            : version.UnavailableReason ?? "This version is unavailable.");
        item.Click += (_, _) => _runUiOperation(action, "File menu action");
        return item;
    }

    internal void Shutdown()
    {
        if (!_initialized) return;
        _initialized = false;
        _changedFilesTree.RightTapped -= ChangedFilesTree_RightTapped;
        _changedFilesTree.DoubleTapped -= ChangedFilesTree_DoubleTapped;
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _viewModel.CommitDetails.PropertyChanged -= ViewModel_PropertyChanged;
        _viewModel.History.PropertyChanged -= ViewModel_PropertyChanged;
        _viewModel.WorkingTree.PropertyChanged -= WorkingTree_PropertyChanged;
        _fileActions.PropertyChanged -= FileActions_PropertyChanged;

        _historyWorkspace.CommitOpenOriginal_ClickCallback = null;
        _historyWorkspace.CommitOpenChanged_ClickCallback = null;
        _historyWorkspace.CommitExternalDiff_ClickCallback = null;
        _historyWorkspace.CommitReveal_ClickCallback = null;
        _workingTree.WorkingTreeOpenOriginal_ClickCallback = null;
        _workingTree.WorkingTreeOpenChanged_ClickCallback = null;
        _workingTree.WorkingTreeExternalDiff_ClickCallback = null;
        _workingTree.WorkingTreeReveal_ClickCallback = null;
    }
}
