namespace CSharpGit.Desktop.Tests;

public sealed class WorkspaceNavigationArchitectureTests
{
    [Fact]
    public void NavigatorHasNoVisualOrDialogDependenciesAndRejectsStaleSelections()
    {
        var core = Read("src/CSharpGit.Presentation.Core/WorkspaceNavigator.cs");
        Assert.DoesNotContain("Microsoft.UI.Xaml", core, StringComparison.Ordinal);
        Assert.DoesNotContain("TreeView", core, StringComparison.Ordinal);
        Assert.DoesNotContain("ScrollViewer", core, StringComparison.Ordinal);
        Assert.DoesNotContain("ContentDialog", core, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(_context.Repository, repository)", core, StringComparison.Ordinal);
        Assert.Contains("Interactions.Version", core, StringComparison.Ordinal);
        Assert.Contains("_history.SelectionIntentVersion == target.SelectionIntentVersion", core, StringComparison.Ordinal);
        Assert.Contains("Interactions.DeferReference(", core, StringComparison.Ordinal);
        Assert.Contains("WorkspaceSurface.WorkingTree", core, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplorerOwnsIncrementalReconciliationAndExplicitDisposal()
    {
        var explorer = Read("src/CSharpGit.Presentation/Views/RepositoryExplorerView.xaml.cs");
        var page = Read("src/CSharpGit.Presentation/MainPage.RepositoryTreeState.cs");
        Assert.Contains("new RepositoryTreeSynchronizer(_roots)", explorer, StringComparison.Ordinal);
        Assert.Contains("_synchronizer.ReconcileRepository(", explorer, StringComparison.Ordinal);
        Assert.Contains("_synchronizer.ReconcileWorktrees(", explorer, StringComparison.Ordinal);
        Assert.Contains("RepositoryTreeSelection.Capture(", explorer, StringComparison.Ordinal);
        Assert.Contains("RepositoryTreeSelection.Resolve(", explorer, StringComparison.Ordinal);
        Assert.Contains("Tree.Expanding -= Tree_Expanding;", explorer, StringComparison.Ordinal);
        Assert.Contains("_repository.PropertyChanged -= Repository_PropertyChanged;", explorer, StringComparison.Ordinal);
        Assert.DoesNotContain("_repositoryTreeRoots", page, StringComparison.Ordinal);
        Assert.Contains("RepositoryExplorer.Shutdown();", page, StringComparison.Ordinal);
    }

    private static string Read(string path)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "CSharpGit.slnx")))
            current = current.Parent;
        return File.ReadAllText(Path.Combine(current!.FullName,
            path.Replace('/', Path.DirectorySeparatorChar)));
    }
}
