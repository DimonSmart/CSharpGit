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
        Assert.Contains("_viewModel.Branches.DeleteLocalBranchAsync(", workflow, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(workflow, "Force delete even if the branch is not fully merged"));
        Assert.Contains("_viewModel.Branches.ResolveRemoteDeletionTargetForLocal(branch)", workflow, StringComparison.Ordinal);
        Assert.Contains("deleteRemoteCheckBox = new CheckBox", workflow, StringComparison.Ordinal);
        Assert.Contains("Content = $\"Also delete remote branch '{remoteTarget.Remote.Name}/{remoteTarget.BranchName}'\"", workflow, StringComparison.Ordinal);

        Assert.Contains("Title = \"Delete remote branch?\"", workflow, StringComparison.Ordinal);
        Assert.Contains("Content = $\"Also delete local branch '{localBranch.Name}'\"", workflow, StringComparison.Ordinal);
        Assert.Contains("Content = \"Force delete local branch even if it is not fully merged\"", workflow, StringComparison.Ordinal);
        Assert.Contains("IsEnabled = !localBranch.IsCurrent", workflow, StringComparison.Ordinal);
        Assert.Contains("deleteLocalCheckBox.Checked += (_, _) => forceCheckBox.IsEnabled = true;", workflow, StringComparison.Ordinal);
        Assert.Contains("The local branch is currently checked out and cannot be deleted.", workflow, StringComparison.Ordinal);
        Assert.Contains("_viewModel.Branches.DeleteRemoteBranchAsync(", workflow, StringComparison.Ordinal);
        Assert.Contains("AddMenuItem(flyout, \"Delete\", !_viewModel.IsBusy, () => ConfirmDeleteRemoteBranchAsync(remoteBranch))", workflow, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(workflow, "Show branch history only"));
        Assert.Contains("ShowReferenceHistoryAsync(branch.Name", workflow, StringComparison.Ordinal);
        Assert.Contains("ShowReferenceHistoryAsync(remoteBranch.Name", workflow, StringComparison.Ordinal);

        Assert.DoesNotContain("_referenceService.DeleteBranchAsync", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("_repositorySyncService.DeleteRemoteBranchAsync", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("_viewModel.RunMutationAsync(", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void CombinedDeletionOrderingAndPartialSuccessBelongToBranchesFeature()
    {
        var root = FindRepositoryRoot();
        var feature = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CSharpGit.Presentation",
            "ViewModels",
            "BranchesViewModel.cs"));

        var localStart = feature.IndexOf("public async Task<BranchDeletionOperationResult> DeleteLocalBranchAsync", StringComparison.Ordinal);
        var remoteStart = feature.IndexOf("public async Task<BranchDeletionOperationResult> DeleteRemoteBranchAsync", StringComparison.Ordinal);
        Assert.True(localStart >= 0 && remoteStart > localStart);

        var localWorkflow = feature[localStart..remoteStart];
        var localDelete = localWorkflow.IndexOf("_referenceService.DeleteBranchAsync(", StringComparison.Ordinal);
        var remoteDelete = localWorkflow.IndexOf("_syncService.DeleteRemoteBranchAsync(", StringComparison.Ordinal);
        Assert.True(localDelete >= 0 && remoteDelete > localDelete);
        Assert.Contains("secondaryFailure = exception.Message;", localWorkflow, StringComparison.Ordinal);
        Assert.Contains("localDeleted = true;", localWorkflow, StringComparison.Ordinal);
        Assert.Contains("remoteDeleted = true;", localWorkflow, StringComparison.Ordinal);

        var remoteEnd = feature.IndexOf("internal BranchFolderDeletionPlan", remoteStart, StringComparison.Ordinal);
        Assert.True(remoteEnd > remoteStart);
        var remoteWorkflow = feature[remoteStart..remoteEnd];
        remoteDelete = remoteWorkflow.IndexOf("_syncService.DeleteRemoteBranchAsync(", StringComparison.Ordinal);
        localDelete = remoteWorkflow.IndexOf("_referenceService.DeleteBranchAsync(", StringComparison.Ordinal);
        Assert.True(remoteDelete >= 0 && localDelete > remoteDelete);
        Assert.Contains("secondaryFailure = exception.Message;", remoteWorkflow, StringComparison.Ordinal);
        Assert.Contains("remoteDeleted = true;", remoteWorkflow, StringComparison.Ordinal);
        Assert.Contains("localDeleted = true;", remoteWorkflow, StringComparison.Ordinal);
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
    public void BranchFolderDeletionLeavesTreeTranslationInViewAndMutationInFeature()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.BranchFolderDeletion.cs"));
        var feature = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "BranchesViewModel.cs"));
        var planner = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "BranchFolderDeletionPlanning.cs"));

        Assert.Contains("BranchFolderDeletionPlanner.Collect(", workflow, StringComparison.Ordinal);
        Assert.Contains("_viewModel.Branches.CreateLocalFolderDeletionPlan(snapshot)", workflow, StringComparison.Ordinal);
        Assert.Contains("_viewModel.Branches.CreateRemoteFolderDeletionTargets(folderInfo, snapshot)", workflow, StringComparison.Ordinal);
        Assert.Contains("_viewModel.Branches.DeleteLocalFolderAsync(", workflow, StringComparison.Ordinal);
        Assert.Contains("_viewModel.Branches.DeleteRemoteFolderAsync(", workflow, StringComparison.Ordinal);
        Assert.Contains("BranchFolderListMaxHeight = 360", workflow, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility = ScrollBarVisibility.Auto", workflow, StringComparison.Ordinal);
        Assert.Contains("Force delete branches even if they are not fully merged", workflow, StringComparison.Ordinal);
        Assert.Contains("Git: {failure.Message}", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("_referenceService.DeleteBranchAsync", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("_repositorySyncService.DeleteRemoteBranchAsync", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("RunMutationAsync(", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("BranchFolderDeletionExecutor.ExecuteAsync", workflow, StringComparison.Ordinal);

        Assert.Contains("BranchFolderDeletionExecutor.ExecuteAsync(", feature, StringComparison.Ordinal);
        Assert.Contains("_referenceService.DeleteBranchAsync(", feature, StringComparison.Ordinal);
        Assert.Contains("_syncService.DeleteRemoteBranchAsync(", feature, StringComparison.Ordinal);
        Assert.Contains("successfulBranches.Add(branchName)", planner, StringComparison.Ordinal);
        Assert.Contains("catch (Exception exception) when (exception is not OperationCanceledException)", planner, StringComparison.Ordinal);
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
