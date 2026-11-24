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

        Assert.Contains("new DesktopUiCheckRunner(new DesktopUiCheckContext(", page);
        Assert.DoesNotContain("Environment.Exit", page);
        Assert.DoesNotContain("RunCommitGraphViewportLifecycleCheckAsync", page);
        Assert.Contains("internal sealed partial class DesktopUiCheckRunner(DesktopUiCheckContext context)", runner);
        Assert.DoesNotContain("partial class MainPage", runner + graph);
        Assert.Contains("PropertyChanged -= ObserveBusy", runner);
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

    [Fact]
    public void Phase3FeaturePartialsAreRemovedAndHaveDedicatedOwners()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");

        foreach (var removed in new[]
                 {
                     "MainPage.CommitActions.cs",
                     "MainPage.BranchDeletion.cs",
                     "MainPage.Tags.cs",
                     "MainPage.FileOpening.cs",
                     "MainPage.RepositoryNavigation.cs",
                     "MainPage.RepositorySwitching.cs",
                     "MainPage.RepositoryTreeState.cs"
                 })
            Assert.False(File.Exists(Path.Combine(presentation, removed)), removed);

        foreach (var owner in new[]
                 {
                     "CommitActionController.cs",
                     "RepositoryReferenceMenuController.cs",
                     "FileActionInteractionController.cs",
                     "WorkspaceNavigationController.cs",
                     "RepositoryFilesActionController.cs",
                     "StashInteractionController.cs",
                     "RepositoryPullController.cs",
                     "RepositoryToolbarActionController.cs"
                 })
            Assert.True(File.Exists(Path.Combine(presentation, "Workflows", owner)), owner);
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
