namespace CSharpGit.Application.Tests;

public sealed class WorkingTreeViewCompositionContractTests
{
    [Fact]
    public void WorkingTreeVisualsLiveInDedicatedViewWithTypedEventBridge()
    {
        var root = FindRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var page = File.ReadAllText(Path.Combine(presentation, "MainPage.xaml"));
        var view = File.ReadAllText(Path.Combine(presentation, "Views", "WorkingTreeView.xaml"));
        var bridge = File.ReadAllText(Path.Combine(presentation, "Views", "WorkingTreeView.xaml.cs"));
        var pageCode = File.ReadAllText(Path.Combine(presentation, "MainPage.xaml.cs"));

        Assert.Contains("<views:WorkingTreeView x:Name=\"WorkingTreePane\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<Grid x:Name=\"WorkingTreePane\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"WorkingTreeDiffViewer\"", page, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"WorkingTreeDiffViewer\"", view, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"UnstagedChangesTree\"", view, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StagedChangesTree\"", view, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"WorkingTreeCommitEditor\"", view, StringComparison.Ordinal);
        Assert.Contains("WorkingTreePane.InitializeWorkingTreeDiffSurface();", pageCode, StringComparison.Ordinal);
        Assert.Contains("WorkingTreePane.WorkingTreeExternalDiff_ClickCallback", pageCode, StringComparison.Ordinal);
        Assert.DoesNotContain("UnstagedChangesTree_ItemInvokedCallback", bridge, StringComparison.Ordinal);
        var selection = File.ReadAllText(Path.Combine(presentation, "Views", "WorkingTreeView.Selection.cs"));
        Assert.Contains("private void UnstagedChangesTree_ItemInvoked(", selection, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CSharpGit.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("CSharpGit root not found");
    }
}
