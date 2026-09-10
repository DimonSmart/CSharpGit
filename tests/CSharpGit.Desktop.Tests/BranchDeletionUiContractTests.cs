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
        Assert.Contains("Title = \"Delete remote branch?\"", workflow, StringComparison.Ordinal);
        Assert.Contains("Content = $\"Also delete local branch '{localBranch.Name}'\"", workflow, StringComparison.Ordinal);
        Assert.Contains("IsChecked = false", workflow, StringComparison.Ordinal);
        Assert.Contains("IsEnabled = !localBranch.IsCurrent", workflow, StringComparison.Ordinal);
        Assert.Contains("The local branch is currently checked out and cannot be deleted.", workflow, StringComparison.Ordinal);
        Assert.Contains("AddMenuItem(flyout, \"Delete\", !_viewModel.IsBusy, () => ConfirmDeleteRemoteBranchAsync(remoteBranch))", workflow, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(workflow, "Show branch history only"));
        Assert.Contains("ShowReferenceHistoryAsync(branch.Name", workflow, StringComparison.Ordinal);
        Assert.Contains("ShowReferenceHistoryAsync(remoteBranch.Name", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void CombinedDeletionDeletesRemoteBeforeLocalAndReportsPartialSuccess()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.BranchDeletion.cs"));

        var remoteDelete = workflow.IndexOf("DeleteRemoteBranchAsync(", StringComparison.Ordinal);
        var localDelete = workflow.IndexOf("DeleteBranchAsync(_viewModel.Repository!, target.LocalBranch.Name)", StringComparison.Ordinal);
        Assert.True(remoteDelete >= 0 && localDelete > remoteDelete);
        Assert.Contains("catch (Exception exception) when (exception is not OperationCanceledException)", workflow, StringComparison.Ordinal);
        Assert.Contains("Remote branch '{remoteBranch.Name}' was deleted, but local branch '{retainedLocalBranch.Name}' could not be deleted.", workflow, StringComparison.Ordinal);
        Assert.Contains("ShowAllHistory();", workflow, StringComparison.Ordinal);
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
