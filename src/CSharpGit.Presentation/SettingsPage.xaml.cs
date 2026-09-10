using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class SettingsPage : Page
{
    private readonly SettingsViewModel _viewModel = new(AppSettingsContext.Current);
    private bool _selectionReady;
    private bool _settingsDetached;

    public SettingsPage()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += (_, _) =>
        {
            _selectionReady = true;
            LogLevelComboBox.IsEnabled = LoggingToggle.IsOn;
        };
    }

    private void SettingsNavigation_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GeneralSettingsPanel is null || DiagnosticsSettingsPanel is null) return;

        var showDiagnostics = SettingsNavigation.SelectedIndex == 1;
        GeneralSettingsPanel.Visibility = showDiagnostics ? Visibility.Collapsed : Visibility.Visible;
        DiagnosticsSettingsPanel.Visibility = showDiagnostics ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void ThemeModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_selectionReady || ThemeModeComboBox.SelectedItem is not ApplicationThemeOption option) return;

        SettingsError.IsOpen = false;
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
        if (!_selectionReady || CommitTimeModeList.SelectedItem is not CommitTimeModeOption option) return;

        SettingsError.IsOpen = false;
        try
        {
            await _viewModel.ApplyCommitTimeModeAsync(option);
        }
        catch (Exception exception)
        {
            ShowSettingsError(exception);
        }
    }

    private async void LoggingToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_selectionReady) return;
        LogLevelComboBox.IsEnabled = LoggingToggle.IsOn;
        await ApplyLoggingSettingsAsync();
    }

    private async void LogLevelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_selectionReady) return;
        await ApplyLoggingSettingsAsync();
    }

    private async Task ApplyLoggingSettingsAsync()
    {
        if (LogLevelComboBox.SelectedItem is not ApplicationLogLevelOption option) return;

        SettingsError.IsOpen = false;
        try
        {
            await _viewModel.ApplyLoggingSettingsAsync(LoggingToggle.IsOn, option);
        }
        catch (Exception exception)
        {
            ShowSettingsError(exception);
        }
    }

    private void ShowSettingsError(Exception exception)
    {
        SettingsError.Message = exception.Message;
        SettingsError.IsOpen = true;
    }

    internal void DetachSettings()
    {
        if (_settingsDetached) return;
        _settingsDetached = true;
        _selectionReady = false;
        _viewModel.Dispose();
    }
}
