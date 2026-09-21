namespace CSharpGit.Desktop.Tests;

public sealed class WorkingTreeContextMenuUiContractTests
{
    [Fact]
    public void WorkingTreeFileRowsExposeContextActions()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var contextMenu = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.WorkingTreeContextMenu.cs"));

        Assert.Contains("RightTapped=\"UnstagedChangesTree_RightTapped\"", xaml, StringComparison.Ordinal);
        Assert.Contains("RightTapped=\"StagedChangesTree_RightTapped\"", xaml, StringComparison.Ordinal);
        Assert.Contains("\"Stage\"", contextMenu, StringComparison.Ordinal);
        Assert.Contains("\"Unstage\"", contextMenu, StringComparison.Ordinal);
        Assert.Contains("\"Discard changes…\"", contextMenu, StringComparison.Ordinal);
        Assert.Contains("selection.SelectSingle(node, roots)", contextMenu, StringComparison.Ordinal);
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
