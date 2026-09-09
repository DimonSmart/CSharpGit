using Microsoft.UI.Xaml;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private Window? _settingsWindow;

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        var window = new Window
        {
            Title = "CSharpGit Settings",
            Content = new SettingsPage { RequestedTheme = RootLayout.RequestedTheme }
        };
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 860, Height = 590 });
        window.AppWindow.Closing += (_, _) =>
        {
            if (ReferenceEquals(_settingsWindow, window)) _settingsWindow = null;
        };

        _settingsWindow = window;
        window.Activate();
    }

    private void CloseSettingsWindow()
    {
        var window = _settingsWindow;
        _settingsWindow = null;
        window?.Close();
    }
}
