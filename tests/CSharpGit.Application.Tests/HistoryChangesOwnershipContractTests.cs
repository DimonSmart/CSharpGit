namespace CSharpGit.Application.Tests;

public sealed class HistoryChangesOwnershipContractTests
{
    [Fact]
    public void CommitChangesAreOwnedAndShutDownByHistoryWorkspace()
    {
        var root = FindRoot();
        var p = Path.Combine(root, "src", "CSharpGit.Presentation");
        var changes = File.ReadAllText(Path.Combine(p, "Views", "HistoryWorkspaceView.Changes.cs"));
        var main = File.ReadAllText(Path.Combine(p, "Composition/WindowWorkspaceComposition.cs"));
        var lifetime = File.ReadAllText(Path.Combine(p, "Composition/WindowWorkspaceComposition.Lifetime.cs"));
        Assert.False(File.Exists(Path.Combine(p, "MainPage.Changes.cs")));
        Assert.Contains("HistoryPane.ConfigureChanges(", main, StringComparison.Ordinal);
        Assert.Contains("HistoryPane.ShutdownChanges();", lifetime, StringComparison.Ordinal);
        Assert.Contains("_viewModel.CommitDetails.PropertyChanged += ChangesViewModel_PropertyChanged", changes, StringComparison.Ordinal);
        Assert.Contains("_viewModel.CommitDetails.PropertyChanged -= ChangesViewModel_PropertyChanged", changes, StringComparison.Ordinal);
        Assert.Contains("_commitSummaryViewport?.Dispose()", changes, StringComparison.Ordinal);
        Assert.Contains("_changesTree?.Shutdown()", changes, StringComparison.Ordinal);
        Assert.DoesNotContain("_commitSummaryViewport", main, StringComparison.Ordinal);
        Assert.DoesNotContain("ChangesViewModel_PropertyChanged", lifetime, StringComparison.Ordinal);
        Assert.Contains("PropertyChanged += CommitDetailsProjection_PropertyChanged", changes, StringComparison.Ordinal);
        Assert.Contains("PropertyChanged -= CommitDetailsProjection_PropertyChanged", changes, StringComparison.Ordinal);
        Assert.Contains("private Task RefreshCommitFilesAsync()", changes, StringComparison.Ordinal);
        Assert.DoesNotContain("_commitFiles", main, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshCommitFilesAsync", main, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "CSharpGit.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException("CSharpGit root not found");
    }
}
