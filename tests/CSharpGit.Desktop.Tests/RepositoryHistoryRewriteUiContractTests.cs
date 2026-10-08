namespace CSharpGit.Desktop.Tests;

public sealed class RepositoryHistoryRewriteUiContractTests
{
    [Fact]
    public void HistoricalFileMenuExposesExplicitLocalHistoryRewriteAction()
    {
        var source = ReadSource("src/CSharpGit.Presentation/MainPage.HistoryRewrite.cs");

        Assert.Contains("Remove from repository history…", source, StringComparison.Ordinal);
        Assert.Contains("entry.Kind == RepositorySnapshotEntryKind.File", source, StringComparison.Ordinal);
        Assert.Contains("Signed commits or tags may lose their cryptographic signatures.", source, StringComparison.Ordinal);
        Assert.Contains("exposed password, token or API key", source, StringComparison.Ordinal);
        Assert.Contains("Remote repositories will NOT be changed.", source, StringComparison.Ordinal);
        Assert.Contains("Publishing rewritten history is a separate operation.", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HistoryRewriteUsesExistingMutationWorkflowAndInvalidatesHistoricalPresentation()
    {
        var source = ReadSource("src/CSharpGit.Presentation/MainPage.HistoryRewrite.cs");
        var viewModel = ReadSource("src/CSharpGit.Presentation/ViewModels/OpenRepositoryViewModel.HistoryRewrite.cs");
        var repositoryFiles = ReadSource("src/CSharpGit.Presentation/ViewModels/RepositoryFilesViewModel.cs");

        Assert.Contains("_viewModel.RepositoryHistoryRewrite.RemovePathAsync", source, StringComparison.Ordinal);
        Assert.Contains("localOnlyRefresh: true", viewModel, StringComparison.Ordinal);
        Assert.Contains("_repositoryFilesViewModel.Invalidate(", source, StringComparison.Ordinal);
        Assert.Contains("CancelRequests()", repositoryFiles, StringComparison.Ordinal);
        Assert.Contains("_snapshotCache.Clear()", repositoryFiles, StringComparison.Ordinal);
        Assert.DoesNotContain("_scopedHistory", source, StringComparison.Ordinal);
        Assert.Contains("History.ResetForRepositoryMutation()", viewModel, StringComparison.Ordinal);
        Assert.Contains("CommitDetails.Invalidate()", viewModel, StringComparison.Ordinal);
        Assert.Contains("Stashes.ClearSelection()", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowCloseIsBlockedWhileDestructiveRewriteIsRunning()
    {
        var source = ReadSource("src/CSharpGit.Presentation/App.xaml.cs");

        Assert.Contains("page?.IsHistoryRewriteInProgress == true", source, StringComparison.Ordinal);
        Assert.Contains("eventArgs.Cancel = true", source, StringComparison.Ordinal);
    }

    private static string ReadSource(string relativePath) =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "CSharpGit.slnx")))
                return current.FullName;
            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}
