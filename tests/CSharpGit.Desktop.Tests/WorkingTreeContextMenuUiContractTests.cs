namespace CSharpGit.Desktop.Tests;

public sealed class WorkingTreeContextMenuUiContractTests
{
    [Fact]
    public void FileRightClickPreservesExistingMultiSelection()
    {
        var root = FindRepositoryRoot();
        var contextMenu = Read(root, "src", "CSharpGit.Presentation", "MainPage.WorkingTreeContextMenu.cs");

        var selection = MethodBody(
            contextMenu,
            "private IReadOnlyList<WorkingTreeChange> SelectWorkingTreeContextTarget",
            "private bool CanDiscardStagedFile");

        Assert.Contains("if (!selection.IsSelected(node.Path))", selection, StringComparison.Ordinal);
        Assert.Contains("selection.SelectSingle(node, roots)", selection, StringComparison.Ordinal);
        Assert.Contains("GetSelectedLeaves(roots)", selection, StringComparison.Ordinal);
        Assert.Contains(".ToArray()", selection, StringComparison.Ordinal);
        Assert.Contains("_viewModel.WorkingTree.SetSelection(kind, snapshot)", selection, StringComparison.Ordinal);
        Assert.Contains("return snapshot", selection, StringComparison.Ordinal);
    }

    [Fact]
    public void FileContextActionsUseImmutableSelectionSnapshot()
    {
        var root = FindRepositoryRoot();
        var contextMenu = Read(root, "src", "CSharpGit.Presentation", "MainPage.WorkingTreeContextMenu.cs");

        var fileMenu = MethodBody(
            contextMenu,
            "private void AddWorkingTreeFileContextMenuItems",
            "private void AddWorkingTreeFolderContextMenuItems");

        Assert.Contains("IReadOnlyList<WorkingTreeChange> changes", fileMenu, StringComparison.Ordinal);
        Assert.Contains("var count = changes.Count", fileMenu, StringComparison.Ordinal);
        Assert.Contains("\"Stage\"", fileMenu, StringComparison.Ordinal);
        Assert.Contains("\"Unstage\"", fileMenu, StringComparison.Ordinal);
        Assert.Contains("\"Stash selected…\"", fileMenu, StringComparison.Ordinal);
        Assert.Contains("$\"Stash {count} files…\"", fileMenu, StringComparison.Ordinal);
        Assert.Contains("$\"Discard {count} files…\"", fileMenu, StringComparison.Ordinal);
    }

    [Fact]
    public void FolderContextMenuDoesNotGainStashFolderAction()
    {
        var root = FindRepositoryRoot();
        var contextMenu = Read(root, "src", "CSharpGit.Presentation", "MainPage.WorkingTreeContextMenu.cs");

        var folderMenu = MethodBody(
            contextMenu,
            "private void AddWorkingTreeFolderContextMenuItems",
            "private IReadOnlyList<WorkingTreeChange> SelectWorkingTreeContextTarget");

        Assert.Contains("_viewModel.WorkingTree.CanStageChanges(changes)", folderMenu, StringComparison.Ordinal);
        Assert.Contains("_viewModel.WorkingTree.CanUnstageChanges(changes)", folderMenu, StringComparison.Ordinal);
        Assert.DoesNotContain("Stash", folderMenu, StringComparison.Ordinal);
        Assert.DoesNotContain("Discard changes…", folderMenu, StringComparison.Ordinal);
    }

    [Fact]
    public void StagedDiscardRemainsSingleFileAndExplicitlyDestructive()
    {
        var root = FindRepositoryRoot();
        var contextMenu = Read(root, "src", "CSharpGit.Presentation", "MainPage.WorkingTreeContextMenu.cs");
        var viewModel = Read(root, "src", "CSharpGit.Presentation", "ViewModels", "WorkingTreeViewModel.cs");

        Assert.Contains("if (count == 1)", contextMenu, StringComparison.Ordinal);
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

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine([root, .. parts]));

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
