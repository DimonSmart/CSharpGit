using CSharpGit.Application.Abstractions;
using CSharpGit.Application;
using CSharpGit.Git;
using CSharpGit.Infrastructure;
using CSharpGit.Presentation.Diagnostics;
using CSharpGit.Presentation.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace CSharpGit.Presentation;

public sealed partial class App : Microsoft.UI.Xaml.Application
{
    private readonly SessionFileLoggerProvider _sessionFileLoggerProvider;
    private readonly IHost _host;
    private Window? _window;
    private IDisposable? _mainThemeRegistration;
    private bool _closeConfirmed;
    private bool _closeConfirmationInProgress;
    private bool _shutdownRequested;
    private bool _closeCheckStarted;
    private int _hostStopped;

    public App()
    {
        InitializeComponent();

        var appSettings = AppSettingsContext.Current;
        _sessionFileLoggerProvider = new SessionFileLoggerProvider(
            appSettings.LoggingEnabled,
            ToMicrosoftLogLevel(appSettings.LogLevel));
        appSettings.Changed += AppSettings_Changed;

        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(configuration =>
            {
                configuration.Sources.Clear();
                configuration.AddJsonFile("appsettings.json", optional: true);
                configuration.AddEnvironmentVariables("CSHARPGIT_");
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.AddSimpleConsole(options => options.SingleLine = true);
                logging.AddProvider(_sessionFileLoggerProvider);
            })
            .ConfigureServices(services =>
            {
                services.AddSingleton<IAppSettingsService>(appSettings);
                services.AddSingleton<ApplicationThemeManager>();

                var checkRepository = Environment.GetEnvironmentVariable("CSHARPGIT_UI_CHECK_REPOSITORY");
                services.AddSingleton<RecentRepositoryFolderPicker>(_ =>
                {
                    IFolderPicker innerPicker;
                    if (string.IsNullOrWhiteSpace(checkRepository))
                    {
                        innerPicker = new NativeFolderPicker(() =>
                        {
                            if (_window is null) throw new InvalidOperationException("The main window has not been created.");
                            return WinRT.Interop.WindowNative.GetWindowHandle(_window);
                        });
                    }
                    else
                    {
                        innerPicker = new FixedFolderPicker(checkRepository);
                    }

                    return new RecentRepositoryFolderPicker(innerPicker);
                });
                services.AddSingleton<IFolderPicker>(provider => provider.GetRequiredService<RecentRepositoryFolderPicker>());
                services.AddSingleton<IRepositoryService, GitCliRepositoryService>();
                services.AddSingleton<IRepositoryStateService>(provider => (GitCliRepositoryService)provider.GetRequiredService<IRepositoryService>());
                services.AddSingleton<IWorkingTreeService>(provider => (GitCliRepositoryService)provider.GetRequiredService<IRepositoryService>());
                services.AddSingleton<IWorkingTreeDiffService>(provider => (GitCliRepositoryService)provider.GetRequiredService<IRepositoryService>());
                services.AddSingleton<IReferenceService>(provider => (GitCliRepositoryService)provider.GetRequiredService<IRepositoryService>());
                services.AddSingleton<IRepositoryWorkflowService>(provider => (GitCliRepositoryService)provider.GetRequiredService<IRepositoryService>());
                services.AddSingleton<GitReferenceHistoryService>();
                services.AddSingleton<IHistoryService>(provider => provider.GetRequiredService<GitReferenceHistoryService>());
                services.AddSingleton<IReferenceHistoryService>(provider => provider.GetRequiredService<GitReferenceHistoryService>());
                services.AddSingleton<IRepositoryStateSessionFactory, RepositoryStateSessionFactory>();
                services.AddTransient<OpenRepositoryViewModel>();
                services.AddTransient<MainPage>();
            })
            .Build();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _sessionFileLoggerProvider.WriteDirect(
            "CSharpGit.Startup",
            "ApplicationLaunching",
            $"version={typeof(App).Assembly.GetName().Version} os={Environment.OSVersion} process={Environment.ProcessId} log={SessionFileLoggerProvider.CurrentLogPath}");

        await _host.StartAsync();
        var loggerFactory = _host.Services.GetRequiredService<ILoggerFactory>();
        var startupLogger = loggerFactory.CreateLogger("CSharpGit.Startup");
        startupLogger.LogInformation(
            "Application started. version={Version} os={OS} process={ProcessId} log={LogPath}",
            typeof(App).Assembly.GetName().Version,
            Environment.OSVersion,
            Environment.ProcessId,
            SessionFileLoggerProvider.CurrentLogPath);

        _window = new Window { Title = "CSharpGit" };
        var themeManager = _host.Services.GetRequiredService<ApplicationThemeManager>();
        var mainPage = _host.Services.GetRequiredService<MainPage>();
        mainPage.InitializeApplicationTheme(themeManager);
        mainPage.InitializeRecentRepositories(
            AppSettingsContext.Current,
            _host.Services.GetRequiredService<RecentRepositoryFolderPicker>());
        _mainThemeRegistration = themeManager.Register(mainPage);
        _window.Content = mainPage;
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CSHARPGIT_UI_CHECK_RESULT")))
            _window.AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 1400, Height = 900 });

        _window.AppWindow.Closing += (sender, eventArgs) =>
        {
            var page = _window.Content as MainPage;
            if (_closeConfirmed)
            {
                BeginShutdown(page);
                return;
            }

            if (page is null || !page.RequiresCloseConfirmation)
            {
                BeginShutdown(page);
                return;
            }

            eventArgs.Cancel = true;
            if (_closeConfirmationInProgress) return;
            _closeConfirmationInProgress = true;
            _ = ConfirmAndCloseAsync(page);
        };

        _window.Activated += async (_, _) =>
        {
            if (_shutdownRequested) return;

            if (Environment.GetEnvironmentVariable("CSHARPGIT_CLOSE_CHECK") == "1")
            {
                if (_closeCheckStarted) return;
                _closeCheckStarted = true;
                if (_window.Content is FrameworkElement { DataContext: OpenRepositoryViewModel closeCheckViewModel })
                    await closeCheckViewModel.OpenRepositoryAsyncForDesktopCheck();
                _window.DispatcherQueue.TryEnqueue(() => _window?.Close());
                return;
            }

            // The desktop check owns refresh timing. Window activation is not a
            // repository change and must not race its deterministic initial load.
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CSHARPGIT_UI_CHECK_RESULT"))) return;
            if (_window.Content is FrameworkElement { DataContext: OpenRepositoryViewModel viewModel })
                await viewModel.RefreshWhenActivatedAsync();
        };
        _window.Activate();
    }

    private void AppSettings_Changed(object? sender, EventArgs e)
    {
        var settings = AppSettingsContext.Current;
        _sessionFileLoggerProvider.Configure(
            settings.LoggingEnabled,
            ToMicrosoftLogLevel(settings.LogLevel));
    }

    private static LogLevel ToMicrosoftLogLevel(ApplicationLogLevel level) => level switch
    {
        ApplicationLogLevel.Trace => LogLevel.Trace,
        ApplicationLogLevel.Debug => LogLevel.Debug,
        ApplicationLogLevel.Information => LogLevel.Information,
        ApplicationLogLevel.Warning => LogLevel.Warning,
        ApplicationLogLevel.Error => LogLevel.Error,
        ApplicationLogLevel.Critical => LogLevel.Critical,
        _ => LogLevel.Information
    };

    private async Task ConfirmAndCloseAsync(MainPage page)
    {
        try
        {
            if (!await page.ConfirmCloseAsync()) return;
            _closeConfirmed = true;
            BeginShutdown(page);
            page.DispatcherQueue.TryEnqueue(() => _window?.Close());
        }
        finally
        {
            _closeConfirmationInProgress = false;
        }
    }

    private void BeginShutdown(MainPage? page)
    {
        if (_shutdownRequested) return;
        _shutdownRequested = true;
        page?.BeginShutdown();
    }

    internal void StopHost()
    {
        if (Interlocked.Exchange(ref _hostStopped, 1) != 0) return;
        AppSettingsContext.Current.Changed -= AppSettings_Changed;
        _mainThemeRegistration?.Dispose();
        _mainThemeRegistration = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            _host.StopAsync(timeout.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _host.Dispose();
        }
    }

    private sealed class FixedFolderPicker(string path) : IFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(path);
    }
}
