using Microsoft.UI.Xaml;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private ApplicationThemeManager? _themeManager;
    private Window? _settingsWindow;
    private SettingsPage? _settingsPage;
    private IDisposable? _settingsThemeRegistration;

    internal void InitializeApplicationTheme(ApplicationThemeManager themeManager)
    {
        ArgumentNullException.ThrowIfNull(themeManager);
        _themeManager = themeManager;
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettingsWindow();

    private void OpenSettingsWindow()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        var themeManager = _themeManager
            ?? throw new InvalidOperationException("Application theme manager has not been initialized.");
        var page = new SettingsPage();
        var themeRegistration = themeManager.Register(page);
        var window = new Window
        {
            Title = "CSharpGit Settings",
            Content = page
        };
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 860, Height = 590 });
        window.AppWindow.Closing += (_, _) =>
        {
            themeRegistration.Dispose();
            page.DetachSettings();
            if (!ReferenceEquals(_settingsWindow, window)) return;

            _settingsThemeRegistration = null;
            _settingsPage = null;
            _settingsWindow = null;
        };

        _settingsWindow = window;
        _settingsPage = page;
        _settingsThemeRegistration = themeRegistration;
        window.Activate();
    }

    private void CloseSettingsWindow()
    {
        var window = _settingsWindow;
        if (window is not null)
        {
            window.Close();
            return;
        }

        _settingsThemeRegistration?.Dispose();
        _settingsThemeRegistration = null;
        _settingsPage?.DetachSettings();
        _settingsPage = null;
    }
}
