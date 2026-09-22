using CSharpGit.Application.Abstractions;

namespace CSharpGit.Application.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void ApplicationAssemblyDoesNotReferencePresentationOrGitIntegration()
    {
        var references = typeof(IRepositoryService).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(references, reference => reference.Name is "CSharpGit.Presentation" or "CSharpGit.Git");
        Assert.DoesNotContain(references, reference => reference.Name is "Microsoft.UI.Xaml" or "Uno.UI");
    }

    [Fact]
    public void ThemePersistenceDoesNotDependOnPresentationTypes()
    {
        var root = FindRepositoryRoot();
        var application = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Application", "Abstractions", "ApplicationThemeMode.cs"))
            + File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Application", "Abstractions", "IAppSettingsService.cs"));
        var infrastructure = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Infrastructure", "JsonAppSettingsService.cs"));

        Assert.DoesNotContain("Microsoft.UI.Xaml", application);
        Assert.DoesNotContain("ElementTheme", application);
        Assert.DoesNotContain("FrameworkElement", application);
        Assert.DoesNotContain("Microsoft.UI.Xaml", infrastructure);
        Assert.DoesNotContain("ElementTheme", infrastructure);
        Assert.DoesNotContain("FrameworkElement", infrastructure);
    }

    [Fact]
    public void ApplicationInterfacesDoNotContainDefaultInstanceImplementations()
    {
        var abstractionNamespace = typeof(IRepositoryService).Namespace;
        var concreteInstanceMethods = typeof(IRepositoryService).Assembly
            .GetTypes()
            .Where(type => type.IsInterface && type.Namespace == abstractionNamespace)
            .SelectMany(type => type.GetMethods()
                .Where(method => !method.IsStatic && !method.IsAbstract)
                .Select(method => $"{type.FullName}.{method.Name}"))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(concreteInstanceMethods);
    }

    [Fact]
    public void RepositoryApplicationContractsRemainSeparatedByCapability()
    {
        var abstractions = Path.Combine(FindRepositoryRoot(), "src", "CSharpGit.Application", "Abstractions");
        var stateSource = File.ReadAllText(Path.Combine(abstractions, "IRepositoryStateService.cs"));

        foreach (var declaration in new[]
                 {
                     "IRepositoryRefreshProbe",
                     "IWorkingTreeService",
                     "IReferenceService",
                     "IRepositorySyncService",
                     "ICommitActionService",
                     "IRepositoryWorkflowService",
                     "IRepositoryStateSession"
                 })
            Assert.DoesNotContain($"interface {declaration}", stateSource, StringComparison.Ordinal);

        foreach (var file in new[]
                 {
                     "IRepositoryStateService.cs",
                     "IRepositoryRefreshProbe.cs",
                     "IWorkingTreeService.cs",
                     "IReferenceService.cs",
                     "IRepositorySyncService.cs",
                     "ICommitActionService.cs",
                     "IRepositoryWorkflowService.cs",
                     "IRepositoryStateSession.cs"
                 })
            Assert.True(File.Exists(Path.Combine(abstractions, file)), $"Missing application contract file: {file}");
    }

    [Fact]
    public void PresentationCompositionRootOwnsLongLivedServices()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var app = File.ReadAllText(Path.Combine(presentation, "App.xaml.cs"));

        Assert.False(File.Exists(Path.Combine(presentation, "AppSettingsContext.cs")));
        Assert.False(File.Exists(Path.Combine(presentation, "RepositoryImageServices.cs")));
        Assert.Contains("AddSingleton<IRepositoryImageService, RepositoryImageService>()", app);
        Assert.Contains("AddSingleton<GitCommandActivityHistory>()", app);
        Assert.Contains("AddSingleton<IGitCommandActivitySink>", app);
        Assert.Contains("AddSingleton<IGitCommandActivitySource>", app);
        Assert.DoesNotContain("mainPage.Initialize", app, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
