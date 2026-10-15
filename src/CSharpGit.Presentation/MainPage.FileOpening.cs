using System.ComponentModel;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    // The repository-files preview/editor is an independent scenario and still uses these services.
    private readonly IRepositoryFileVersionService _fileVersionService = null!;
    private readonly IExternalGitToolService _externalGitToolService = null!;
    private readonly FileActionsViewModel _fileActions = null!;

    private MainPage(
        OpenRepositoryViewModel viewModel,
        IWorkingTreeStatusReader workingTreeStatusReader,
        IRepositoryFileVersionService fileVersionService,
        IExternalGitToolService externalGitToolService,
        FileActionsViewModel fileActions)
        : this(viewModel, workingTreeStatusReader)
    {
        _fileVersionService = fileVersionService ?? throw new ArgumentNullException(nameof(fileVersionService));
        _externalGitToolService = externalGitToolService ?? throw new ArgumentNullException(nameof(externalGitToolService));
        _fileActions = fileActions ?? throw new ArgumentNullException(nameof(fileActions));
        _fileActions.Attach(_viewModel);
        InitializeFileOpening();
    }

    private void InitializeFileOpening()
    {
        ChangedFilesTree.RightTapped += ChangedFilesTree_RightTapped;
        ChangedFilesTree.DoubleTapped += ChangedFilesTree_DoubleTapped;
        UnstagedChangesList.RightTapped += WorkingTreeChanges_RightTapped;
        StagedChangesList.RightTapped += WorkingTreeChanges_RightTapped;
        UnstagedChangesList.DoubleTapped += WorkingTreeChanges_DoubleTapped;
        StagedChangesList.DoubleTapped += WorkingTreeChanges_DoubleTapped;
        _viewModel.PropertyChanged += FileOpeningViewModel_PropertyChanged;
        _viewModel.CommitDetails.PropertyChanged += FileOpeningViewModel_PropertyChanged;
        _viewModel.History.PropertyChanged += FileOpeningViewModel_PropertyChanged;
        _viewModel.WorkingTree.PropertyChanged += WorkingTreeFileOpening_PropertyChanged;
        _fileActions.PropertyChanged += FileActions_PropertyChanged;

        RunPageUiOperation(RefreshCommitFileActionStateAsync, "Commit file action state");
        RunPageUiOperation(RefreshWorkingTreeFileActionStateAsync, "Working-tree file action state");
    }

    private void FileActions_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(FileActionsViewModel.Commit))
            UpdateCommitButtons();
        else if (args.PropertyName == nameof(FileActionsViewModel.WorkingTree))
            UpdateWorkingTreeButtons();
    }

    private void FileOpeningViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(OpenRepositoryViewModel.Repository))
            _fileActions.Invalidate();

        if (args.PropertyName is nameof(CommitDetailsViewModel.SelectedFile)
            or nameof(HistoryViewModel.SelectedRow)
            or nameof(OpenRepositoryViewModel.Repository))
            RunPageUiOperation(RefreshCommitFileActionStateAsync, "Commit file action state");

        if (args.PropertyName == nameof(OpenRepositoryViewModel.Repository))
            RunPageUiOperation(RefreshWorkingTreeFileActionStateAsync, "Working-tree file action state");
    }

    private void WorkingTreeFileOpening_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(WorkingTreeViewModel.SelectedChange)
            or nameof(WorkingTreeViewModel.SelectedDiffKind))
            RunPageUiOperation(RefreshWorkingTreeFileActionStateAsync, "Working-tree file action state");
    }

    private CommitFileActionRequest? CaptureCommitRequest(string? path = null)
    {
        var repository = _viewModel.Repository;
        var commit = _viewModel.History.SelectedRow?.Commit;
        path ??= _viewModel.CommitDetails.SelectedFile?.Path;
        return repository is null || commit is null || path is null
            ? null : new CommitFileActionRequest(repository, commit.Hash, path);
    }

    private WorkingTreeFileActionRequest? CaptureWorkingTreeRequest(
        WorkingTreeChange? change = null, WorkingTreeDiffKind? kind = null)
    {
        var repository = _viewModel.Repository;
        change ??= _viewModel.WorkingTree.SelectedChange;
        kind ??= _viewModel.WorkingTree.SelectedDiffKind;
        return repository is null || change is null || kind is null
            ? null : new WorkingTreeFileActionRequest(repository, change, kind.Value);
    }

    private Task RefreshCommitFileActionStateAsync()
    {
        _fileActions.SelectCommitLazy(CaptureCommitRequest());
        return Task.CompletedTask;
    }

    private Task RefreshWorkingTreeFileActionStateAsync() =>
        _fileActions.SelectWorkingTreeAsync(CaptureWorkingTreeRequest());

    private void UpdateCommitButtons()
    {
        var pair = _fileActions.Commit.Prepared?.Pair;
        if (pair is not null)
        {
            SetVersionButton(CommitOpenOriginalButton, pair.Original, "No original version is available.");
            SetVersionButton(CommitOpenChangedButton, pair.Changed, "No changed version is available.");
            SetExternalDiffButton(CommitExternalDiffButton, pair);
        }
        else
        {
            // Preserve the existing conservative/optimistic button state during loading.
            var file = _viewModel.CommitDetails.SelectedFile;
            SetAvailabilityButton(CommitOpenOriginalButton,
                file is not null && file.Status != "A", "Open original version",
                "No original version is available.");
            SetAvailabilityButton(CommitOpenChangedButton,
                file is not null && file.Status != "D", "Open changed version",
                "No changed version is available.");
            SetAvailabilityButton(CommitExternalDiffButton, file is not null,
                "Open this exact OLD/NEW pair in the configured external diff tool",
                "Select a file diff first.");
        }
        SetRevealButton(CommitRevealButton, _fileActions.Commit.Prepared?.RevealPath ?? _fileActions.Commit.RevealPath);
    }

    private void UpdateWorkingTreeButtons()
    {
        var prepared = _fileActions.WorkingTree.Prepared;
        SetVersionButton(WorkingTreeOpenOriginalButton, prepared?.Pair.Original, "No original version is available.");
        SetVersionButton(WorkingTreeOpenChangedButton, prepared?.Pair.Changed, "No changed version is available.");
        SetExternalDiffButton(WorkingTreeExternalDiffButton, prepared?.Pair);
        SetRevealButton(WorkingTreeRevealButton, prepared?.RevealPath);
    }

    private static void SetAvailabilityButton(Button? button, bool enabled, string enabledText, string disabledText)
    {
        if (button is null) return;
        button.IsEnabled = enabled;
        ToolTipService.SetToolTip(button, enabled ? enabledText : disabledText);
    }

    private static void SetVersionButton(Button? button, DiffFileVersion? version, string fallbackReason)
    {
        if (button is null) return;
        button.IsEnabled = version?.CanOpen == true;
        ToolTipService.SetToolTip(button, version?.CanOpen == true
            ? Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button)
            : version?.UnavailableReason ?? fallbackReason);
    }

    private static void SetExternalDiffButton(Button? button, DiffFileVersionPair? pair)
    {
        if (button is null) return;
        var enabled = pair is not null && FileActionsViewModel.CanExternalDiff(pair);
        button.IsEnabled = enabled;
        ToolTipService.SetToolTip(button, enabled
            ? "Open this exact OLD/NEW pair in the configured external diff tool"
            : FileActionsViewModel.ExternalDiffUnavailableReason(pair));
    }

    private void SetRevealButton(Button? button, string? path)
    {
        if (button is null) return;
        button.IsEnabled = path is not null;
        ToolTipService.SetToolTip(button, path is null
            ? "The file is not present in the current working tree."
            : _fileActions.RevealDescription);
    }

    private async Task ExecuteFileActionAsync(
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
                        await ShowErrorAsync(errorTitle, result.Reason ?? "The file operation failed.");
                    break;
            }
        }
        catch (OperationCanceledException) when (IsShuttingDown || !_fileActions.IsCurrent(request))
        {
        }
        catch (Exception exception)
        {
            ReportUiFailure(exception, "File action");
            if (!IsShuttingDown && _fileActions.IsCurrent(request))
                await ShowErrorAsync(errorTitle, FileActionsViewModel.UserFacingFileError(exception));
        }
    }

    private void CommitOpenOriginal_Click(object sender, RoutedEventArgs args) =>
        RunPageUiOperation(() => CommitOpenOriginal_ClickCoreAsync(sender, args), "CommitOpenOriginal_Click");

    private async Task CommitOpenOriginal_ClickCoreAsync(object sender, RoutedEventArgs args) =>
        await ExecuteFileActionAsync(CaptureCommitRequest(), FileActionKind.Original, "Could not open original version");

    private void CommitOpenChanged_Click(object sender, RoutedEventArgs args) =>
        RunPageUiOperation(() => CommitOpenChanged_ClickCoreAsync(sender, args), "CommitOpenChanged_Click");

    private async Task CommitOpenChanged_ClickCoreAsync(object sender, RoutedEventArgs args) =>
        await ExecuteFileActionAsync(CaptureCommitRequest(), FileActionKind.Changed, "Could not open changed version");

    private void CommitExternalDiff_Click(object sender, RoutedEventArgs args) =>
        RunPageUiOperation(() => CommitExternalDiff_ClickCoreAsync(sender, args), "CommitExternalDiff_Click");

    private async Task CommitExternalDiff_ClickCoreAsync(object sender, RoutedEventArgs args) =>
        await ExecuteFileActionAsync(CaptureCommitRequest(), FileActionKind.ExternalDiff, "Could not open external diff tool");

    private void CommitReveal_Click(object sender, RoutedEventArgs args) =>
        RunPageUiOperation(() => CommitReveal_ClickCoreAsync(sender, args), "CommitReveal_Click");

    private async Task CommitReveal_ClickCoreAsync(object sender, RoutedEventArgs args) =>
        await ExecuteFileActionAsync(CaptureCommitRequest(), FileActionKind.Reveal, "Could not reveal file");

    private void WorkingTreeOpenOriginal_Click(object sender, RoutedEventArgs args) =>
        RunPageUiOperation(() => WorkingTreeOpenOriginal_ClickCoreAsync(sender, args), "WorkingTreeOpenOriginal_Click");

    private async Task WorkingTreeOpenOriginal_ClickCoreAsync(object sender, RoutedEventArgs args) =>
        await ExecuteFileActionAsync(CaptureWorkingTreeRequest(), FileActionKind.Original, "Could not open original version");

    private void WorkingTreeOpenChanged_Click(object sender, RoutedEventArgs args) =>
        RunPageUiOperation(() => WorkingTreeOpenChanged_ClickCoreAsync(sender, args), "WorkingTreeOpenChanged_Click");

    private async Task WorkingTreeOpenChanged_ClickCoreAsync(object sender, RoutedEventArgs args) =>
        await ExecuteFileActionAsync(CaptureWorkingTreeRequest(), FileActionKind.Changed, "Could not open changed version");

    private void WorkingTreeExternalDiff_Click(object sender, RoutedEventArgs args) =>
        RunPageUiOperation(() => WorkingTreeExternalDiff_ClickCoreAsync(sender, args), "WorkingTreeExternalDiff_Click");

    private async Task WorkingTreeExternalDiff_ClickCoreAsync(object sender, RoutedEventArgs args) =>
        await ExecuteFileActionAsync(CaptureWorkingTreeRequest(), FileActionKind.ExternalDiff, "Could not open external diff tool");

    private void WorkingTreeReveal_Click(object sender, RoutedEventArgs args) =>
        RunPageUiOperation(() => WorkingTreeReveal_ClickCoreAsync(sender, args), "WorkingTreeReveal_Click");

    private async Task WorkingTreeReveal_ClickCoreAsync(object sender, RoutedEventArgs args) =>
        await ExecuteFileActionAsync(CaptureWorkingTreeRequest(), FileActionKind.Reveal, "Could not reveal file");

    private async Task ShowDiffToolNotConfiguredAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Diff tool is not configured",
            Content = "Configure a Git diff tool in Settings → Git Tools.",
            PrimaryButtonText = "Open Git Tools Settings",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await ShowPageDialogAsync(dialog) == ContentDialogResult.Primary)
            OpenSettingsWindow(SettingsSection.GitTools);
    }

    private void ChangedFilesTree_DoubleTapped(object sender, DoubleTappedRoutedEventArgs args) =>
        RunPageUiOperation(() => ChangedFilesTree_DoubleTappedCoreAsync(sender, args), "ChangedFilesTree_DoubleTapped");

    private async Task ChangedFilesTree_DoubleTappedCoreAsync(object sender, DoubleTappedRoutedEventArgs args)
    {
        var source = args.OriginalSource as FrameworkElement;
        var node = ResolveChangedFileNode(source?.DataContext);
        if (node?.Entry is null) return;
        var request = CaptureCommitRequest(node.Entry.File.Path);
        if (request is null) return;
        _viewModel.CommitDetails.SelectedFile = node.Entry.File;
        args.Handled = true;
        await ExecuteFileActionAsync(request, FileActionKind.Preferred, "Could not open file");
    }

    private void WorkingTreeChanges_DoubleTapped(object sender, DoubleTappedRoutedEventArgs args) =>
        RunPageUiOperation(() => WorkingTreeChanges_DoubleTappedCoreAsync(sender, args), "WorkingTreeChanges_DoubleTapped");

    private async Task WorkingTreeChanges_DoubleTappedCoreAsync(object sender, DoubleTappedRoutedEventArgs args)
    {
        if (sender is not ListView list
            || args.OriginalSource is not FrameworkElement element
            || element.DataContext is not WorkingTreeChange change)
            return;
        var kind = ReferenceEquals(list, StagedChangesList)
            ? WorkingTreeDiffKind.Staged : WorkingTreeDiffKind.Unstaged;
        var request = CaptureWorkingTreeRequest(change, kind);
        if (request is null) return;
        SelectWorkingTreeChange(change, kind);
        args.Handled = true;
        await ExecuteFileActionAsync(request, FileActionKind.Preferred, "Could not open file");
    }

    private void ChangedFilesTree_RightTapped(object sender, RightTappedRoutedEventArgs args) =>
        RunPageUiOperation(() => ChangedFilesTree_RightTappedCoreAsync(sender, args), "ChangedFilesTree_RightTapped");

    private async Task ChangedFilesTree_RightTappedCoreAsync(object sender, RightTappedRoutedEventArgs args)
    {
        var source = args.OriginalSource as FrameworkElement;
        var node = ResolveChangedFileNode(source?.DataContext);
        if (source is null || node?.Entry is null) return;
        var request = CaptureCommitRequest(node.Entry.File.Path);
        if (request is null) return;
        _viewModel.CommitDetails.SelectedFile = node.Entry.File;
        args.Handled = true;
        await ShowFileMenuAsync(source, request);
    }

    private void WorkingTreeChanges_RightTapped(object sender, RightTappedRoutedEventArgs args) =>
        RunPageUiOperation(() => WorkingTreeChanges_RightTappedCoreAsync(sender, args), "WorkingTreeChanges_RightTapped");

    private async Task WorkingTreeChanges_RightTappedCoreAsync(object sender, RightTappedRoutedEventArgs args)
    {
        var source = args.OriginalSource as FrameworkElement;
        if (source is null || sender is not ListView list
            || source.DataContext is not WorkingTreeChange change)
            return;
        var kind = ReferenceEquals(list, StagedChangesList)
            ? WorkingTreeDiffKind.Staged : WorkingTreeDiffKind.Unstaged;
        var request = CaptureWorkingTreeRequest(change, kind);
        if (request is null) return;
        SelectWorkingTreeContextRow(list, change, kind);
        args.Handled = true;
        await ShowFileMenuAsync(source, request);
    }

    private async Task ShowFileMenuAsync(FrameworkElement source, FileActionRequest request)
    {
        try
        {
            var prepared = await _fileActions.PrepareAsync(request);
            if (IsShuttingDown || prepared is null || !_fileActions.IsCurrent(request) || source.XamlRoot is null
                || !IsMenuSelectionCurrent(request))
                return;

            if (request is CommitFileActionRequest commit)
                _fileActions.PublishCommitPreview(commit, prepared.Pair);
            else if (request is WorkingTreeFileActionRequest change)
                _fileActions.PublishWorkingTreePreview(change, prepared.Pair);

            var flyout = request is WorkingTreeFileActionRequest working
                ? BuildWorkingTreeFileMenu(working, prepared)
                : BuildFileMenu(request, prepared);
            flyout.ShowAt(source);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (_fileActions.IsCurrent(request) && !IsShuttingDown)
                await ShowErrorAsync("Could not read file versions",
                    FileActionsViewModel.UserFacingFileError(exception));
        }
    }

    private bool IsMenuSelectionCurrent(FileActionRequest request)
    {
        if (!_fileActions.IsCurrent(request)) return false;
        return request switch
        {
            CommitFileActionRequest commit =>
                CaptureCommitRequest() is { } current
                && Equals(current, commit),
            WorkingTreeFileActionRequest change =>
                CaptureWorkingTreeRequest() is { } current
                && ReferenceEquals(current.Repository, change.Repository)
                && current.Kind == change.Kind
                && SameWorkingTreeChange(current.Change, change.Change),
            _ => false
        };
    }

    private void SelectWorkingTreeContextRow(ListView list, WorkingTreeChange change, WorkingTreeDiffKind kind)
    {
        _workingTreeSelectionSync = true;
        try
        {
            list.SelectedItems.Clear();
            list.SelectedItem = change;
        }
        finally
        {
            _workingTreeSelectionSync = false;
        }

        _viewModel.WorkingTree.SetSelection(kind, [change]);
        SelectWorkingTreeChange(change, kind);
    }

    private MenuFlyout BuildWorkingTreeFileMenu(
        WorkingTreeFileActionRequest request, PreparedFileActions prepared)
    {
        var flyout = BuildFileMenu(request, prepared, separateReveal: false);
        flyout.Items.Insert(0, new MenuFlyoutSeparator());
        if (request.Kind == WorkingTreeDiffKind.Unstaged)
        {
            flyout.Items.Insert(0, CreateWorkingTreeCommandMenuItem(
                "Discard file", "\uE74D", _viewModel.WorkingTree.RequestDiscardSelectedCommand, request, destructive: true));
            flyout.Items.Insert(0, CreateWorkingTreeCommandMenuItem(
                "Stage file", "\uE710", _viewModel.WorkingTree.StageCommand, request));
        }
        else
        {
            flyout.Items.Insert(0, CreateWorkingTreeCommandMenuItem(
                "Unstage file", "\uE738", _viewModel.WorkingTree.UnstageCommand, request));
        }
        return flyout;
    }

    private MenuFlyoutItem CreateWorkingTreeCommandMenuItem(
        string text, string glyph, System.Windows.Input.ICommand command,
        WorkingTreeFileActionRequest request, bool destructive = false)
    {
        var item = new MenuFlyoutItem
        {
            Text = text,
            Icon = new FontIcon { Glyph = glyph },
            IsEnabled = command.CanExecute(null)
        };
        // Keep Stage/Unstage/Discard in WorkingTreeViewModel; stale menus cannot mutate B.
        item.Click += (_, _) =>
        {
            if (IsMenuSelectionCurrent(request) && command.CanExecute(null))
                command.Execute(null);
        };
        if (destructive) item.Foreground = CreateDestructiveBrush();
        return item;
    }

    private MenuFlyout BuildFileMenu(
        FileActionRequest request, PreparedFileActions prepared, bool separateReveal = true)
    {
        var pair = prepared.Pair;
        var flyout = new MenuFlyout();
        flyout.Items.Add(CreateMenuItem("Open changed", pair.Changed,
            () => ExecuteFileActionAsync(request, FileActionKind.Changed, "Could not open changed version", prepared)));
        flyout.Items.Add(CreateMenuItem("Open original", pair.Original,
            () => ExecuteFileActionAsync(request, FileActionKind.Original, "Could not open original version", prepared)));
        var externalItem = new MenuFlyoutItem
        {
            Text = "Open in Diff Tool",
            IsEnabled = prepared.CanExternalDiff
        };
        ToolTipService.SetToolTip(externalItem, prepared.CanExternalDiff
            ? externalItem.Text : FileActionsViewModel.ExternalDiffUnavailableReason(pair));
        externalItem.Click += (_, _) => RunPageUiOperation(
            () => ExecuteFileActionAsync(
                request, FileActionKind.ExternalDiff, "Could not open external diff tool", prepared),
            "External file diff");
        flyout.Items.Add(externalItem);
        if (separateReveal) flyout.Items.Add(new MenuFlyoutSeparator());
        var revealItem = new MenuFlyoutItem
        {
            Text = _fileActions.RevealDescription,
            IsEnabled = prepared.CanReveal
        };
        ToolTipService.SetToolTip(revealItem, prepared.CanReveal
            ? revealItem.Text : "The file is not present in the current working tree.");
        revealItem.Click += (_, _) => RunPageUiOperation(
            () => ExecuteFileActionAsync(
                request, FileActionKind.Reveal, "Could not reveal file", prepared),
            "Reveal file");
        flyout.Items.Add(revealItem);
        return flyout;
    }

    private MenuFlyoutItem CreateMenuItem(string text, DiffFileVersion version, Func<Task> action)
    {
        var item = new MenuFlyoutItem { Text = text, IsEnabled = version.CanOpen };
        ToolTipService.SetToolTip(item, version.CanOpen ? text
            : version.UnavailableReason ?? "This version is unavailable.");
        item.Click += (_, _) => RunPageUiOperation(action, "File menu action");
        return item;
    }

    private static SolidColorBrush CreateDestructiveBrush() =>
        new(Microsoft.UI.Colors.Red);

    private static string UserFacingFileError(Exception exception) =>
        FileActionsViewModel.UserFacingFileError(exception);
}
