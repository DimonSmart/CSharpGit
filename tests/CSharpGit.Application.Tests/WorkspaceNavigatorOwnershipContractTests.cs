namespace CSharpGit.Application.Tests;

public sealed class WorkspaceNavigatorOwnershipContractTests
{
    [Fact]
    public void NavigatorIsRegisteredOncePerWindowAndNotConstructedByTheRootView()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var app = File.ReadAllText(Path.Combine(presentation, "App.xaml.cs"));
        var factory = File.ReadAllText(Path.Combine(presentation, "Windowing", "WindowSessionFactory.cs"));
        var session = File.ReadAllText(Path.Combine(presentation, "Windowing", "WindowSession.cs"));
        var page = File.ReadAllText(Path.Combine(presentation, "MainPage.xaml.cs"));

        Assert.Contains("services.AddScoped(provider =>", app);
        Assert.Contains("return new WorkspaceNavigator(", app);
        Assert.Contains("GetRequiredService<WorkspaceNavigator>()", factory);
        Assert.Contains("WorkspaceNavigator navigator,", session);
        Assert.Contains("WorkspaceNavigator Navigator { get; } = navigator;", session);
        Assert.Contains("public MainPage(", page);
        Assert.Contains("WorkspaceNavigator workspaceNavigator)", page);
        Assert.Contains("_workspaceNavigator = workspaceNavigator", page);
        Assert.DoesNotContain("new WorkspaceNavigator(", page);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;
        return directory?.FullName ??
            throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
