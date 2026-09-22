using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;

namespace CSharpGit.Presentation;

public sealed class SettingsWindowController : IDisposable
{
    private readonly ApplicationThemeManager _themeManager;
    private readonly IAppSettingsService _settings;
    private readonly IGitToolsService _gitToolsService;
    private Window? _window;
    private SettingsPage? _page;
    private IDisposable? _themeRegistration;
    private int _shutdownStarted;

    public SettingsWindowController(
        ApplicationThemeManager themeManager,
        IAppSettingsService settings,
        IGitToolsService gitToolsService)
    {
        _themeManager = themeManager ?? throw new ArgumentNullException(nameof(themeManager));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _gitToolsService = gitToolsService ?? throw new ArgumentNullException(nameof(gitToolsService));
    }

    internal void Show(SettingsSection section, Func<Repository?> repositoryAccessor)
    {
        if (Volatile.Read(ref _shutdownStarted) != 0) return;
        ArgumentNullException.ThrowIfNull(repositoryAccessor);

        if (_window is not null)
        {
            _page?.SelectSection(section);
            _window.Activate();
            return;
        }

        var page = new SettingsPage(
            new SettingsViewModel(_settings),
            new GitToolsSettingsViewModel(_gitToolsService),
            repositoryAccessor,
            section);
        var themeRegistration = _themeManager.Register(page);
        var window = new Window
        {
            Title = "CSharpGit Settings",
            Content = page
        };
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 980, Height = 720 });
        window.AppWindow.Closing += (_, _) => Cleanup(window, page, themeRegistration);

        _window = window;
        _page = page;
        _themeRegistration = themeRegistration;
        window.Activate();
    }

    internal void Shutdown()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0) return;

        var window = _window;
        var page = _page;
        var registration = _themeRegistration;
        if (window is not null)
            window.Close();
        if (page is not null && registration is not null)
            Cleanup(window, page, registration);
    }

    public void Dispose() => Shutdown();

    private void Cleanup(Window? window, SettingsPage page, IDisposable themeRegistration)
    {
        themeRegistration.Dispose();
        page.DetachSettings();
        if (!ReferenceEquals(_window, window)) return;

        _themeRegistration = null;
        _page = null;
        _window = null;
    }
}
