namespace CSharpGit.Desktop.Tests;

public sealed class WorkingTreeContextMenuUiContractTests
{
    [Fact]
    public void WorkingTreeFileAndFolderRowsExposeContextActions()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var contextMenu = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.WorkingTreeContextMenu.cs"));

        Assert.Contains("RightTapped=\"UnstagedChangesTree_RightTapped\"", xaml, StringComparison.Ordinal);
        Assert.Contains("RightTapped=\"StagedChangesTree_RightTapped\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowWorkingTreeContextMenu", contextMenu, StringComparison.Ordinal);
        Assert.Contains("node.GetDescendantChanges()", contextMenu, StringComparison.Ordinal);
        Assert.Contains("_viewModel.CanStageChanges(changes)", contextMenu, StringComparison.Ordinal);
        Assert.Contains("_viewModel.CanUnstageChanges(changes)", contextMenu, StringComparison.Ordinal);
        Assert.Contains("Could not stage folder", contextMenu, StringComparison.Ordinal);
        Assert.Contains("Could not unstage folder", contextMenu, StringComparison.Ordinal);
        Assert.Contains("selection.SelectSingle(node, roots)", contextMenu, StringComparison.Ordinal);

        var folderMenu = MethodBody(
            contextMenu,
            "private void AddWorkingTreeFolderContextMenuItems",
            "private void SelectWorkingTreeContextTarget");
        Assert.Contains("\"Stage\"", folderMenu, StringComparison.Ordinal);
        Assert.Contains("\"Unstage\"", folderMenu, StringComparison.Ordinal);
        Assert.DoesNotContain("Discard changes…", folderMenu, StringComparison.Ordinal);
        Assert.DoesNotContain("StageSelectedCommand", folderMenu, StringComparison.Ordinal);
        Assert.DoesNotContain("UnstageSelectedCommand", folderMenu, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectWorkingTreeContextTarget", folderMenu, StringComparison.Ordinal);
    }

    [Fact]
    public void FolderBatchWorkflowUsesExplicitChangesWithoutReplacingSelection()
    {
        var root = FindRepositoryRoot();
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.cs"));
        var contextMenu = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.WorkingTreeContextMenu.cs"));

        Assert.Contains("internal bool CanStageChanges", viewModel, StringComparison.Ordinal);
        Assert.Contains("internal bool CanUnstageChanges", viewModel, StringComparison.Ordinal);
        Assert.Contains("_workingTreeService.StageFilesAsync(Repository!, snapshot)", viewModel, StringComparison.Ordinal);
        Assert.Contains("_workingTreeService.UnstageFilesAsync(Repository!, snapshot)", viewModel, StringComparison.Ordinal);
        Assert.Contains("await StageChangesAsync(changes, \"Could not stage selected files\")", viewModel, StringComparison.Ordinal);
        Assert.Contains("await UnstageChangesAsync(changes, \"Could not unstage selected files\")", viewModel, StringComparison.Ordinal);

        var folderBranch = MethodBody(
            contextMenu,
            "private void AddWorkingTreeFolderContextMenuItems",
            "private void SelectWorkingTreeContextTarget");
        Assert.DoesNotContain("SetWorkingTreeSelection", folderBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectWorkingTreeChange", folderBranch, StringComparison.Ordinal);
    }

    [Fact]
    public void StagedDiscardIsExplicitAndDestructive()
    {
        var root = FindRepositoryRoot();
        var contextMenu = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.WorkingTreeContextMenu.cs"));
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.Discard.cs"));

        Assert.Contains("ConfirmDiscardStagedFileAsync", contextMenu, StringComparison.Ordinal);
        Assert.Contains("This file also has unstaged changes", contextMenu, StringComparison.Ordinal);
        Assert.Contains("PrimaryButtonText = \"Discard\"", contextMenu, StringComparison.Ordinal);
        Assert.Contains("DefaultButton = ContentDialogButton.Close", contextMenu, StringComparison.Ordinal);
        Assert.Contains("DiscardAllFileChangesAsync", viewModel, StringComparison.Ordinal);
    }

    private static string MethodBody(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate CSharpGit repository root.");
    }
}
