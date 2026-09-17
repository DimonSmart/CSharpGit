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

        Assert.Contains("_viewModel.RunMutationAsync", source, StringComparison.Ordinal);
        Assert.Contains("CancelRepositoryFilesRequests()", source, StringComparison.Ordinal);
        Assert.Contains("_repositorySnapshotCache.Clear()", source, StringComparison.Ordinal);
        Assert.Contains("_scopedHistory.Clear()", source, StringComparison.Ordinal);
        Assert.Contains("InvalidateHistoryLoad()", viewModel, StringComparison.Ordinal);
        Assert.Contains("ResetCommitChangesSession()", viewModel, StringComparison.Ordinal);
        Assert.Contains("History.Clear()", viewModel, StringComparison.Ordinal);
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
