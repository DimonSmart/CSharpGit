using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace CSharpGit.Presentation;

internal enum SettingsSection
{
    General,
    Identity,
    GitTools,
    Diagnostics
}

public sealed partial class SettingsPage : Page
{
    private static readonly SettingsSection[] NavigationOrder =
    [
        SettingsSection.General,
        SettingsSection.Identity,
        SettingsSection.GitTools,
        SettingsSection.Diagnostics
    ];

    private readonly SettingsViewModel _viewModel;
    private readonly RepositoryIdentitySettingsViewModel _identityViewModel;
    private readonly GitToolsSettingsViewModel _gitToolsViewModel;
    private readonly IDesktopShellService _desktopShellService;
    private readonly IFolderPicker _folderPicker;
    private readonly string _logFilePath;
    private readonly Func<Repository?> _repositoryAccessor;
    private bool _selectionReady;
    private bool _settingsDetached;
    private bool _gitToolsLoaded;

    internal SettingsPage(
        SettingsViewModel viewModel,
        RepositoryIdentitySettingsViewModel identityViewModel,
        GitToolsSettingsViewModel gitToolsViewModel,
        IDesktopShellService desktopShellService,
        IFolderPicker folderPicker,
        string logFilePath,
        Func<Repository?> repositoryAccessor,
        SettingsSection initialSection)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _identityViewModel = identityViewModel ?? throw new ArgumentNullException(nameof(identityViewModel));
        _gitToolsViewModel = gitToolsViewModel ?? throw new ArgumentNullException(nameof(gitToolsViewModel));
        _desktopShellService = desktopShellService ?? throw new ArgumentNullException(nameof(desktopShellService));
        _folderPicker = folderPicker ?? throw new ArgumentNullException(nameof(folderPicker));
        _logFilePath = string.IsNullOrWhiteSpace(logFilePath)
            ? throw new ArgumentException("A log file path is required.", nameof(logFilePath))
            : Path.GetFullPath(logFilePath);
        _repositoryAccessor = repositoryAccessor ?? throw new ArgumentNullException(nameof(repositoryAccessor));

        InitializeComponent();
        LogFilePathText.Text = _logFilePath;
        DataContext = _viewModel;
        IdentitySettingsPanel.DataContext = _identityViewModel;
        GitToolsSettingsPanel.DataContext = _gitToolsViewModel;

        SettingsNavigation.SelectedIndex = IndexOfSection(initialSection);
        Loaded += async (_, _) =>
        {
            _selectionReady = true;
            LogLevelComboBox.IsEnabled = LoggingToggle.IsOn;
            await ActivateSectionAsync(SelectedSection(), force: true);
        };
    }

    internal void SelectSection(SettingsSection section)
    {
        SettingsNavigation.SelectedIndex = IndexOfSection(section);
        if (_selectionReady)
            _ = ActivateSectionAsync(section, force: true);
    }

    internal void RepositoryChanged()
    {
        if (_selectionReady && SelectedSection() == SettingsSection.Identity)
            _ = RefreshIdentityAsync(force: false);
    }

    private async void SettingsNavigation_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GeneralSettingsPanel is null
            || IdentitySettingsPanel is null
            || GitToolsSettingsPanel is null
            || DiagnosticsSettingsPanel is null)
            return;

        var section = SelectedSection();
        GeneralSettingsPanel.Visibility = section == SettingsSection.General ? Visibility.Visible : Visibility.Collapsed;
        IdentitySettingsPanel.Visibility = section == SettingsSection.Identity ? Visibility.Visible : Visibility.Collapsed;
        GitToolsSettingsPanel.Visibility = section == SettingsSection.GitTools ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsSettingsPanel.Visibility = section == SettingsSection.Diagnostics ? Visibility.Visible : Visibility.Collapsed;

        if (_selectionReady)
            await ActivateSectionAsync(section, force: false);
    }

    private Task ActivateSectionAsync(SettingsSection section, bool force) => section switch
    {
        SettingsSection.Identity => RefreshIdentityAsync(force),
        SettingsSection.GitTools => RefreshGitToolsAsync(force),
        _ => Task.CompletedTask
    };

    private static int IndexOfSection(SettingsSection section)
    {
        var index = Array.IndexOf(NavigationOrder, section);
        return index >= 0 ? index : 0;
    }

    private SettingsSection SelectedSection()
    {
        var index = SettingsNavigation.SelectedIndex;
        return index >= 0 && index < NavigationOrder.Length
            ? NavigationOrder[index]
            : SettingsSection.General;
    }

    private async Task RefreshIdentityAsync(bool force)
    {
        SettingsMessage.IsOpen = false;
        try
        {
            await _identityViewModel.RefreshAsync(force);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ShowSettingsError(exception);
        }
    }

    private async Task RefreshGitToolsAsync(bool force)
    {
        if (_gitToolsViewModel is null)
        {
            ShowSettingsError(new InvalidOperationException("Git Tools service is not available."));
            return;
        }
        if (!force && _gitToolsLoaded &&
            (_gitToolsViewModel.Editor.IsDirty || _gitToolsViewModel.Diff.IsDirty || _gitToolsViewModel.Merge.IsDirty))
            return;

        SettingsMessage.IsOpen = false;
        try
        {
            await _gitToolsViewModel.RefreshAsync(_repositoryAccessor());
            _gitToolsLoaded = true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ShowSettingsError(exception);
        }
    }

    private async void IdentitySave_Click(object sender, RoutedEventArgs e)
    {
        SettingsMessage.IsOpen = false;
        try
        {
            await _identityViewModel.SaveAsync();
            ShowSettingsSuccess("Identity", "Repository identity saved.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ShowSettingsError(exception);
        }
    }

    private async void IdentityNameRemove_Click(object sender, RoutedEventArgs e) =>
        await RemoveIdentityOverrideAsync(RepositoryIdentityField.Name);

    private async void IdentityEmailRemove_Click(object sender, RoutedEventArgs e) =>
        await RemoveIdentityOverrideAsync(RepositoryIdentityField.Email);

    private async void IdentityReload_Click(object sender, RoutedEventArgs e)
    {
        SettingsMessage.IsOpen = false;
        try
        {
            await _identityViewModel.DiscardChangesAndReloadAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ShowSettingsError(exception);
        }
    }

    private async Task RemoveIdentityOverrideAsync(RepositoryIdentityField field)
    {
        SettingsMessage.IsOpen = false;
        try
        {
            if (field == RepositoryIdentityField.Name)
                await _identityViewModel.RemoveNameOverrideAsync();
            else
                await _identityViewModel.RemoveEmailOverrideAsync();
            ShowSettingsSuccess("Identity", $"{field} repository override removed.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ShowSettingsError(exception);
        }
    }

    private async void EditorSave_Click(object sender, RoutedEventArgs e) =>
        await SaveGitToolAsync(_gitToolsViewModel?.Editor, "Git editor configuration saved.");

    private async void DiffSave_Click(object sender, RoutedEventArgs e) =>
        await SaveGitToolAsync(_gitToolsViewModel?.Diff, "Diff tool configuration saved.");

    private async void MergeSave_Click(object sender, RoutedEventArgs e) =>
        await SaveGitToolAsync(_gitToolsViewModel?.Merge, "Merge tool configuration saved.");

    private async void EditorRemove_Click(object sender, RoutedEventArgs e) =>
        await RemoveGitToolOverrideAsync(_gitToolsViewModel?.Editor);

    private async void DiffRemove_Click(object sender, RoutedEventArgs e) =>
        await RemoveGitToolOverrideAsync(_gitToolsViewModel?.Diff);

    private async void MergeRemove_Click(object sender, RoutedEventArgs e) =>
        await RemoveGitToolOverrideAsync(_gitToolsViewModel?.Merge);

    private async void EditorTest_Click(object sender, RoutedEventArgs e) =>
        await TestGitToolAsync(_gitToolsViewModel?.Editor, "Editor test completed.");

    private async void DiffTest_Click(object sender, RoutedEventArgs e) =>
        await TestGitToolAsync(_gitToolsViewModel?.Diff, "Diff tool test completed.");

    private async void MergeTest_Click(object sender, RoutedEventArgs e) =>
        await TestGitToolAsync(_gitToolsViewModel?.Merge, "Merge tool test completed in an isolated temporary repository.");

    private async Task SaveGitToolAsync(GitToolSectionViewModel? section, string successMessage)
    {
        if (_gitToolsViewModel is null || section is null) return;
        SettingsMessage.IsOpen = false;
        try
        {
            await _gitToolsViewModel.SaveAsync(_repositoryAccessor(), section);
            ShowSettingsSuccess("Git Tools", successMessage);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ShowSettingsError(exception);
        }
    }

    private async Task RemoveGitToolOverrideAsync(GitToolSectionViewModel? section)
    {
        if (_gitToolsViewModel is null || section is null) return;
        SettingsMessage.IsOpen = false;
        try
        {
            await _gitToolsViewModel.RemoveOverrideAsync(_repositoryAccessor(), section);
            ShowSettingsSuccess("Git Tools", "Override removed. Effective Git configuration was re-read.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ShowSettingsError(exception);
        }
    }

    private async Task TestGitToolAsync(GitToolSectionViewModel? section, string successMessage)
    {
        if (_gitToolsViewModel is null || section is null) return;
        SettingsMessage.IsOpen = false;
        try
        {
            await _gitToolsViewModel.TestAsync(_repositoryAccessor(), section);
            ShowSettingsSuccess("Git Tools", successMessage);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ShowSettingsError(exception);
        }
    }

    private async void DefaultRepositoriesDirectoryTextBox_LostFocus(
        object sender,
        RoutedEventArgs e)
    {
        if (!_selectionReady || _viewModel.IsSynchronizingFromSettings) return;

        SettingsMessage.IsOpen = false;
        try
        {
            await _viewModel.ApplyDefaultRepositoriesDirectoryAsync(
                _viewModel.DefaultRepositoriesDirectory);
        }
        catch (Exception exception)
        {
            ShowSettingsError(exception);
        }
    }

    private async void DefaultRepositoriesDirectoryBrowse_Click(
        object sender,
        RoutedEventArgs e)
    {
        SettingsMessage.IsOpen = false;
        try
        {
            var selected = await _folderPicker.PickFolderAsync();
            if (selected is null) return;
            await _viewModel.ApplyDefaultRepositoriesDirectoryAsync(selected);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ShowSettingsError(exception);
        }
    }

    private async void ThemeModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_selectionReady || _viewModel.IsSynchronizingFromSettings || ThemeModeComboBox.SelectedItem is not ApplicationThemeOption option) return;

        SettingsMessage.IsOpen = false;
        try
        {
            await _viewModel.ApplyThemeModeAsync(option);
        }
        catch (Exception exception)
        {
            ShowSettingsError(exception);
        }
    }

    private async void CommitTimeModeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_selectionReady || _viewModel.IsSynchronizingFromSettings || CommitTimeModeList.SelectedItem is not CommitTimeModeOption option) return;

        SettingsMessage.IsOpen = false;
        try
        {
            await _viewModel.ApplyCommitTimeModeAsync(option);
        }
        catch (Exception exception)
        {
            ShowSettingsError(exception);
        }
    }

    private async void AutoSetupRemoteOnPushToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_selectionReady || _viewModel.IsSynchronizingFromSettings) return;

        SettingsMessage.IsOpen = false;
        try
        {
            await _viewModel.ApplyAutoSetupRemoteOnPushAsync(AutoSetupRemoteOnPushToggle.IsOn);
        }
        catch (Exception exception)
        {
            ShowSettingsError(exception);
        }
    }

    private async void ShowAuthorAvatarsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_selectionReady || _viewModel.IsSynchronizingFromSettings) return;

        SettingsMessage.IsOpen = false;
        try
        {
            await _viewModel.ApplyShowAuthorAvatarsAsync(ShowAuthorAvatarsToggle.IsOn);
        }
        catch (Exception exception)
        {
            ShowSettingsError(exception);
        }
    }

    private async void OnlineAvatarLookupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_selectionReady || _viewModel.IsSynchronizingFromSettings) return;

        SettingsMessage.IsOpen = false;
        try
        {
            await _viewModel.ApplyOnlineAvatarLookupEnabledAsync(OnlineAvatarLookupToggle.IsOn);
        }
        catch (Exception exception)
        {
            ShowSettingsError(exception);
        }
    }

    private async void HistoryPerformanceDiagnosticsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_selectionReady || _viewModel.IsSynchronizingFromSettings) return;

        SettingsMessage.IsOpen = false;
        try
        {
            await _viewModel.ApplyHistoryPerformanceDiagnosticsEnabledAsync(
                HistoryPerformanceDiagnosticsToggle.IsOn);
        }
        catch (Exception exception)
        {
            ShowSettingsError(exception);
        }
    }

    private async void HistoryRenderingModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_selectionReady
            || _viewModel.IsSynchronizingFromSettings
            || HistoryRenderingModeComboBox.SelectedItem is not HistoryRenderingModeOption option)
            return;

        SettingsMessage.IsOpen = false;
        try
        {
            await _viewModel.ApplyHistoryRenderingModeAsync(option);
        }
        catch (Exception exception)
        {
            ShowSettingsError(exception);
        }
    }

    private async void GitConsoleAutoOpenComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_selectionReady || _viewModel.IsSynchronizingFromSettings || GitConsoleAutoOpenComboBox.SelectedItem is not GitConsoleAutoOpenOption option) return;

        SettingsMessage.IsOpen = false;
        try
        {
            await _viewModel.ApplyGitConsoleAutoOpenModeAsync(option);
        }
        catch (Exception exception)
        {
            ShowSettingsError(exception);
        }
    }

    private void CopyLogPath_Click(object sender, RoutedEventArgs e)
    {
        SettingsMessage.IsOpen = false;
        try
        {
            var package = new DataPackage();
            package.SetText(_logFilePath);
            Clipboard.SetContent(package);
        }
        catch (Exception exception)
        {
            ShowSettingsError(exception);
        }
    }

    private async void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        SettingsMessage.IsOpen = false;
        try
        {
            var directory = Path.GetDirectoryName(_logFilePath)
                ?? throw new InvalidOperationException("The log directory could not be determined.");
            Directory.CreateDirectory(directory);
            await _desktopShellService.OpenFolderAsync(directory);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ShowSettingsError(exception);
        }
    }

    private async void LoggingToggle_Toggled(object sender, RoutedEventArgs e)
    {
        LogLevelComboBox.IsEnabled = LoggingToggle.IsOn;
        if (!_selectionReady || _viewModel.IsSynchronizingFromSettings) return;
        await ApplyLoggingSettingsAsync();
    }

    private async void LogLevelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_selectionReady || _viewModel.IsSynchronizingFromSettings) return;
        await ApplyLoggingSettingsAsync();
    }

    private async Task ApplyLoggingSettingsAsync()
    {
        if (LogLevelComboBox.SelectedItem is not ApplicationLogLevelOption option) return;

        SettingsMessage.IsOpen = false;
        try
        {
            await _viewModel.ApplyLoggingSettingsAsync(LoggingToggle.IsOn, option);
        }
        catch (Exception exception)
        {
            ShowSettingsError(exception);
        }
    }

    private void ShowSettingsSuccess(string title, string message)
    {
        SettingsMessage.Title = title;
        SettingsMessage.Message = message;
        SettingsMessage.Severity = InfoBarSeverity.Success;
        SettingsMessage.IsOpen = true;
    }

    private void ShowSettingsError(Exception exception)
    {
        SettingsMessage.Title = "Could not apply settings";
        SettingsMessage.Message = exception.Message;
        SettingsMessage.Severity = InfoBarSeverity.Error;
        SettingsMessage.IsOpen = true;
    }

    internal void DetachSettings()
    {
        if (_settingsDetached) return;
        _settingsDetached = true;
        _selectionReady = false;
        _viewModel.Dispose();
    }
}
