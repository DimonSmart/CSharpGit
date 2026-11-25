namespace CSharpGit.Desktop.Tests;

public sealed class BranchDeletionUiContractTests
{
    [Fact]
    public void RepositoryTreeUsesConfirmedBranchDeletionWorkflow()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Views", "RepositoryExplorerView.xaml"));
        var treeState = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Composition/WindowWorkspaceComposition.Shell.cs"));
        var workflow = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Workflows", "RepositoryReferenceMenuController.cs")).ReplaceLineEndings("\n");
        var controller = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Workflows", "BranchDeletionController.cs"));

        Assert.Contains("RightTapped=\"Tree_RightTapped\"", xaml, StringComparison.Ordinal);
        var explorer = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Views", "RepositoryExplorerView.xaml.cs"));
        Assert.Contains("_referenceMenus?.Show(sender, e);", explorer, StringComparison.Ordinal);
        Assert.DoesNotContain("ReferenceMenuRequested", explorer, StringComparison.Ordinal);
        Assert.DoesNotContain("RightTapped -= RepositoryTree_RightTapped", treeState, StringComparison.Ordinal);
        Assert.DoesNotContain("RightTapped += RepositoryTreeBranchDeletion_RightTapped", treeState, StringComparison.Ordinal);
        Assert.Contains("_branchDeletion.ConfirmDeleteLocalAsync(branch)", workflow, StringComparison.Ordinal);
        Assert.Contains("Title = \"Delete local branch?\"", controller, StringComparison.Ordinal);
        Assert.Contains("!branch.IsCurrent && branchWorktree is null && !_viewModel.IsBusy", workflow, StringComparison.Ordinal);
        Assert.Contains("var forceDeleteCheckBox = new CheckBox", controller, StringComparison.Ordinal);
        Assert.Contains("Content = \"Force delete even if the branch is not fully merged\"", controller, StringComparison.Ordinal);
        Assert.Contains("Force delete may remove the only branch reference to commits. Those commits may become difficult to recover.", controller, StringComparison.Ordinal);
        Assert.Contains("forceDeleteCheckBox.IsChecked == true", controller, StringComparison.Ordinal);
        Assert.Contains("? BranchDeletionMode.Force", controller, StringComparison.Ordinal);
        Assert.Contains(": BranchDeletionMode.Safe;", controller, StringComparison.Ordinal);
        Assert.Contains("_viewModel.Branches.DeleteLocalBranchAsync(", controller, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(controller, "Force delete even if the branch is not fully merged"));
        Assert.Contains("_viewModel.Branches.ResolveRemoteDeletionTargetForLocal(branch)", controller, StringComparison.Ordinal);
        Assert.Contains("deleteRemoteCheckBox = new CheckBox", controller, StringComparison.Ordinal);
        Assert.Contains("Content = $\"Also delete remote branch '{remoteTarget.Remote.Name}/{remoteTarget.BranchName}'\"", controller, StringComparison.Ordinal);

        Assert.Contains("Title = \"Delete remote branch?\"", controller, StringComparison.Ordinal);
        Assert.Contains("Content = $\"Also delete local branch '{localBranch.Name}'\"", controller, StringComparison.Ordinal);
        Assert.Contains("Content = \"Force delete local branch even if it is not fully merged\"", controller, StringComparison.Ordinal);
        Assert.Contains("IsEnabled = !localBranch.IsCurrent", controller, StringComparison.Ordinal);
        Assert.Contains("deleteLocalCheckBox.Checked += (_, _) => forceCheckBox.IsEnabled = true;", controller, StringComparison.Ordinal);
        Assert.Contains("The local branch is currently checked out and cannot be deleted.", controller, StringComparison.Ordinal);
        Assert.Contains("_viewModel.Branches.DeleteRemoteBranchAsync(", controller, StringComparison.Ordinal);
        Assert.Contains("AddMenuItem(flyout, \"Delete\", () => !_viewModel.IsBusy,\n            () => _branchDeletion.ConfirmDeleteRemoteAsync(remoteBranch))", workflow, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(workflow, "Show branch history only"));
        Assert.Contains("_showReferenceHistory(branch.Name", workflow, StringComparison.Ordinal);
        Assert.Contains("_showReferenceHistory(remoteBranch.Name", workflow, StringComparison.Ordinal);

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
            "CSharpGit.Presentation.Core",
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
        var menu = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Workflows", "RepositoryReferenceMenuController.cs")).ReplaceLineEndings("\n");

        Assert.Contains("case RepositoryTreeNodeKind.BranchFolder when node.Value is BranchFolderInfo folderInfo:", menu, StringComparison.Ordinal);
        Assert.Contains("Delete all branches in this folder…", menu, StringComparison.Ordinal);
        Assert.Contains("Delete all remote branches in this folder…", menu, StringComparison.Ordinal);
        Assert.Contains("!_viewModel.IsBusy", menu, StringComparison.Ordinal);
        Assert.Contains("_branchDeletion.ConfirmDeleteAsync(node, folderInfo)", menu, StringComparison.Ordinal);
        Assert.DoesNotContain("case RepositoryTreeNodeKind.Group when node.Name == \"Branches\"", menu, StringComparison.Ordinal);
        Assert.Contains("case RepositoryTreeNodeKind.Remote when node.Value is GitRemote remote:", menu, StringComparison.Ordinal);
        Assert.Contains("Remove remote…", menu, StringComparison.Ordinal);
        Assert.Contains("ConfirmRemoveRemoteAsync(remote)", menu, StringComparison.Ordinal);
    }

    [Fact]
    public void BranchFolderDeletionLeavesTreeTranslationInViewAndMutationInFeature()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Workflows", "BranchDeletionController.cs"));
        var feature = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation.Core", "ViewModels", "BranchesViewModel.cs"));
        var planner = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation.Core", "ViewModels", "BranchFolderDeletionPlanning.cs"));

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
