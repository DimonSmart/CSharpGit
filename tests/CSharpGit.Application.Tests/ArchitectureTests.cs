namespace CSharpGit.Application.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void ApplicationAssemblyDoesNotReferencePresentationOrGitIntegration()
    {
        var references = typeof(CSharpGit.Application.Abstractions.IRepositoryService).Assembly.GetReferencedAssemblies();
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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
