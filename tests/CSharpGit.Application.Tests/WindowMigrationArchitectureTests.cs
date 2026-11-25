namespace CSharpGit.Application.Tests;

public sealed class WindowMigrationArchitectureTests
{
    [Fact]
    public void AppDelegatesCloseAdmissionAndShutdownToOrchestrator()
    {
        var app = Read("src/CSharpGit.Presentation/App.xaml.cs");
        var close = Read("src/CSharpGit.Presentation/Windowing/WindowCloseOrchestrator.cs");
        var safety = Read("src/CSharpGit.Presentation/Windowing/WindowCloseSafetyProvider.cs");
        Assert.Contains("_windowClose?.RequestClose()", app);
        Assert.DoesNotContain("page.ConfirmCloseAsync()", app);
        Assert.DoesNotContain("page.PrepareForShutdown()", app);
        Assert.DoesNotContain("page.BeginShutdown()", app);
        Assert.DoesNotContain("page?.IsUnsafeToClose", app);
        Assert.Contains("_coordinator.Request(", close);
        Assert.Contains("_coordinator.CompleteConfirmation(", close);
        Assert.Contains("_coordinator.TrySchedule(", close);
        Assert.Contains("_coordinator.AuthorizeFinalClose();", close);
        Assert.Contains("WindowCloseSafetyState(", safety);
        Assert.Contains("session.IsUnsafeToClose || !ReferenceEquals(window.Content, content)", close);
    }

    [Fact]
    public void EachWindowHasScopedCompositionAndOneLifetimeOwner()
    {
        var app = Read("src/CSharpGit.Presentation/App.xaml.cs");
        var session = Read("src/CSharpGit.Presentation/Windowing/WindowSession.cs");
        var factory = Read("src/CSharpGit.Presentation/Windowing/WindowSessionFactory.cs");
        var page = Read("src/CSharpGit.Presentation/Composition/WindowWorkspaceComposition.Lifetime.cs");
        Assert.Contains("scopeFactory.CreateScope()", factory);
        Assert.Contains("Program.InitialRepositoryPath is not null", factory);
        var transition = Read("src/CSharpGit.Presentation.Core/WindowSessionLifecycle.cs");
        Assert.Contains("InitializeCoreAsync", transition);
        Assert.Contains("await initialize(cancellationToken)", transition);
        Assert.Contains("_lifetime.InitializeAsync(initializeContent, CompleteShutdown, cancellationToken)", session);
        Assert.Contains("await _windowSession.InitializeAsync(CancellationToken.None);", app);
        Assert.DoesNotContain("await mainPage.OpenInitialRepositoryAsync();", app);
        Assert.Contains("scope.ServiceProvider.GetRequiredService<WindowWorkspaceComposition>()", factory);
        Assert.Contains("finally { scope.Dispose(); }", session);
        Assert.Contains("completeContent();", session);
        Assert.DoesNotContain("MainPage", session);
        Assert.Contains("services.AddScoped<SettingsWindowController>();", app);
        Assert.Contains("services.AddScoped<RecentRepositoryFolderPicker>", app);
        Assert.DoesNotContain("_viewModel.Dispose()", page);
        Assert.DoesNotContain("_fileActions.Dispose()", page);
        Assert.DoesNotContain("_repositoryMaintenance.Dispose()", page);
    }

    [Fact]
    public void NewWindowingCodeDoesNotBecomeAServiceLocatorOrEventBus()
    {
        var root = Path.Combine(Root(), "src", "CSharpGit.Presentation", "Windowing");
        foreach (var file in Directory.GetFiles(root, "*.cs"))
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("MainPageContext", source);
            Assert.DoesNotContain("GlobalUiServices", source);
            Assert.DoesNotContain("EventBus", source);
            Assert.DoesNotContain("GetService(typeof", source);
        }
    }

    [Fact]
    public void XamlOnlyProbeParticipatesInDesktopSmokeCheck()
    {
        var runner = Read("src/CSharpGit.Presentation/Diagnostics/MainPageDesktopUiCheckRunner.cs");
        Assert.Contains("new Views.XamlOnlyProbe", runner);
        Assert.Contains("DataContext = probeData", runner);
        Assert.Contains("probeData.ProbeText = \"XAML-only binding updated\"", runner);
        Assert.Contains("XAML-only binding verified", runner);
    }

    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(Root(), relative.Replace('/', Path.DirectorySeparatorChar)));

    private static string Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
