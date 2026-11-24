namespace CSharpGit.Application.Tests;

public sealed class HistoryWorkspaceCompositionContractTests
{
    [Fact]
    public void HistoryAndCommitDetailsHaveTheirOwnVisualRoot()
    {
        var root = FindRoot();
        var p = Path.Combine(root, "src", "CSharpGit.Presentation");
        var main = File.ReadAllText(Path.Combine(p, "MainPage.xaml"));
        var history = File.ReadAllText(Path.Combine(p, "Views", "HistoryWorkspaceView.xaml"));
        var callbacks = File.ReadAllText(Path.Combine(p, "Views", "HistoryWorkspaceView.xaml.cs"));
        var code = File.ReadAllText(Path.Combine(p, "MainPage.xaml.cs"));

        Assert.Contains("<views:HistoryWorkspaceView x:Name=\"HistoryPane\"", main, StringComparison.Ordinal);
        Assert.DoesNotContain("<Grid x:Name=\"HistoryPane\"", main, StringComparison.Ordinal);
        foreach (var control in new[] { "HistoryView", "DetailsTabs", "CommitDetailsContent", "ChangedFilesTree", "CompactDiffViewer" })
        {
            Assert.DoesNotContain($"x:Name=\"{control}\"", main, StringComparison.Ordinal);
            Assert.Contains($"x:Name=\"{control}\"", history, StringComparison.Ordinal);
        }
        Assert.Contains("HistoryPane.ConfigureChanges(", code, StringComparison.Ordinal);
        var changes = File.ReadAllText(Path.Combine(presentation, "Views", "HistoryWorkspaceView.Changes.cs"));
        Assert.Contains("private void ChangesSurface_Loaded(", changes, StringComparison.Ordinal);
        Assert.Contains("private void ChangedFilesTree_ItemInvoked(", changes, StringComparison.Ordinal);
        Assert.Contains("Action<TreeView, TreeViewItemInvokedEventArgs>", callbacks, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CSharpGit.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("CSharpGit root not found");
    }
}
