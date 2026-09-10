using System.ComponentModel;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private IRepositoryFileVersionService? _fileVersionService;
    private IDesktopShellService? _desktopShellService;
    private IRepositoryPathService? _repositoryPathService;
    private DiffFileVersionPair? _commitFileVersions;
    private DiffFileVersionPair? _workingTreeFileVersions;
    private string? _commitRevealPath;
    private string? _workingTreeRevealPath;
    private long _commitFileActionGeneration;
    private long _workingTreeFileActionGeneration;
    private Button? _commitOpenOriginalButton;
    private Button? _commitOpenChangedButton;
    private Button? _commitRevealButton;
    private Button? _workingTreeOpenOriginalButton;
    private Button? _workingTreeOpenChangedButton;
    private Button? _workingTreeRevealButton;

    public MainPage(
        OpenRepositoryViewModel viewModel,
        IReferenceHistoryService referenceHistoryService,
        IReferenceService referenceService,
        IWorkingTreeDiffService workingTreeDiffService,
        IRepositoryFileVersionService fileVersionService,
        IDesktopShellService desktopShellService,
        IRepositoryPathService repositoryPathService)
        : this(viewModel, referenceHistoryService, referenceService, workingTreeDiffService)
    {
        _fileVersionService = fileVersionService;
        _desktopShellService = desktopShellService;
        _repositoryPathService = repositoryPathService;
        InitializeFileOpening();
    }

    private void InitializeFileOpening()
    {
        InstallCommitFileActions();
        InstallWorkingTreeFileActions();

        ChangedFilesTree.RightTapped += ChangedFilesTree_RightTapped;
        ChangedFilesTree.DoubleTapped += ChangedFilesTree_DoubleTapped;
        UnstagedChangesList.RightTapped += WorkingTreeChanges_RightTapped;
        StagedChangesList.RightTapped += WorkingTreeChanges_RightTapped;
        UnstagedChangesList.DoubleTapped += WorkingTreeChanges_DoubleTapped;
        StagedChangesList.DoubleTapped += WorkingTreeChanges_DoubleTapped;
        _viewModel.PropertyChanged += FileOpeningViewModel_PropertyChanged;

        _ = RefreshCommitFileActionStateAsync();
        _ = RefreshWorkingTreeFileActionStateAsync();
    }

    private void InstallCommitFileActions()
    {
        if (CompactDiffList.Parent is not Grid diffBody || diffBody.Parent is not Grid diffGrid)
            return;
        var header = diffGrid.Children.OfType<Border>().FirstOrDefault(element => Grid.GetRow(element) == 0);
        if (header?.Child is not UIElement existingContent) return;

        header.Child = null;
        var layout = new Grid { ColumnSpacing = 6 };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(existingContent, 0);
        layout.Children.Add(existingContent);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        _commitOpenOriginalButton = CreateActionButton("Original", "Open original version", CommitOpenOriginal_Click);
        _commitOpenChangedButton = CreateActionButton("Changed", "Open changed version", CommitOpenChanged_Click);
        _commitRevealButton = CreateActionButton("Reveal", _desktopShellService!.RevealDescription, CommitReveal_Click);
        actions.Children.Add(_commitOpenOriginalButton);
        actions.Children.Add(_commitOpenChangedButton);
        actions.Children.Add(_commitRevealButton);
        Grid.SetColumn(actions, 1);
        layout.Children.Add(actions);
        header.Child = layout;
    }

    private void InstallWorkingTreeFileActions()
    {
        if (WorkingTreeDiffHeader.Parent is not Grid headerGrid) return;
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        _workingTreeOpenOriginalButton = CreateActionButton("Original", "Open original side of this diff", WorkingTreeOpenOriginal_Click);
        _workingTreeOpenChangedButton = CreateActionButton("Changed", "Open changed side of this diff", WorkingTreeOpenChanged_Click);
        _workingTreeRevealButton = CreateActionButton("Reveal", _desktopShellService!.RevealDescription, WorkingTreeReveal_Click);
        actions.Children.Add(_workingTreeOpenOriginalButton);
        actions.Children.Add(_workingTreeOpenChangedButton);
        actions.Children.Add(_workingTreeRevealButton);
        Grid.SetColumn(actions, 2);
        headerGrid.Children.Add(actions);
    }

    private static Button CreateActionButton(string text, string tooltip, RoutedEventHandler click)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(7, 2, 7, 2),
            MinHeight = 26,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(button, tooltip);
        button.Click += click;
        return button;
    }

    private void FileOpeningViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(OpenRepositoryViewModel.SelectedFile)
            or nameof(OpenRepositoryViewModel.SelectedCommit)
            or nameof(OpenRepositoryViewModel.Repository))
            _ = RefreshCommitFileActionStateAsync();

        if (args.PropertyName is nameof(OpenRepositoryViewModel.ActiveWorkingTreeChange)
            or nameof(OpenRepositoryViewModel.ActiveWorkingTreeDiffKind)
            or nameof(OpenRepositoryViewModel.Repository))
            _ = RefreshWorkingTreeFileActionStateAsync();
    }

    private async Task RefreshCommitFileActionStateAsync()
    {
        var generation = Interlocked.Increment(ref _commitFileActionGeneration);
        _commitFileVersions = null;
        _commitRevealPath = null;
        UpdateCommitButtons();

        var repository = _viewModel.Repository;
        var commit = _viewModel.SelectedCommit;
        var file = _viewModel.SelectedFile;
        if (repository is null || commit is null || file is null || _fileVersionService is null || _repositoryPathService is null)
            return;

        try
        {
            var pair = await _fileVersionService.ResolveCommitAsync(repository, commit.Commit.Hash, file.Path);
            if (generation != Volatile.Read(ref _commitFileActionGeneration)
                || !ReferenceEquals(repository, _viewModel.Repository)
                || !ReferenceEquals(commit, _viewModel.SelectedCommit)
                || !ReferenceEquals(file, _viewModel.SelectedFile))
                return;

            _commitFileVersions = pair;
            try
            {
                _commitRevealPath = _repositoryPathService.ResolveExistingWorkingTreeFile(repository, pair.RevealPath, allowFinalLink: true);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                _commitRevealPath = null;
            }
            UpdateCommitButtons();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (generation == Volatile.Read(ref _commitFileActionGeneration)) UpdateCommitButtons();
        }
    }

    private async Task RefreshWorkingTreeFileActionStateAsync()
    {
        var generation = Interlocked.Increment(ref _workingTreeFileActionGeneration);
        _workingTreeFileVersions = null;
        _workingTreeRevealPath = null;
        UpdateWorkingTreeButtons();

        var repository = _viewModel.Repository;
        var change = _viewModel.ActiveWorkingTreeChange;
        var kind = _viewModel.ActiveWorkingTreeDiffKind;
        if (repository is null || change is null || kind is null || _fileVersionService is null || _repositoryPathService is null)
            return;

        try
        {
            var pair = await _fileVersionService.ResolveWorkingTreeAsync(repository, change, kind.Value);
            if (generation != Volatile.Read(ref _workingTreeFileActionGeneration)
                || !ReferenceEquals(repository, _viewModel.Repository)
                || _viewModel.ActiveWorkingTreeDiffKind != kind
                || _viewModel.ActiveWorkingTreeChange is not { } current
                || !SameWorkingTreeChange(current, change))
                return;

            _workingTreeFileVersions = pair;
            try
            {
                _workingTreeRevealPath = _repositoryPathService.ResolveExistingWorkingTreeFile(repository, pair.RevealPath, allowFinalLink: true);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                _workingTreeRevealPath = null;
            }
            UpdateWorkingTreeButtons();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (generation == Volatile.Read(ref _workingTreeFileActionGeneration)) UpdateWorkingTreeButtons();
        }
    }

    private void UpdateCommitButtons()
    {
        SetVersionButton(_commitOpenOriginalButton, _commitFileVersions?.Original, "No original version is available.");
        SetVersionButton(_commitOpenChangedButton, _commitFileVersions?.Changed, "No changed version is available.");
        SetRevealButton(_commitRevealButton, _commitRevealPath);
    }

    private void UpdateWorkingTreeButtons()
    {
        SetVersionButton(_workingTreeOpenOriginalButton, _workingTreeFileVersions?.Original, "No original version is available.");
        SetVersionButton(_workingTreeOpenChangedButton, _workingTreeFileVersions?.Changed, "No changed version is available.");
        SetRevealButton(_workingTreeRevealButton, _workingTreeRevealPath);
    }

    private static void SetVersionButton(Button? button, DiffFileVersion? version, string fallbackReason)
    {
        if (button is null) return;
        button.IsEnabled = version?.CanOpen == true;
        ToolTipService.SetToolTip(button, version?.CanOpen == true
            ? button.Content?.ToString()
            : version?.UnavailableReason ?? fallbackReason);
    }

    private void SetRevealButton(Button? button, string? fullPath)
    {
        if (button is null || _desktopShellService is null) return;
        button.IsEnabled = fullPath is not null;
        ToolTipService.SetToolTip(button, fullPath is null
            ? "The file is not present in the current working tree."
            : _desktopShellService.RevealDescription);
    }

    private async void CommitOpenOriginal_Click(object sender, RoutedEventArgs args) =>
        await OpenSelectedCommitVersionAsync(DiffFileSide.Original);

    private async void CommitOpenChanged_Click(object sender, RoutedEventArgs args) =>
        await OpenSelectedCommitVersionAsync(DiffFileSide.Changed);

    private async void CommitReveal_Click(object sender, RoutedEventArgs args) =>
        await RevealSelectedCommitFileAsync();

    private async void WorkingTreeOpenOriginal_Click(object sender, RoutedEventArgs args) =>
        await OpenSelectedWorkingTreeVersionAsync(DiffFileSide.Original);

    private async void WorkingTreeOpenChanged_Click(object sender, RoutedEventArgs args) =>
        await OpenSelectedWorkingTreeVersionAsync(DiffFileSide.Changed);

    private async void WorkingTreeReveal_Click(object sender, RoutedEventArgs args) =>
        await RevealSelectedWorkingTreeFileAsync();

    private async Task OpenSelectedCommitVersionAsync(DiffFileSide side)
    {
        var repository = _viewModel.Repository;
        var commit = _viewModel.SelectedCommit;
        var file = _viewModel.SelectedFile;
        if (repository is null || commit is null || file is null || _fileVersionService is null || _desktopShellService is null)
            return;

        try
        {
            var pair = await _fileVersionService.ResolveCommitAsync(repository, commit.Commit.Hash, file.Path);
            var version = side == DiffFileSide.Original ? pair.Original : pair.Changed;
            await OpenResolvedVersionAsync(repository, version, side);
            _commitFileVersions = pair;
            UpdateCommitButtons();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(side == DiffFileSide.Original ? "Could not open original version" : "Could not open changed version", UserFacingFileError(exception));
        }
    }

    private async Task OpenSelectedWorkingTreeVersionAsync(DiffFileSide side)
    {
        var repository = _viewModel.Repository;
        var change = _viewModel.ActiveWorkingTreeChange;
        var kind = _viewModel.ActiveWorkingTreeDiffKind;
        if (repository is null || change is null || kind is null || _fileVersionService is null || _desktopShellService is null)
            return;

        try
        {
            var pair = await _fileVersionService.ResolveWorkingTreeAsync(repository, change, kind.Value);
            var version = side == DiffFileSide.Original ? pair.Original : pair.Changed;
            await OpenResolvedVersionAsync(repository, version, side);
            _workingTreeFileVersions = pair;
            UpdateWorkingTreeButtons();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(side == DiffFileSide.Original ? "Could not open original version" : "Could not open changed version", UserFacingFileError(exception));
        }
    }

    private async Task OpenResolvedVersionAsync(Repository repository, DiffFileVersion version, DiffFileSide side)
    {
        if (_fileVersionService is null || _desktopShellService is null || _repositoryPathService is null) return;
        if (!version.CanOpen)
            throw new InvalidOperationException(version.UnavailableReason ?? "This file version is unavailable.");

        string path;
        if (version.Location == DiffFileVersionLocation.WorkingCopy)
        {
            path = _repositoryPathService.ResolveExistingWorkingTreeFile(repository, version.GitPath);
        }
        else
        {
            path = (await _fileVersionService.MaterializeAsync(repository, version, side)).Path;
        }
        await _desktopShellService.OpenFileAsync(path);
    }

    private async Task RevealSelectedCommitFileAsync()
    {
        var repository = _viewModel.Repository;
        var commit = _viewModel.SelectedCommit;
        var file = _viewModel.SelectedFile;
        if (repository is null || commit is null || file is null || _fileVersionService is null || _repositoryPathService is null || _desktopShellService is null)
            return;
        try
        {
            var pair = await _fileVersionService.ResolveCommitAsync(repository, commit.Commit.Hash, file.Path);
            var path = _repositoryPathService.ResolveExistingWorkingTreeFile(repository, pair.RevealPath, allowFinalLink: true);
            await _desktopShellService.RevealFileAsync(path);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ShowErrorAsync("Could not reveal file", UserFacingFileError(exception));
        }
    }

    private async Task RevealSelectedWorkingTreeFileAsync()
    {
        var repository = _viewModel.Repository;
        var change = _viewModel.ActiveWorkingTreeChange;
        var kind = _viewModel.ActiveWorkingTreeDiffKind;
        if (repository is null || change is null || kind is null || _fileVersionService is null || _repositoryPathService is null || _desktopShellService is null)
            return;
        try
        {
            var pair = await _fileVersionService.ResolveWorkingTreeAsync(repository, change, kind.Value);
            var path = _repositoryPathService.ResolveExistingWorkingTreeFile(repository, pair.RevealPath, allowFinalLink: true);
            await _desktopShellService.RevealFileAsync(path);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ShowErrorAsync("Could not reveal file", UserFacingFileError(exception));
        }
    }

    private async void ChangedFilesTree_DoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        var source = args.OriginalSource as FrameworkElement;
        var node = ResolveChangedFileNode(source?.DataContext);
        if (node?.Entry is null || _viewModel.Repository is null || _viewModel.SelectedCommit is null || _fileVersionService is null)
            return;

        _viewModel.SelectedFile = node.Entry.File;
        try
        {
            var pair = await _fileVersionService.ResolveCommitAsync(_viewModel.Repository, _viewModel.SelectedCommit.Commit.Hash, node.Entry.File.Path);
            var side = pair.Changed.CanOpen ? DiffFileSide.Changed : DiffFileSide.Original;
            await OpenResolvedVersionAsync(_viewModel.Repository, side == DiffFileSide.Changed ? pair.Changed : pair.Original, side);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ShowErrorAsync("Could not open file", UserFacingFileError(exception));
        }
        args.Handled = true;
    }

    private async void WorkingTreeChanges_DoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        if (sender is not ListView list || list.SelectedItem is not WorkingTreeChange change || _viewModel.Repository is null || _fileVersionService is null)
            return;
        var kind = ReferenceEquals(list, StagedChangesList) ? WorkingTreeDiffKind.Staged : WorkingTreeDiffKind.Unstaged;
        SelectWorkingTreeChange(change, kind);
        try
        {
            var pair = await _fileVersionService.ResolveWorkingTreeAsync(_viewModel.Repository, change, kind);
            var side = pair.Changed.CanOpen ? DiffFileSide.Changed : DiffFileSide.Original;
            await OpenResolvedVersionAsync(_viewModel.Repository, side == DiffFileSide.Changed ? pair.Changed : pair.Original, side);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ShowErrorAsync("Could not open file", UserFacingFileError(exception));
        }
        args.Handled = true;
    }

    private async void ChangedFilesTree_RightTapped(object sender, RightTappedRoutedEventArgs args)
    {
        var source = args.OriginalSource as FrameworkElement;
        var node = ResolveChangedFileNode(source?.DataContext);
        if (source is null || node?.Entry is null || _viewModel.Repository is null || _viewModel.SelectedCommit is null || _fileVersionService is null)
            return;

        _viewModel.SelectedFile = node.Entry.File;
        try
        {
            var pair = await _fileVersionService.ResolveCommitAsync(_viewModel.Repository, _viewModel.SelectedCommit.Commit.Hash, node.Entry.File.Path);
            _commitFileVersions = pair;
            _commitRevealPath = TryResolveReveal(_viewModel.Repository, pair.RevealPath);
            UpdateCommitButtons();
            var flyout = BuildFileMenu(
                pair,
                _commitRevealPath is not null,
                () => OpenSelectedCommitVersionAsync(DiffFileSide.Changed),
                () => OpenSelectedCommitVersionAsync(DiffFileSide.Original),
                RevealSelectedCommitFileAsync);
            flyout.ShowAt(source);
            args.Handled = true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ShowErrorAsync("Could not read file versions", UserFacingFileError(exception));
        }
    }

    private async void WorkingTreeChanges_RightTapped(object sender, RightTappedRoutedEventArgs args)
    {
        var source = args.OriginalSource as FrameworkElement;
        if (source is null || sender is not ListView list || source.DataContext is not WorkingTreeChange change || _viewModel.Repository is null || _fileVersionService is null)
            return;
        var kind = ReferenceEquals(list, StagedChangesList) ? WorkingTreeDiffKind.Staged : WorkingTreeDiffKind.Unstaged;
        SelectWorkingTreeChange(change, kind);
        try
        {
            var pair = await _fileVersionService.ResolveWorkingTreeAsync(_viewModel.Repository, change, kind);
            _workingTreeFileVersions = pair;
            _workingTreeRevealPath = TryResolveReveal(_viewModel.Repository, pair.RevealPath);
            UpdateWorkingTreeButtons();
            var flyout = BuildFileMenu(
                pair,
                _workingTreeRevealPath is not null,
                () => OpenSelectedWorkingTreeVersionAsync(DiffFileSide.Changed),
                () => OpenSelectedWorkingTreeVersionAsync(DiffFileSide.Original),
                RevealSelectedWorkingTreeFileAsync);
            flyout.ShowAt(source);
            args.Handled = true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ShowErrorAsync("Could not read file versions", UserFacingFileError(exception));
        }
    }

    private MenuFlyout BuildFileMenu(
        DiffFileVersionPair pair,
        bool canReveal,
        Func<Task> openChanged,
        Func<Task> openOriginal,
        Func<Task> reveal)
    {
        var flyout = new MenuFlyout();
        flyout.Items.Add(CreateMenuItem("Open changed", pair.Changed, openChanged));
        flyout.Items.Add(CreateMenuItem("Open original", pair.Original, openOriginal));
        flyout.Items.Add(new MenuFlyoutSeparator());
        var revealItem = new MenuFlyoutItem { Text = _desktopShellService?.RevealDescription ?? "Reveal", IsEnabled = canReveal };
        ToolTipService.SetToolTip(revealItem, canReveal ? revealItem.Text : "The file is not present in the current working tree.");
        revealItem.Click += async (_, _) => await reveal();
        flyout.Items.Add(revealItem);
        return flyout;
    }

    private static MenuFlyoutItem CreateMenuItem(string text, DiffFileVersion version, Func<Task> action)
    {
        var item = new MenuFlyoutItem { Text = text, IsEnabled = version.CanOpen };
        ToolTipService.SetToolTip(item, version.CanOpen ? text : version.UnavailableReason ?? "This version is unavailable.");
        item.Click += async (_, _) => await action();
        return item;
    }

    private string? TryResolveReveal(Repository repository, string gitPath)
    {
        try
        {
            return _repositoryPathService?.ResolveExistingWorkingTreeFile(repository, gitPath, allowFinalLink: true);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string UserFacingFileError(Exception exception) => exception switch
    {
        FileNotFoundException => "The file is no longer present in the current working tree.",
        UnauthorizedAccessException => "CSharpGit does not have permission to access this file.",
        NotSupportedException => exception.Message,
        ArgumentException => "The repository returned an invalid file path.",
        InvalidOperationException => exception.Message,
        IOException => "The file could not be prepared or opened.",
        _ => "The file operation failed."
    };
}
