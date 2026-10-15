using System.Diagnostics;
using CSharpGit.Application.Abstractions;
using CSharpGit.Application;
using CSharpGit.Git;
using CSharpGit.Infrastructure;
using CSharpGit.Presentation.Diagnostics;
using CSharpGit.Presentation.Threading;
using CSharpGit.Presentation.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace CSharpGit.Presentation;

public sealed partial class App : Microsoft.UI.Xaml.Application
{
    private readonly IAppSettingsService _appSettings;
    private readonly SessionFileLoggerProvider _sessionFileLoggerProvider;
    private readonly IHost _host;
    private Window? _window;
    private IDisposable? _mainThemeRegistration;
    private bool _closeConfirmed;
    private bool _closeConfirmationInProgress;
    private bool _shutdownRequested;
    private bool _finalCloseAuthorized;
    private static readonly TimeSpan DiagnosticsShutdownBudget = TimeSpan.FromSeconds(3);
    private bool _closeCheckStarted;
    private int _hostStopped;

    internal double MainWindowClientWidth => _window?.AppWindow.ClientSize.Width ?? 0;

    internal void ResizeMainWindowForCheck(int width, int height) =>
        _window?.AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = width, Height = height });

    public App()
    {
        InitializeComponent();

        var appSettings = new JsonAppSettingsService();
        _appSettings = appSettings;
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
                services.AddSingleton<IAppSettingsChangeSource>(provider => provider.GetRequiredService<IAppSettingsService>());
                services.AddSingleton<IAppearanceSettings>(provider => provider.GetRequiredService<IAppSettingsService>());
                services.AddSingleton<IGitWorkflowSettings>(provider => provider.GetRequiredService<IAppSettingsService>());
                services.AddSingleton<IDiagnosticsSettings>(provider => provider.GetRequiredService<IAppSettingsService>());
                services.AddSingleton<IRepositoryLauncherSettings>(provider => provider.GetRequiredService<IAppSettingsService>());
                services.AddSingleton<IUiDispatcher>(_ =>
                    new UiDispatcher(
                        DispatcherQueue.GetForCurrentThread()
                        ?? throw new InvalidOperationException("The UI dispatcher is not available on the current thread.")));
                services.AddSingleton<ApplicationThemeManager>();
                services.AddSingleton<IRepositoryImageService, RepositoryImageService>();
                services.AddSingleton<IAuthorAvatarService, AuthorAvatarService>();
                services.AddSingleton<SettingsWindowController>();
                services.AddSingleton<GitCommandActivityHistory>();
                services.AddSingleton<IGitCommandActivitySink>(
                    provider => provider.GetRequiredService<GitCommandActivityHistory>());
                services.AddSingleton<IGitCommandActivitySource>(
                    provider => provider.GetRequiredService<GitCommandActivityHistory>());

                var checkRepository = Environment.GetEnvironmentVariable("CSHARPGIT_UI_CHECK_REPOSITORY")
                                      ?? Program.InitialRepositoryPath;
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

                services.AddSingleton<IExternalToolProcessService, ExternalToolProcessService>();
                services.AddSingleton<IApplicationVersionProvider>(_ => new ApplicationVersionProvider(typeof(App).Assembly));
                services.AddSingleton<IUpdateCheckService, GitHubUpdateCheckService>();
                services.AddSingleton<IProcessExecutor, ProcessExecutor>();
                services.AddSingleton<IApplicationUpdateEnvironment, DefaultApplicationUpdateEnvironment>();
                if (OperatingSystem.IsMacOS())
                    services.AddSingleton<IApplicationUpdateInstaller, MacOsHomebrewUpdateInstaller>();
                else
                    services.AddSingleton<IApplicationUpdateInstaller, UnsupportedApplicationUpdateInstaller>();
                services.AddSingleton<ISystemUriLauncher, SystemUriLauncher>();
                services.AddCSharpGitGit();
                services.AddSingleton<IRepositoryPathService, RepositoryPathService>();
                services.AddSingleton<IDesktopShellService, DesktopShellService>();

                services.AddTransient<TagsViewModel>();
                services.AddTransient<RepositorySyncViewModel>();
                services.AddTransient<BranchesViewModel>();
                services.AddTransient<HistoryViewModel>();
                services.AddTransient<StashesViewModel>();
                services.AddTransient<CommitDetailsViewModel>();
                services.AddTransient<CommitActionsViewModel>();
                services.AddTransient<RepositoryHistoryRewriteViewModel>();
                services.AddTransient<CommitCreationViewModel>();
                services.AddTransient<RepositoryOperationsViewModel>();
                services.AddTransient<InteractiveRebaseViewModel>();
                services.AddTransient<WorktreesViewModel>();
                services.AddTransient<RepositoryMaintenanceViewModel>();
                services.AddTransient<FileActionsViewModel>();
                services.AddTransient<RepositoryFilesViewModel>();
                services.AddTransient<WorkingTreeViewModel>();
                services.AddTransient<OpenRepositoryViewModel>();
                services.AddTransient<CreateRepositoryViewModel>();
                services.AddTransient<CloneRepositoryViewModel>();
                services.AddTransient<MainPage>();
            })
            .Build();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try { await LaunchCoreAsync(args); }
        catch (Exception exception)
        {
            LogUiFailure(exception, "Application startup");
            try { _window?.Close(); }
            catch (Exception closeFailure) { LogUiFailure(closeFailure, "Failed startup cleanup"); }
        }
    }

    private async Task LaunchCoreAsync(LaunchActivatedEventArgs args)
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
        mainPage.WindowTitleChanged += MainPage_WindowTitleChanged;
        _window.Title = mainPage.CurrentWindowTitle;
        _mainThemeRegistration = themeManager.Register(mainPage);
        _window.AppWindow.Changed += (_, eventArgs) =>
        {
            if (eventArgs.DidSizeChange) mainPage.QueueCommitDetailsLayout();
        };
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CSHARPGIT_UI_CHECK_RESULT")))
            _window.AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 1400, Height = 900 });

        _window.AppWindow.Closing += (sender, eventArgs) =>
        {
            if (!TryBeginNormalShutdown(_window.Content as MainPage))
                eventArgs.Cancel = true;
        };

        _window.Closed += (_, _) => ReportDesktopCloseCheck("WindowClosed");
        if (Environment.GetEnvironmentVariable("CSHARPGIT_CLOSE_CHECK") == "1")
            mainPage.Loaded += RunDesktopCloseCheckWhenLoaded;
        _window.Content = mainPage;
        SetWindowIcon(_window, startupLogger);
        _window.Activate();

        if (Program.InitialRepositoryPath is not null)
            await mainPage.OpenInitialRepositoryAsync();
    }

    private bool TryBeginNormalShutdown(MainPage? page)
    {
        ReportDesktopCloseCheck("Closing");
        if (_finalCloseAuthorized) return true;
        if (_shutdownRequested || _closeConfirmationInProgress) return false;
        if (page?.IsUnsafeToClose == true)
        {
            ReportDesktopCloseCheck("CloseCanceled: protected operation is active");
            return false;
        }

        if (!_closeConfirmed && page is not null && page.RequiresCloseConfirmation)
        {
            ReportDesktopCloseCheck("CloseCanceled: commit draft requires confirmation");
            _closeConfirmationInProgress = true;
            _ = UiAsyncOperationRunner.RunAsync(
                () => ConfirmAndCloseAsync(page),
                exception => LogUiFailure(exception, "Close confirmation"));
            return false;
        }

        BeginShutdown(page);
        return false;
    }

    internal void LogUiFailure(Exception exception, string operation)
    {
        try
        {
            _host.Services.GetService<ILoggerFactory>()?
                .CreateLogger("CSharpGit.UiLifecycle")
                .LogError(exception, "UI operation failed: {Operation}", operation);
        }
        catch (Exception loggerFailure)
        {
            Trace.TraceError($"UI logger failed: {loggerFailure}");
        }

        // Trace remains available even when application logging is disabled.
        Trace.TraceError($"UI operation '{operation}' failed: {exception}");
    }

    private void MainPage_WindowTitleChanged(string title)
    {
        if (_window is not null)
            _window.Title = title;
    }

    private static void SetWindowIcon(Window window, ILogger logger)
    {
        if (!OperatingSystem.IsWindows()) return;

        var iconPath = Path.Combine(AppContext.BaseDirectory, "CSharpGit.ico");
        if (!File.Exists(iconPath))
        {
            logger.LogWarning("Windows application icon was not found at {IconPath}.", iconPath);
            return;
        }

        window.AppWindow.SetIcon(iconPath);
    }

    private void AppSettings_Changed(object? sender, EventArgs e)
    {
        var settings = _appSettings;
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
            if (_shutdownRequested || !ReferenceEquals(_window?.Content, page) || page.IsUnsafeToClose)
                return;
            _closeConfirmed = true;
            BeginShutdown(page);
        }
        catch (Exception exception) { LogUiFailure(exception, "Close confirmation"); }
        finally { _closeConfirmationInProgress = false; }
    }

    private void BeginShutdown(MainPage? page)
    {
        if (_shutdownRequested) return;
        _shutdownRequested = true;
        ReportDesktopCloseCheck("CloseAccepted");
        _ = UiAsyncOperationRunner.RunAsync(
            () => FinalizeWindowShutdownAsync(page),
            exception => LogUiFailure(exception, "Window shutdown"));
    }

    private async Task FinalizeWindowShutdownAsync(MainPage? page)
    {
        try
        {
            if (page is not null)
            {
                var stopTask = page.PrepareForShutdown();
                try { await stopTask.WaitAsync(DiagnosticsShutdownBudget); }
                catch (TimeoutException)
                {
                    LogUiFailure(new TimeoutException("History diagnostics exceeded shutdown budget."),
                        "History diagnostics shutdown timeout");
                    _ = UiAsyncOperationRunner.RunAsync(
                        () => stopTask,
                        exception => LogUiFailure(exception, "Late history diagnostics shutdown"));
                }
                catch (Exception exception) { LogUiFailure(exception, "History diagnostics shutdown"); }

                page.WindowTitleChanged -= MainPage_WindowTitleChanged;
                page.BeginShutdown();
                ReportDesktopCloseCheck("PageShutdownCompleted");
            }
        }
        catch (Exception exception) { LogUiFailure(exception, "Final window cleanup"); }
        finally
        {
            _finalCloseAuthorized = true;
            try
            {
                var window = _window;
                if (window is not null)
                {
                    if (window.DispatcherQueue.HasThreadAccess) window.Close();
                    else if (!window.DispatcherQueue.TryEnqueue(window.Close))
                        LogUiFailure(new InvalidOperationException("Window dispatcher rejected final close."), "Final window close");
                }
            }
            catch (Exception exception) { LogUiFailure(exception, "Final window close"); }
        }
    }

    internal void CompleteApplicationUpdateRestart(MainPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (_shutdownRequested || !ReferenceEquals(_window?.Content, page) || page.IsUnsafeToClose)
            return;
        _closeConfirmed = true;
        BeginShutdown(page);
    }

    internal void StopHost()
    {
        if (Interlocked.Exchange(ref _hostStopped, 1) != 0) return;
        ReportDesktopCloseCheck("HostStopping");
        _appSettings.Changed -= AppSettings_Changed;
        _mainThemeRegistration?.Dispose();
        _mainThemeRegistration = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            _host.StopAsync(timeout.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            ReportDesktopCloseCheck("HostStopTimedOut");
        }
        finally
        {
            _host.Dispose();
            ReportDesktopCloseCheck("HostStopped");
        }
    }

    private sealed class FixedFolderPicker(string path) : IFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(path);
    }
}
