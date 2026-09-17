namespace CSharpGit.Desktop.Tests;

public sealed class BranchDragDropUiContractTests
{
    [Fact]
    public void RepositoryTreeUsesCustomMoveDragDropWithoutGenericReorder()
    {
        var root = FindRepositoryRoot();
        var lifecycle = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.Lifecycle.cs"));
        var dragDrop = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.BranchDragDrop.cs"));

        Assert.Contains("InitializeBranchDragDrop();", lifecycle, StringComparison.Ordinal);
        Assert.Contains("RepositoryTree.CanDragItems = true;", dragDrop, StringComparison.Ordinal);
        Assert.Contains("RepositoryTree.AllowDrop = true;", dragDrop, StringComparison.Ordinal);
        Assert.Contains("RepositoryTree.CanReorderItems = false;", dragDrop, StringComparison.Ordinal);
        Assert.Contains("RepositoryTree.DragItemsStarting += RepositoryTree_DragItemsStarting;", dragDrop, StringComparison.Ordinal);
        Assert.Contains("RepositoryTree.DragOver += RepositoryTree_DragOver;", dragDrop, StringComparison.Ordinal);
        Assert.Contains("RepositoryTree.Drop += RepositoryTree_Drop;", dragDrop, StringComparison.Ordinal);
        Assert.Contains("RepositoryTree.DragItemsCompleted += RepositoryTree_DragItemsCompleted;", dragDrop, StringComparison.Ordinal);
    }

    [Fact]
    public void DragSourceComesFromDraggedItemAndIsCapturedForTheOperation()
    {
        var dragDrop = ReadDragDrop();

        Assert.Contains("args.Items.Count == 1", dragDrop, StringComparison.Ordinal);
        Assert.Contains("ResolveNode(args.Items[0])", dragDrop, StringComparison.Ordinal);
        Assert.Contains("BranchMovePlanning.GetSourceBranch(node.Kind, node.Value)", dragDrop, StringComparison.Ordinal);
        Assert.Contains("_branchDragSource = branch;", dragDrop, StringComparison.Ordinal);
        Assert.Contains("args.Cancel = true;", dragDrop, StringComparison.Ordinal);
        Assert.Contains("!CanRenameBranch()", dragDrop, StringComparison.Ordinal);
        Assert.DoesNotContain("RepositoryTree.SelectedItem", dragDrop, StringComparison.Ordinal);
        Assert.DoesNotContain("_viewModel.SelectedLocalBranch", dragDrop, StringComparison.Ordinal);
    }

    [Fact]
    public void DragTargetComesFromPointerAndOnlyEligibleTargetsAcceptMove()
    {
        var dragDrop = ReadDragDrop();

        Assert.Contains("args.OriginalSource as FrameworkElement", dragDrop, StringComparison.Ordinal);
        Assert.Contains("BranchMovePlanning.TryGetDestinationPrefix(", dragDrop, StringComparison.Ordinal);
        Assert.Contains("DataPackageOperation.Move", dragDrop, StringComparison.Ordinal);
        Assert.Contains("DataPackageOperation.None", dragDrop, StringComparison.Ordinal);
        Assert.DoesNotContain("RepositoryTree.SelectedItem", dragDrop, StringComparison.Ordinal);
    }

    [Fact]
    public void DropRechecksMutationStateAndUsesSharedRenameWorkflow()
    {
        var root = FindRepositoryRoot();
        var dragDrop = ReadDragDrop();
        var rename = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.BranchRename.cs"));

        Assert.Contains("if (sourceBranch is null || target is null || !CanRenameBranch()) return;", dragDrop, StringComparison.Ordinal);
        Assert.Contains("BranchMovePlanning.BuildTargetName(sourceBranch, destinationPrefix)", dragDrop, StringComparison.Ordinal);
        Assert.Contains("string.Equals(sourceBranch.Name, newName, StringComparison.Ordinal)", dragDrop, StringComparison.Ordinal);
        Assert.Contains("await ExecuteBranchRenameAsync(sourceBranch, newName);", dragDrop, StringComparison.Ordinal);

        Assert.Contains("await ExecuteBranchRenameAsync(branch, newName);", rename, StringComparison.Ordinal);
        Assert.Contains("private async Task ExecuteBranchRenameAsync(GitBranch branch, string newName)", rename, StringComparison.Ordinal);
        Assert.Contains("string.Equals(oldName, newName, StringComparison.Ordinal)", rename, StringComparison.Ordinal);
        Assert.Contains("_viewModel.RunMutationAsync(", rename, StringComparison.Ordinal);
        Assert.Contains("_referenceService.RenameBranchAsync(", rename, StringComparison.Ordinal);
        Assert.Contains("_activeReference = newName;", rename, StringComparison.Ordinal);
        Assert.Contains("QueueWorktreeRefresh();", rename, StringComparison.Ordinal);
    }

    [Fact]
    public void DragDropNeverReparentsRepositoryTreeNodesManually()
    {
        var dragDrop = ReadDragDrop();

        Assert.DoesNotContain(".Children.Remove", dragDrop, StringComparison.Ordinal);
        Assert.DoesNotContain(".Children.Add", dragDrop, StringComparison.Ordinal);
        Assert.DoesNotContain("sourceParent", dragDrop, StringComparison.Ordinal);
        Assert.DoesNotContain("destination.Children", dragDrop, StringComparison.Ordinal);
    }

    private static string ReadDragDrop()
    {
        var root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.BranchDragDrop.cs"));
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
