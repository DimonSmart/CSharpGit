namespace CSharpGit.Application.Tests;

public sealed class WorkingTreeMenuOwnershipContractTests
{
    [Fact]
    public void WorkingTreeRightTapMenusHaveSingleFeatureOwner()
    {
        var root = FindRoot();
        var p = Path.Combine(root, "src", "CSharpGit.Presentation");
        var menu = File.ReadAllText(Path.Combine(p, "Views", "WorkingTreeView.ContextMenu.cs"));
        var view = File.ReadAllText(Path.Combine(p, "Views", "WorkingTreeView.xaml.cs"));
        var main = File.ReadAllText(Path.Combine(p, "Composition/WindowWorkspaceComposition.cs"));
        var selection = File.ReadAllText(Path.Combine(p, "Views", "WorkingTreeView.Selection.cs"));
        Assert.False(File.Exists(Path.Combine(p, "MainPage.WorkingTreeContextMenu.cs")));
        Assert.Contains("private void ShowWorkingTreeContextMenu(", menu, StringComparison.Ordinal);
        Assert.Contains("private void UnstagedChangesTree_RightTapped(", menu, StringComparison.Ordinal);
        Assert.Contains("private void StagedChangesTree_RightTapped(", menu, StringComparison.Ordinal);
        Assert.DoesNotContain("UnstagedChangesTree_RightTappedCallback", view, StringComparison.Ordinal);
        Assert.DoesNotContain("StagedChangesTree_RightTappedCallback", main, StringComparison.Ordinal);
        Assert.Contains("private Action<MenuFlyout, string, Func<bool>, Func<Task>> _addMenuItem", menu, StringComparison.Ordinal);
        Assert.Contains("SelectWorkingTreeContextTarget(", menu, StringComparison.Ordinal);
        Assert.Contains("internal IReadOnlyList<WorkingTreeChange> SelectWorkingTreeContextTarget(", selection, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(repository, _viewModel.Repository)", menu, StringComparison.Ordinal);
        Assert.Contains("_showDialog(dialog)", menu, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(p, "MainPage.FileOpening.cs")));
        Assert.DoesNotContain("WorkingTreeChanges_RightTapped", File.ReadAllText(Path.Combine(p, "Composition", "WindowWorkspaceComposition.Lifetime.cs")), StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CSharpGit.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("CSharpGit root not found");
    }
}
