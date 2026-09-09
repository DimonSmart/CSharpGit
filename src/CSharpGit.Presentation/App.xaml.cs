using CSharpGit.Application.Abstractions;
using CSharpGit.Application;
using CSharpGit.Git;
using CSharpGit.Infrastructure;
using CSharpGit.Presentation.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace CSharpGit.Presentation;

public sealed partial class App : Microsoft.UI.Xaml.Application
{
    private readonly IHost _host;
    private Window? _window;
    private bool _closeConfirmed;
    private bool _closeConfirmationInProgress;
    private bool _shutdownRequested;
    private bool _closeCheckStarted;
    private int _hostStopped;

    public App()
    {
        InitializeComponent();
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
                logging.AddSimpleConsole(options => options.SingleLine = true);
            })
            .ConfigureServices(services =>
            {
                var checkRepository = Environment.GetEnvironmentVariable("CSHARPGIT_UI_CHECK_REPOSITORY");
                if (string.IsNullOrWhiteSpace(checkRepository))
                    services.AddSingleton<IFolderPicker>(_ => new NativeFolderPicker(() =>
                    {
                        if (_window is null) throw new InvalidOperationException("The main window has not been created.");
                        return WinRT.Interop.WindowNative.GetWindowHandle(_window);
                    }));
                else services.AddSingleton<IFolderPicker>(new FixedFolderPicker(checkRepository));
                services.AddSingleton<IRepositoryService, GitCliRepositoryService>();
                services.AddSingleton<IRepositoryStateService>(provider => (GitCliRepositoryService)provider.GetRequiredService<IRepositoryService>());
                services.AddSingleton<IWorkingTreeService>(provider => (GitCliRepositoryService)provider.GetRequiredService<IRepositoryService>());
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
        await _host.StartAsync();
        _window = new Window { Title = "CSharpGit" };
        _window.Content = _host.Services.GetRequiredService<MainPage>();
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CSHARPGIT_UI_CHECK_RESULT")))
            _window.AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 1400, Height = 900 });

        _window.AppWindow.Closing += (_, eventArgs) =>
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
