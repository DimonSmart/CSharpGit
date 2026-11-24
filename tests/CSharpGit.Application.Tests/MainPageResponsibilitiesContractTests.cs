namespace CSharpGit.Application.Tests;

public sealed class MainPageResponsibilitiesContractTests
{
    [Fact]
    public void DesktopSmokeScenariosAreOwnedByDedicatedRunner()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var page = File.ReadAllText(Path.Combine(presentation, "MainPage.xaml.cs"));
        var runner = File.ReadAllText(Path.Combine(presentation, "Diagnostics", "MainPageDesktopUiCheckRunner.cs"));
        var graph = File.ReadAllText(Path.Combine(presentation, "Diagnostics", "MainPageDesktopUiCheckRunner.Graph.cs"));

        Assert.Contains("await new DesktopUiCheckRunner(this).RunAsync(resultPath);", page);
        Assert.DoesNotContain("Environment.Exit", page);
        Assert.DoesNotContain("RunCommitGraphViewportLifecycleCheckAsync", page);
        Assert.Contains("private sealed partial class DesktopUiCheckRunner", runner);
        Assert.Contains("RunCommitGraphViewportLifecycleCheckAsync", graph);
        Assert.Contains("RunDesktopDensityCheckAsync", graph);
        Assert.False(File.Exists(Path.Combine(presentation, "MainPage.GraphViewportCheck.cs")));
    }

    [Fact]
    public void RepositoryTreeNavigationStateIsNotKeptInMainPage()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var page = File.ReadAllText(Path.Combine(presentation, "MainPage.xaml.cs"));
        var navigation = File.ReadAllText(Path.Combine(presentation, "Workflows", "WorkspaceNavigationController.cs"));
        var coordinator = File.ReadAllText(Path.Combine(
            root, "src", "CSharpGit.Presentation.Core", "RepositoryTreeNavigationCoordinator.cs"));

        Assert.DoesNotContain("_pendingRepositoryTreeCheckout", page);
        Assert.DoesNotContain("_pendingRepositoryTreeNavigation", page);
        Assert.False(File.Exists(Path.Combine(presentation, "MainPage.RepositoryNavigation.cs")));
        var explorer = File.ReadAllText(Path.Combine(presentation, "Views", "RepositoryExplorerView.xaml.cs"));
        var navigator = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation.Core", "WorkspaceNavigator.cs"));
        Assert.Contains("_navigator.Interactions.BeginInteraction()", explorer);
        Assert.Contains("Interactions.DeferCheckout(", navigation);
        Assert.Contains("Interactions.DeferReference(", navigator);
        Assert.Contains("TryTakeReference(", coordinator);
        Assert.Contains("TryTakeCheckout(", coordinator);
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
