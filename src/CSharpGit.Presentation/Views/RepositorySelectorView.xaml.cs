using System.Collections.ObjectModel;
using System.ComponentModel;
using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation.Views;

internal sealed record RepositorySelectorEntry(
    string Path,
    string Branch,
    bool IsCurrent,
    string DisplayPath,
    bool IsGroupHeader = false)
{
    public Visibility HeaderVisibility => IsGroupHeader ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RepositoryVisibility => IsGroupHeader ? Visibility.Collapsed : Visibility.Visible;
    public string CurrentGlyph => IsCurrent ? "\uE73E" : string.Empty;
    public string ToolTip => IsGroupHeader ? Path : $"{Path}\nBranch: {Branch}";
    public string AccessibleName => IsGroupHeader
        ? $"Repository group {Path}"
        : IsCurrent
            ? $"Current repository {Path}, branch {Branch}"
            : $"{Path}, branch {Branch}";
}


/// <summary>
/// Window-local repository selector. The switching workflow remains outside the view.
/// </summary>
public sealed partial class RepositorySelectorView : UserControl
{
    private OpenRepositoryViewModel _viewModel = null!;
    private IAppSettingsService _recentRepositorySettings = null!;
    private IDesktopShellService _desktopShellService = null!;
    private RepositorySelectorActions _actions = null!;
    private bool _isShutdown;

    public RepositorySelectorView() => InitializeComponent();

    internal void Configure(OpenRepositoryViewModel repository, IAppSettingsService settings,
        IDesktopShellService shell, RepositorySelectorActions actions)
    {
        if (_actions is not null) return;
        if (_isShutdown) return;
        _viewModel = repository ?? throw new ArgumentNullException(nameof(repository));
        _recentRepositorySettings = settings ?? throw new ArgumentNullException(nameof(settings));
        _desktopShellService = shell ?? throw new ArgumentNullException(nameof(shell));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        InitializeRepositorySwitching();
        _viewModel.PropertyChanged += Repository_PropertyChanged;
    }

    private void Repository_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isShutdown) return;
        if (e.PropertyName is nameof(OpenRepositoryViewModel.Repository)
            or nameof(OpenRepositoryViewModel.CurrentBranchName)
            or nameof(OpenRepositoryViewModel.IsDetachedHead))
            Refresh();
    }

    internal void Shutdown()
    {
        if (_isShutdown) return;
        _isShutdown = true;
        if (_actions is not null)
            _viewModel.PropertyChanged -= Repository_PropertyChanged;
    }

    private void RunSelectorOperation(Func<Task> action, string description, bool showError = false)
    {
        if (_isShutdown) return;
        _ = ExecuteAsync();

        async Task ExecuteAsync()
        {
            try { await action(); }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"Repository selector {description}: {exception}");
                if (showError && !_isShutdown)
                    await _actions.ShowError(description, exception.Message);
            }
        }
    }

    private const int QuickRepositoryLimit = 8;
    private readonly ObservableCollection<RepositorySelectorEntry> _repositorySelectorItems = [];

    private void InitializeRepositorySwitching()
    {
        RepositorySelectorList.ItemsSource = _repositorySelectorItems;
        RepositorySelectorOpenFolderText.Text = _desktopShellService.OpenFolderDescription;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            RepositorySelectorOpenFolderButton, _desktopShellService.OpenFolderDescription);
        Refresh();
    }

    internal void Refresh()
    {
        if (_isShutdown || _actions is null) return;
        var repository = _viewModel.Repository;
        if (repository is null)
        {
            RepositorySelectorName.Text = string.Empty;
            ToolTipService.SetToolTip(RepositorySelectorButton, null);
            return;
        }

        var path = repository.WorkingDirectory;
        RepositorySelectorName.Text = RepositoryDisplayName(path);
        ToolTipService.SetToolTip(RepositorySelectorButton, path);
    }

    private void RepositorySelectorFlyout_Opening(object sender, object args)
    {
        _repositorySelectorItems.Clear();
        List<RepositorySelectorEntry> entries = [];

        var currentPath = _viewModel.Repository?.WorkingDirectory;
        if (currentPath is not null)
        {
            var currentBranch = _viewModel.IsDetachedHead
                ? "detached HEAD"
                : RepositoryBranchDisplay(_viewModel.CurrentBranchName);
            entries.Add(new RepositorySelectorEntry(
                currentPath,
                currentBranch,
                true,
                currentPath));
        }

        var snapshot = _recentRepositorySettings.RepositoryLauncherSnapshot;
        var quickRepositories = RecentRepositoriesProjectionBuilder.BuildQuickList(
            snapshot.Groups,
            snapshot.Repositories,
            currentPath,
            QuickRepositoryLimit);

        foreach (var repository in quickRepositories)
        {
            entries.Add(new RepositorySelectorEntry(
                repository.Path,
                RepositoryBranchDisplay(repository.LastBranchName),
                false,
                repository.Path));
        }

        foreach (var row in RepositorySelectorGrouping.Build(entries.Select(entry => entry.Path).ToArray()))
        {
            _repositorySelectorItems.Add(row.RepositoryIndex is { } index
                ? entries[index] with { DisplayPath = row.DisplayPath }
                : new RepositorySelectorEntry(row.Path, string.Empty, false, row.DisplayPath, true));
        }

        var canChangeRepository = _viewModel.CanChangeRepository;
        RepositorySelectorList.IsEnabled = canChangeRepository;
        RepositorySelectorOpenFolderButton.IsEnabled =
            currentPath is not null && Directory.Exists(currentPath);
        RepositorySelectorOpenTerminalButton.IsEnabled = currentPath is not null && Directory.Exists(currentPath);
        RepositorySelectorCopyPathButton.IsEnabled = currentPath is not null;
        RepositorySelectorOpenButton.IsEnabled = canChangeRepository;
        RepositorySelectorCloneButton.IsEnabled = canChangeRepository;
        RepositorySelectorCreateButton.IsEnabled = canChangeRepository;
        RepositorySelectorRepositoriesButton.IsEnabled = canChangeRepository;
        RepositorySelectorCloseButton.IsEnabled = canChangeRepository;
    }

    private void RepositorySelectorList_ItemClick(object sender, ItemClickEventArgs args) =>
        RunSelectorOperation(() => RepositorySelectorList_ItemClickCoreAsync(sender, args), "RepositorySelectorList_ItemClick");

    private async Task RepositorySelectorList_ItemClickCoreAsync(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is not RepositorySelectorEntry { IsGroupHeader: false } entry) return;

        RepositorySelectorFlyout.Hide();
        if (entry.IsCurrent) return;

        await _actions.SwitchRepository(entry.Path);
    }

    private void RepositorySelectorOpenFolder_Click(object sender, RoutedEventArgs args) =>
        RunSelectorOperation(() => RepositorySelectorOpenFolder_ClickCoreAsync(sender, args), "RepositorySelectorOpenFolder_Click");

    private async Task RepositorySelectorOpenFolder_ClickCoreAsync(object sender, RoutedEventArgs args)
    {
        var path = _viewModel.Repository?.WorkingDirectory;
        RepositorySelectorFlyout.Hide();
        if (path is not null && Directory.Exists(path))
            await _actions.OpenFolder(path);
    }

    private void RepositorySelectorOpenTerminal_Click(object sender, RoutedEventArgs args) =>
        RunSelectorOperation(OpenRepositoryTerminalAsync, "Open repository terminal", showError: true);

    private async Task OpenRepositoryTerminalAsync()
    {
        var path = _viewModel.Repository?.WorkingDirectory;
        RepositorySelectorFlyout.Hide();
        if (path is null || !Directory.Exists(path)) return;
        try
        {
            await _desktopShellService.OpenTerminalAsync(path);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await _actions.ShowError("Could not open terminal", exception.Message);
        }
    }

    private void RepositorySelectorCopyPath_Click(object sender, RoutedEventArgs args) =>
        RunSelectorOperation(() => RepositorySelectorCopyPath_ClickCoreAsync(sender, args), "RepositorySelectorCopyPath_Click");

    private async Task RepositorySelectorCopyPath_ClickCoreAsync(object sender, RoutedEventArgs args)
    {
        var path = _viewModel.Repository?.WorkingDirectory;
        RepositorySelectorFlyout.Hide();
        if (path is not null)
            await _actions.CopyPath(path);
    }

    private void RepositorySelectorOpenRepository_Click(object sender, RoutedEventArgs args) =>
        RunSelectorOperation(() => RepositorySelectorOpenRepository_ClickCoreAsync(sender, args), "RepositorySelectorOpenRepository_Click");

    private async Task RepositorySelectorOpenRepository_ClickCoreAsync(object sender, RoutedEventArgs args)
    {
        RepositorySelectorFlyout.Hide();
        await _actions.OpenRepository();
    }

    private void RepositorySelectorCloneRepository_Click(object sender, RoutedEventArgs args) =>
        RunSelectorOperation(() => RepositorySelectorCloneRepository_ClickCoreAsync(sender, args), "RepositorySelectorCloneRepository_Click");

    private async Task RepositorySelectorCloneRepository_ClickCoreAsync(object sender, RoutedEventArgs args)
    {
        RepositorySelectorFlyout.Hide();
        await _actions.CloneRepository();
    }

    private void RepositorySelectorCreateRepository_Click(object sender, RoutedEventArgs args) =>
        RunSelectorOperation(() => RepositorySelectorCreateRepository_ClickCoreAsync(sender, args), "RepositorySelectorCreateRepository_Click");

    private async Task RepositorySelectorCreateRepository_ClickCoreAsync(object sender, RoutedEventArgs args)
    {
        RepositorySelectorFlyout.Hide();
        await _actions.CreateRepository();
    }

    private void RepositorySelectorRepositories_Click(object sender, RoutedEventArgs args) =>
        RunSelectorOperation(() => RepositorySelectorRepositories_ClickCoreAsync(sender, args), "RepositorySelectorRepositories_Click");

    private async Task RepositorySelectorRepositories_ClickCoreAsync(object sender, RoutedEventArgs args)
    {
        RepositorySelectorFlyout.Hide();
        await _actions.CloseRepository();
    }

    private void RepositorySelectorCloseRepository_Click(object sender, RoutedEventArgs args) =>
        RunSelectorOperation(() => RepositorySelectorCloseRepository_ClickCoreAsync(sender, args), "RepositorySelectorCloseRepository_Click");

    private async Task RepositorySelectorCloseRepository_ClickCoreAsync(object sender, RoutedEventArgs args)
    {
        RepositorySelectorFlyout.Hide();
        await _actions.CloseRepository();
    }


    private static string RepositoryBranchDisplay(string? branchName) =>
        string.IsNullOrWhiteSpace(branchName) ? "—" : branchName;

    private static string RepositoryDisplayName(string path)
    {
        var displayName = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return string.IsNullOrWhiteSpace(displayName) ? path : displayName;
    }
}
