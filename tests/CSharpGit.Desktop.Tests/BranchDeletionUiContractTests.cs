namespace CSharpGit.Desktop.Tests;

public sealed class BranchDeletionUiContractTests
{
    [Fact]
    public void RepositoryTreeUsesConfirmedBranchDeletionWorkflow()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var treeState = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.RepositoryTreeState.cs"));
        var workflow = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.BranchDeletion.cs"));

        Assert.Contains("RightTapped=\"RepositoryTreeBranchDeletion_RightTapped\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RightTapped -= RepositoryTree_RightTapped", treeState, StringComparison.Ordinal);
        Assert.DoesNotContain("RightTapped += RepositoryTreeBranchDeletion_RightTapped", treeState, StringComparison.Ordinal);
        Assert.Contains("ConfirmDeleteLocalBranchAsync(branch)", workflow, StringComparison.Ordinal);
        Assert.Contains("Title = \"Delete local branch?\"", workflow, StringComparison.Ordinal);
        Assert.Contains("!branch.IsCurrent && branchWorktree is null && !_viewModel.IsBusy", workflow, StringComparison.Ordinal);
        Assert.Contains("var forceDeleteCheckBox = new CheckBox", workflow, StringComparison.Ordinal);
        Assert.Contains("Content = \"Force delete even if the branch is not fully merged\"", workflow, StringComparison.Ordinal);
        Assert.Contains("Force delete may remove the only branch reference to commits. Those commits may become difficult to recover.", workflow, StringComparison.Ordinal);
        Assert.Contains("forceDeleteCheckBox.IsChecked == true", workflow, StringComparison.Ordinal);
        Assert.Contains("? BranchDeletionMode.Force", workflow, StringComparison.Ordinal);
        Assert.Contains(": BranchDeletionMode.Safe;", workflow, StringComparison.Ordinal);
        Assert.Contains("DeleteBranchAsync(_viewModel.Repository!, branch.Name, deletionMode)", workflow, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(workflow, "Force delete even if the branch is not fully merged"));

        Assert.Contains("Title = \"Delete remote branch?\"", workflow, StringComparison.Ordinal);
        Assert.Contains("Content = $\"Also delete local branch '{localBranch.Name}'\"", workflow, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(workflow, "IsChecked = false"));
        Assert.Contains("IsEnabled = !localBranch.IsCurrent", workflow, StringComparison.Ordinal);
        Assert.Contains("The local branch is currently checked out and cannot be deleted.", workflow, StringComparison.Ordinal);
        Assert.Contains("AddMenuItem(flyout, \"Delete\", !_viewModel.IsBusy, () => ConfirmDeleteRemoteBranchAsync(remoteBranch))", workflow, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(workflow, "Show branch history only"));
        Assert.Contains("ShowReferenceHistoryAsync(branch.Name", workflow, StringComparison.Ordinal);
        Assert.Contains("ShowReferenceHistoryAsync(remoteBranch.Name", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void CombinedDeletionDeletesRemoteBeforeSafeLocalAndReportsPartialSuccess()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.BranchDeletion.cs"));

        var remoteDelete = workflow.IndexOf("DeleteRemoteBranchAsync(", StringComparison.Ordinal);
        var localDelete = workflow.IndexOf("target.LocalBranch.Name,", StringComparison.Ordinal);
        var safeMode = localDelete < 0
            ? -1
            : workflow.IndexOf("BranchDeletionMode.Safe);", localDelete, StringComparison.Ordinal);
        Assert.True(remoteDelete >= 0 && localDelete > remoteDelete && safeMode > localDelete);
        Assert.Contains("catch (Exception exception) when (exception is not OperationCanceledException)", workflow, StringComparison.Ordinal);
        Assert.Contains("Remote branch '{remoteBranch.Name}' was deleted, but local branch '{retainedLocalBranch.Name}' could not be deleted.", workflow, StringComparison.Ordinal);
        Assert.Contains("ShowAllHistory();", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void BranchFolderContextMenuUsesStronglyTypedMetadataAndScopedLabels()
    {
        var root = FindRepositoryRoot();
        var menu = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.BranchDeletion.cs"));

        Assert.Contains("case RepositoryTreeNodeKind.BranchFolder when node.Value is BranchFolderInfo folderInfo:", menu, StringComparison.Ordinal);
        Assert.Contains("Delete all branches in this folder…", menu, StringComparison.Ordinal);
        Assert.Contains("Delete all remote branches in this folder…", menu, StringComparison.Ordinal);
        Assert.Contains("!_viewModel.IsBusy", menu, StringComparison.Ordinal);
        Assert.Contains("ConfirmDeleteBranchFolderAsync(node, folderInfo)", menu, StringComparison.Ordinal);
        Assert.DoesNotContain("case RepositoryTreeNodeKind.Group when node.Name == \"Branches\"", menu, StringComparison.Ordinal);
        Assert.DoesNotContain("case RepositoryTreeNodeKind.Remote when node.Value is GitRemote", menu, StringComparison.Ordinal);
    }

    [Fact]
    public void BranchFolderDeletionUsesOneMutationPerOperationAndBoundedConfirmationLists()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.BranchFolderDeletion.cs"));
        var planner = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "BranchFolderDeletionPlanning.cs"));

        Assert.Equal(2, CountOccurrences(workflow, "RunMutationAsync("));
        Assert.Contains("BranchDeletionMode.Safe", workflow, StringComparison.Ordinal);
        Assert.Contains("BranchDeletionMode.Force", workflow, StringComparison.Ordinal);
        Assert.Contains("Force delete branches even if they are not fully merged", workflow, StringComparison.Ordinal);
        Assert.Contains("IsChecked = false", workflow, StringComparison.Ordinal);
        Assert.Contains("PrimaryButtonText = $\"Delete {plan.Attempted} branches\"", workflow, StringComparison.Ordinal);
        Assert.Contains("current branch", workflow, StringComparison.Ordinal);
        Assert.Contains("used by worktree", workflow, StringComparison.Ordinal);
        Assert.Contains("BranchFolderListMaxHeight = 360", workflow, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility = ScrollBarVisibility.Auto", workflow, StringComparison.Ordinal);
        Assert.Contains("DeleteBranchAsync(", workflow, StringComparison.Ordinal);
        Assert.Contains("DeleteRemoteBranchAsync(", workflow, StringComparison.Ordinal);
        Assert.Contains("target.RelativeBranchName", workflow, StringComparison.Ordinal);
        Assert.Contains("if (!mutationSucceeded || executionResult is null || executionResult.Failures.Count == 0)", workflow, StringComparison.Ordinal);
        Assert.Contains("Git: {failure.Message}", workflow, StringComparison.Ordinal);
        Assert.Contains("successfulBranches.Add(branchName)", planner, StringComparison.Ordinal);
        Assert.Contains("catch (Exception exception) when (exception is not OperationCanceledException)", planner, StringComparison.Ordinal);
        Assert.DoesNotContain("BranchDeletionResolver", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("Split('/')", workflow, StringComparison.Ordinal);

        var remoteSection = workflow[workflow.IndexOf("private async Task ConfirmDeleteRemoteBranchFolderAsync", StringComparison.Ordinal)..];
        Assert.DoesNotContain("Force delete branches even if they are not fully merged", remoteSection, StringComparison.Ordinal);
        Assert.DoesNotContain("Also delete local branch", remoteSection, StringComparison.Ordinal);
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
