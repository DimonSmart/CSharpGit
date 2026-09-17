namespace CSharpGit.Desktop.Tests;

public sealed class BranchRenameUiContractTests
{
    [Fact]
    public void LocalBranchMenuUsesSharedRenameWorkflowWithoutCurrentOrWorktreeRestriction()
    {
        var root = FindRepositoryRoot();
        var menu = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.BranchDeletion.cs"));
        var workflow = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.BranchRename.cs"));

        Assert.Contains("AddMenuItem(flyout, \"Rename…\", CanRenameBranch(), () => RenameBranchAsync(branch));", menu, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(menu, "\"Rename…\""));
        Assert.Contains("private bool CanRenameBranch() =>", workflow, StringComparison.Ordinal);
        Assert.Contains("!_viewModel.IsBusy", workflow, StringComparison.Ordinal);
        Assert.Contains("_viewModel.Repository is not null", workflow, StringComparison.Ordinal);
        Assert.Contains("_viewModel.CurrentOperation == RepositoryOperation.None", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("branch.IsCurrent", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("FindWorktreeForBranch", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void F2IsScopedToRepositoryTreeAndUsesItsSelectedLocalBranch()
    {
        var root = FindRepositoryRoot();
        var lifecycle = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.Lifecycle.cs"));
        var workflow = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.BranchRename.cs"));

        Assert.Contains("InitializeBranchRename();", lifecycle, StringComparison.Ordinal);
        Assert.Contains("RepositoryTree.KeyDown += RepositoryTree_KeyDown;", workflow, StringComparison.Ordinal);
        Assert.Contains("e.Key != VirtualKey.F2", workflow, StringComparison.Ordinal);
        Assert.Contains("ResolveNode(RepositoryTree.SelectedItem)", workflow, StringComparison.Ordinal);
        Assert.Contains("RepositoryTreeNodeKind.LocalBranch", workflow, StringComparison.Ordinal);
        Assert.Contains("node.Value is not GitBranch branch", workflow, StringComparison.Ordinal);
        Assert.Contains("await RenameBranchAsync(branch);", workflow, StringComparison.Ordinal);
        Assert.Contains("e.Handled = true;", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("_viewModel.SelectedLocalBranch", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void RenameUsesMutationPipelineAndMovesScopedReferenceBeforeRefreshCompletes()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.BranchRename.cs"));

        Assert.Contains("Title = \"Rename branch\"", workflow, StringComparison.Ordinal);
        Assert.Contains("Header = \"Name\"", workflow, StringComparison.Ordinal);
        Assert.Contains("Text = oldName", workflow, StringComparison.Ordinal);
        Assert.Contains("dialog.IsPrimaryButtonEnabled", workflow, StringComparison.Ordinal);
        Assert.Contains("nameBox.Text.Trim()", workflow, StringComparison.Ordinal);
        Assert.Contains("_viewModel.RunMutationAsync(", workflow, StringComparison.Ordinal);
        Assert.Contains("_referenceService.RenameBranchAsync(", workflow, StringComparison.Ordinal);
        Assert.Contains("\"Could not rename local branch\"", workflow, StringComparison.Ordinal);
        Assert.Contains("string.Equals(_activeReference, oldName, StringComparison.Ordinal)", workflow, StringComparison.Ordinal);
        Assert.Contains("_activeReference = newName;", workflow, StringComparison.Ordinal);
        Assert.Contains("ActiveReferenceText.Text = $\"Branch: {newName}\";", workflow, StringComparison.Ordinal);
        Assert.Contains("QueueWorktreeRefresh();", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("git branch", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".git/refs", workflow, StringComparison.OrdinalIgnoreCase);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
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
