using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace CSharpGit.Presentation;

internal enum SettingsSection
{
    General = 0,
    GitTools = 1,
    Diagnostics = 2
}

public sealed partial class SettingsPage : Page
{
    private readonly SettingsViewModel _viewModel;
    private readonly GitToolsSettingsViewModel _gitToolsViewModel;
    private readonly IDesktopShellService _desktopShellService;
    private readonly string _logFilePath;
    private readonly Func<Repository?> _repositoryAccessor;
    private bool _selectionReady;
    private bool _settingsDetached;
    private bool _gitToolsLoaded;

    internal SettingsPage(
        SettingsViewModel viewModel,
        GitToolsSettingsViewModel gitToolsViewModel,
        IDesktopShellService desktopShellService,
        string logFilePath,
        Func<Repository?> repositoryAccessor,
        SettingsSection initialSection)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _gitToolsViewModel = gitToolsViewModel ?? throw new ArgumentNullException(nameof(gitToolsViewModel));
        _desktopShellService = desktopShellService ?? throw new ArgumentNullException(nameof(desktopShellService));
        _logFilePath = string.IsNullOrWhiteSpace(logFilePath)
            ? throw new ArgumentException("A log file path is required.", nameof(logFilePath))
            : Path.GetFullPath(logFilePath);
        _repositoryAccessor = repositoryAccessor ?? throw new ArgumentNullException(nameof(repositoryAccessor));

        InitializeComponent();
        LogFilePathText.Text = _logFilePath;
        DataContext = _viewModel;
        GitToolsSettingsPanel.DataContext = _gitToolsViewModel;

        SettingsNavigation.SelectedIndex = (int)initialSection;
        Loaded += async (_, _) =>
        {
            _selectionReady = true;
            LogLevelComboBox.IsEnabled = LoggingToggle.IsOn;
            if (SettingsNavigation.SelectedIndex == (int)SettingsSection.GitTools)
                await RefreshGitToolsAsync(force: true);
        };
    }

    internal void SelectSection(SettingsSection section)
    {
        SettingsNavigation.SelectedIndex = (int)section;
    }

    private async void SettingsNavigation_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GeneralSettingsPanel is null || GitToolsSettingsPanel is null || DiagnosticsSettingsPanel is null) return;

        var section = (SettingsSection)Math.Clamp(SettingsNavigation.SelectedIndex, 0, 2);
        GeneralSettingsPanel.Visibility = section == SettingsSection.General ? Visibility.Visible : Visibility.Collapsed;
        GitToolsSettingsPanel.Visibility = section == SettingsSection.GitTools ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsSettingsPanel.Visibility = section == SettingsSection.Diagnostics ? Visibility.Visible : Visibility.Collapsed;

        if (_selectionReady && section == SettingsSection.GitTools)
            await RefreshGitToolsAsync(force: false);
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
            ShowSettingsSuccess(successMessage);
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
            ShowSettingsSuccess("Override removed. Effective Git configuration was re-read.");
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
            ShowSettingsSuccess(successMessage);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
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

    private void ShowSettingsSuccess(string message)
    {
        SettingsMessage.Title = "Git Tools";
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
