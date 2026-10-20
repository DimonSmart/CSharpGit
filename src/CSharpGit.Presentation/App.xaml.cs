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
using Uno.UI.Xaml;

namespace CSharpGit.Presentation;

public sealed partial class App : Microsoft.UI.Xaml.Application
{
    private readonly IAppSettingsService _appSettings;
    private readonly SessionFileLoggerProvider _sessionFileLoggerProvider;
    private readonly IHost _host;
    private Window? _window;
    private IDisposable? _mainThemeRegistration;
    private readonly WindowCloseCoordinator _closeCoordinator = new();
    private static readonly TimeSpan DiagnosticsShutdownBudget = TimeSpan.FromSeconds(3);
    private bool _closeCheckStarted;
    private int _hostStopped;
    private readonly object _hostStopGate = new();
    private Task? _hostStopTask;

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
        // The final native request can arrive after MainPage was disposed.
        if (_closeCoordinator.Stage == WindowCloseStage.FinalClose)
        {
            ReportDesktopCloseCheck("FinalCloseAllowed");
            return true;
        }

        var decision = _closeCoordinator.Request(
            page?.IsUnsafeToClose == true,
            page?.RequiresCloseConfirmation == true);

        switch (decision)
        {
            case WindowCloseDecision.AllowFinalClose:
                ReportDesktopCloseCheck("FinalCloseAllowed");
                return true;

            case WindowCloseDecision.Confirm:
                ReportDesktopCloseCheck("CloseCanceled: commit draft requires confirmation");
                _ = UiAsyncOperationRunner.RunAsync(
                    () => ConfirmAndCloseAsync(page!),
                    exception =>
                    {
                        _closeCoordinator.CancelConfirmation();
                        LogUiFailure(exception, "Close confirmation");
                    });
                return false;

            case WindowCloseDecision.Schedule:
                ScheduleShutdown(page);
                return false;

            default:
                if (page?.IsUnsafeToClose == true)
                    ReportDesktopCloseCheck("CloseCanceled: protected operation is active");
                return false;
        }
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
            ReportDesktopCloseCheck("CloseConfirmationStarted");
            var confirmed = await page.ConfirmCloseAsync();
            var accepted = _closeCoordinator.CompleteConfirmation(
                confirmed,
                page.IsUnsafeToClose || !ReferenceEquals(_window?.Content, page));
            if (!accepted)
            {
                ReportDesktopCloseCheck("CloseConfirmationCanceled");
                return;
            }

            ReportDesktopCloseCheck("CloseConfirmed");
            ScheduleShutdown(page);
        }
        catch (Exception exception)
        {
            _closeCoordinator.CancelConfirmation();
            LogUiFailure(exception, "Close confirmation");
        }
    }

    private void ScheduleShutdown(MainPage? page)
    {
        var window = _window;
        if (window is null) return;

        try
        {
            // macOS windowShouldClose must return before we start any cleanup
            // or invoke Window.Close again. Enqueue *before* disposing resources.
            var scheduled = _closeCoordinator.TrySchedule(() =>
            {
                if (OperatingSystem.IsMacOS())
                    return window.DispatcherQueue.TryEnqueue(() => BeginShutdown(page));

                // Preserve Windows' existing WM_CLOSE route and Linux behavior.
                BeginShutdown(page);
                return true;
            });

            if (!scheduled)
            {
                ReportDesktopCloseCheck("CloseSchedulingRejected");
                LogUiFailure(
                    new InvalidOperationException("Window dispatcher rejected shutdown scheduling."),
                    "Window shutdown scheduling");
                return;
            }

            ReportDesktopCloseCheck("CloseScheduled");
        }
        catch (Exception exception)
        {
            ReportDesktopCloseCheck("CloseSchedulingFailed");
            LogUiFailure(exception, "Window shutdown scheduling");
        }
    }

    private void BeginShutdown(MainPage? page)
    {
        if (!_closeCoordinator.BeginShutdown()) return;
        ReportDesktopCloseCheck("CloseAccepted");
        ReportDesktopCloseCheck("ShutdownStarted");
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
                try
                {
                    await UiBoundedTaskWait.WaitAsync(
                        page.PrepareForShutdown(),
                        DiagnosticsShutdownBudget,
                        exception => LogUiFailure(exception, "History diagnostics shutdown"));
                }
                finally
                {
                    // A failed PrepareForShutdown or diagnostics writer must not
                    // bypass page disposal or authorize an undisposed window.
                    page.WindowTitleChanged -= MainPage_WindowTitleChanged;
                    page.BeginShutdown();
                    ReportDesktopCloseCheck("PageShutdownCompleted");
                }
            }
        }
        catch (Exception exception) { LogUiFailure(exception, "Final window cleanup"); }
        finally
        {
            // On macOS the native loop can exit as soon as the window closes.
            // Complete managed shutdown asynchronously before that boundary.
            if (OperatingSystem.IsMacOS())
            {
                try { await StopHostAsync(); }
                catch (Exception exception) { LogUiFailure(exception, "Host shutdown before window close"); }
            }

            _closeCoordinator.AuthorizeFinalClose();
            try
            {
                var window = _window;
                if (window is not null)
                {
                    // macOS shutdown started in a separate UI queue callback,
                    // after the original windowShouldClose stack returned.
                    if (window.DispatcherQueue.HasThreadAccess)
                        CloseAuthorizedWindow();
                    else if (!window.DispatcherQueue.TryEnqueue(CloseAuthorizedWindow))
                    {
                        LogUiFailure(new InvalidOperationException(
                            "Window dispatcher rejected final close after cleanup."), "Final window close");
                        FailFinalMacOsClose();
                    }
                }
            }
            catch (Exception exception)
            {
                LogUiFailure(exception, "Final window close");
                FailFinalMacOsClose();
            }
        }
    }

    private void CloseAuthorizedWindow()
    {
        var window = _window;
        if (window is null) return;
        ReportDesktopCloseCheck("FinalCloseInvoked");

        if (OperatingSystem.IsWindows())
        {
            // Direct Window.Close bypasses Win32 WM_CLOSE's cancelable route and
            // can leave Uno's native message loop running after WindowClosed.
            // We already completed shutdown and authorized this final WM_CLOSE.
            if (window.GetNativeWindow() is Uno.UI.NativeElementHosting.Win32NativeWindow native
                && PostMessageW(native.Hwnd, 0x0010, 0, 0))
                return;

            LogUiFailure(new InvalidOperationException("Could not dispatch the authorized WM_CLOSE."),
                "Final window close");
        }

        window.Close();
    }

    // Only used when the app has already been approved and disposed, but the
    // dispatcher/native close failed. Never used for normal close requests.
    private static void FailFinalMacOsClose()
    {
        if (!OperatingSystem.IsMacOS()) return;
        ReportDesktopCloseCheck("FatalFinalCloseFailure");
        Environment.Exit(1);
    }

    internal void CompleteApplicationUpdateRestart(MainPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (_closeCoordinator.Stage != WindowCloseStage.Idle
            || !ReferenceEquals(_window?.Content, page) || page.IsUnsafeToClose)
            return;
        ScheduleShutdown(page);
    }

    // Program's synchronous join occurs only after UnoPlatformHost.RunAsync exits.
    internal void StopHost() => StopHostAsync().GetAwaiter().GetResult();

    private Task StopHostAsync()
    {
        lock (_hostStopGate)
            return _hostStopTask ??= StopHostCoreAsync();
    }

    private async Task StopHostCoreAsync()
    {
        if (Interlocked.Exchange(ref _hostStopped, 1) != 0) return;
        ReportDesktopCloseCheck("HostStopping");
        _appSettings.Changed -= AppSettings_Changed;
        try { _mainThemeRegistration?.Dispose(); }
        catch (Exception exception) { LogUiFailure(exception, "Theme cleanup"); }
        finally { _mainThemeRegistration = null; }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { await _host.StopAsync(timeout.Token); }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            ReportDesktopCloseCheck("HostStopTimedOut");
        }
        catch (Exception exception) { LogUiFailure(exception, "Host stop"); }
        finally
        {
            try { _host.Dispose(); }
            catch (Exception exception) { LogUiFailure(exception, "Host dispose"); }
            finally { ReportDesktopCloseCheck("HostStopped"); }
        }
    }

    private sealed class FixedFolderPicker(string path) : IFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(path);
    }
}
