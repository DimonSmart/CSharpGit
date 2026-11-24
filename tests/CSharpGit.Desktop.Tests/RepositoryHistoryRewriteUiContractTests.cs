namespace CSharpGit.Desktop.Tests;

public sealed class RepositoryHistoryRewriteUiContractTests
{
    [Fact]
    public void HistoricalFileMenuExposesExplicitLocalHistoryRewriteAction()
    {
        var source = ReadSource("src/CSharpGit.Presentation/Workflows/RepositoryFilesActionController.cs");
        var workflow = ReadSource("src/CSharpGit.Presentation/Workflows/HistoryRewriteController.cs");

        Assert.Contains("Remove from repository history…", source, StringComparison.Ordinal);
        Assert.Contains("entry.Kind == RepositorySnapshotEntryKind.File", source, StringComparison.Ordinal);
        Assert.Contains("Signed commits or tags may lose their cryptographic signatures.", workflow, StringComparison.Ordinal);
        Assert.Contains("exposed password, token or API key", workflow, StringComparison.Ordinal);
        Assert.Contains("Remote repositories will NOT be changed.", workflow, StringComparison.Ordinal);
        Assert.Contains("Publishing rewritten history is a separate operation.", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void HistoryRewriteUsesExistingMutationWorkflowAndInvalidatesHistoricalPresentation()
    {
        var source = ReadSource("src/CSharpGit.Presentation/Workflows/RepositoryFilesActionController.cs");
        var workflow = ReadSource("src/CSharpGit.Presentation/Workflows/HistoryRewriteController.cs");
        var viewModel = ReadSource("src/CSharpGit.Presentation/ViewModels/OpenRepositoryViewModel.HistoryRewrite.cs");
        var repositoryFiles = ReadSource("src/CSharpGit.Presentation.Core/ViewModels/RepositoryFilesViewModel.cs");

        Assert.Contains("_viewModel.RepositoryHistoryRewrite.RemovePathAsync", workflow, StringComparison.Ordinal);
        Assert.Contains("_historyRewrite.RemovePathAsync(entry)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("InstallRepositoryHistoryRewriteMenus", ReadSource("src/CSharpGit.Presentation/Composition/WindowWorkspaceComposition.Repository.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("ShutdownRepositoryHistoryRewriteMenus", ReadSource("src/CSharpGit.Presentation/Composition/WindowWorkspaceComposition.Lifetime.cs"), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(FindRepositoryRoot(), "src/CSharpGit.Presentation/MainPage.HistoryRewrite.cs")));
        Assert.Contains("localOnlyRefresh: true", viewModel, StringComparison.Ordinal);
        Assert.Contains("_repositoryFilesViewModel.Invalidate(", workflow, StringComparison.Ordinal);
        Assert.Contains("CancelRequests()", repositoryFiles, StringComparison.Ordinal);
        Assert.Contains("_snapshotCache.Clear()", repositoryFiles, StringComparison.Ordinal);
        Assert.DoesNotContain("_scopedHistory", workflow, StringComparison.Ordinal);
        Assert.Contains("History.ResetForRepositoryMutation()", viewModel, StringComparison.Ordinal);
        Assert.Contains("CommitDetails.Invalidate()", viewModel, StringComparison.Ordinal);
        Assert.Contains("Stashes.ClearSelection()", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowCloseIsBlockedWhileDestructiveRewriteIsRunning()
    {
        var source = ReadSource("src/CSharpGit.Presentation/App.xaml.cs");

        var safety = ReadSource("src/CSharpGit.Presentation/Windowing/WindowCloseSafetyProvider.cs");
        var close = ReadSource("src/CSharpGit.Presentation/Windowing/WindowCloseOrchestrator.cs");
        Assert.Contains("_windowClose?.RequestClose()", source, StringComparison.Ordinal);
        Assert.Contains("session.IsUnsafeToClose", close, StringComparison.Ordinal);
        Assert.Contains("IsHistoryRewriteRunning: repository.RepositoryHistoryRewrite.IsInProgress", safety, StringComparison.Ordinal);
        Assert.Contains("eventArgs.Cancel = true", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SubmoduleMenuIsPreservedInUnifiedRepositoryTreeContextMenu()
    {
        var history = ReadSource("src/CSharpGit.Presentation/Controls/RepositoryFilesSurface.Interactions.cs");
        var files = ReadSource("src/CSharpGit.Presentation/Composition/WindowWorkspaceComposition.Files.cs");
        var submodules = ReadSource("src/CSharpGit.Presentation/Workflows/SubmoduleWorkflowController.cs");
        var clone = ReadSource("src/CSharpGit.Presentation/Workflows/RepositoryCloneWorkflow.cs");

        Assert.Contains("entry.Kind == RepositorySnapshotEntryKind.Submodule", history, StringComparison.Ordinal);
        Assert.Contains("_showSubmoduleMenu(source, entry)", history, StringComparison.Ordinal);
        Assert.Contains("_showFileMenu(source, entry)", history, StringComparison.Ordinal);
        Assert.Contains("OpenSelectedSubmoduleAsync(entry)", files, StringComparison.Ordinal);
        Assert.Contains("Initialize submodule", submodules, StringComparison.Ordinal);
        Assert.Contains("Update All Submodules", submodules, StringComparison.Ordinal);
        Assert.Contains("Content = \"Initialize submodules\"", clone, StringComparison.Ordinal);
        Assert.Contains("initializeSubmodulesBox.IsEnabled = !busy", clone, StringComparison.Ordinal);
        Assert.Contains("_cloneRepositoryViewModel.InitializeSubmodules = true", clone, StringComparison.Ordinal);
        Assert.Contains("_cloneRepositoryViewModel.InitializeSubmodules = false", clone, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(FindRepositoryRoot(),
            "src/CSharpGit.Presentation/MainPage.RepositoryCloning.cs")));
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
