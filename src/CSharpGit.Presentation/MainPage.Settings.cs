using Microsoft.UI.Xaml;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private SettingsWindowController _settingsWindowController = null!;

    private void Settings_Click(object sender, RoutedEventArgs e) =>
        OpenSettingsWindow(SettingsSection.General);

    private void RepositorySettings_Click(object sender, RoutedEventArgs e) =>
        OpenSettingsWindow(SettingsSection.Identity);

    private void PullSettings_Click(object sender, RoutedEventArgs e) =>
        OpenSettingsWindow(SettingsSection.General, focusPullStrategy: true);

    private void OpenSettingsWindow(
        SettingsSection section = SettingsSection.General,
        bool focusPullStrategy = false) =>
        _settingsWindowController.Show(section, () => _viewModel.Repository, focusPullStrategy);
}
