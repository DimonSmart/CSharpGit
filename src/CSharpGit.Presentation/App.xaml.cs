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
                services.AddSingleton<IHistoryService>(provider => (GitCliRepositoryService)provider.GetRequiredService<IRepositoryService>());
                services.AddSingleton<IReferenceService>(provider => (GitCliRepositoryService)provider.GetRequiredService<IRepositoryService>());
                services.AddSingleton<IRepositoryWorkflowService>(provider => (GitCliRepositoryService)provider.GetRequiredService<IRepositoryService>());
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
        _window.Closed += async (_, _) => await _host.StopAsync();
        _window.AppWindow.Closing += async (_, eventArgs) =>
        {
            if (_closeConfirmed || _window.Content is not MainPage page) return;
            eventArgs.Cancel = true;
            if (await page.ConfirmCloseAsync())
            {
                _closeConfirmed = true;
                _window.Close();
            }
        };
        _window.Activated += async (_, _) =>
        {
            // The desktop check owns refresh timing. Window activation is not a
            // repository change and must not race its deterministic initial load.
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CSHARPGIT_UI_CHECK_RESULT"))) return;
            if (_window.Content is FrameworkElement { DataContext: OpenRepositoryViewModel viewModel })
                await viewModel.RefreshWhenActivatedAsync();
        };
        _window.Activate();
    }

    private sealed class FixedFolderPicker(string path) : IFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(path);
    }
}
