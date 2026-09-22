using Microsoft.UI.Xaml;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private SettingsWindowController _settingsWindowController = null!;

    private void Settings_Click(object sender, RoutedEventArgs e) =>
        OpenSettingsWindow(SettingsSection.General);

    private void OpenSettingsWindow(SettingsSection section = SettingsSection.General) =>
        _settingsWindowController.Show(section, () => _viewModel.Repository);
}
