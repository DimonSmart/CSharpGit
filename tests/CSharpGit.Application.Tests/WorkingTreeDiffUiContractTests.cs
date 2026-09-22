namespace CSharpGit.Application.Tests;

public sealed class WorkingTreeDiffUiContractTests
{
    [Fact]
    public void WorkingTreeDiffUsesExplicitReadOnlyContractAndSeparateBatchSelection()
    {
        var root = FindRepositoryRoot();
        var contract = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Application", "Abstractions", "IWorkingTreeDiffService.cs"));
        var mutationContract = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Application", "Abstractions", "IWorkingTreeService.cs"));
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.cs"));

        Assert.Contains("enum WorkingTreeDiffKind", contract);
        Assert.Contains("Unstaged", contract);
        Assert.Contains("Staged", contract);
        Assert.Contains("interface IWorkingTreeDiffService", contract);
        Assert.Contains("WorkingTreeChange change", contract);
        Assert.DoesNotContain("ReadDiffAsync", ExtractInterface(mutationContract, "IWorkingTreeService"));
        Assert.Contains("SelectedUnstagedChanges", viewModel);
        Assert.Contains("SelectedStagedChanges", viewModel);
        Assert.Contains("ActiveWorkingTreeChange", viewModel);
        Assert.Contains("ActiveWorkingTreeDiffKind", viewModel);
        Assert.Contains("SelectedWorkingTreeDiff", viewModel);
    }

    [Fact]
    public void WorkingTreeUsesHierarchicalLeafOnlyDesktopSelectionAndSingleActivePreview()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var confirmations = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.ConfirmationDialogs.cs"));
        var workingTree = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.WorkingTreeDiff.cs"));
        var selection = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "WorkingTreeTreeSelection.cs"));
        var repositoryTree = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Styles", "RepositoryTree.xaml"));
        var workspace = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Styles", "Workspace.xaml"));

        Assert.Contains("<TreeView x:Name=\"UnstagedChangesTree\"", xaml);
        Assert.Contains("<TreeView x:Name=\"StagedChangesTree\"", xaml);
        Assert.True(Count(xaml, "ItemTemplate=\"{StaticResource WorkingTreeTreeItemTemplate}\"") >= 2);
        Assert.True(Count(xaml, "SelectionMode=\"None\"") >= 2);
        Assert.DoesNotContain("SelectionMode=\"Multiple\"", xaml);
        Assert.DoesNotContain("SelectionMode=\"Extended\"", xaml);
        Assert.DoesNotContain("CheckBox", repositoryTree);
        Assert.Contains("x:Key=\"WorkingTreeTreeItemTemplate\"", repositoryTree);
        Assert.Contains("controls:RepositoryTreeGuides", repositoryTree);
        Assert.Contains("Segments=\"{Binding HierarchyGuideSegments}\"", repositoryTree);
        Assert.Contains("Opacity=\"{Binding BatchSelectionOpacity}\"", repositoryTree);
        Assert.DoesNotContain("x:Key=\"WorkingTreeRowStyle\"", workspace);
        Assert.DoesNotContain("UnstagedWorkingTreeItemTemplate", workspace);
        Assert.DoesNotContain("StagedWorkingTreeItemTemplate", workspace);

        Assert.Contains("WorkingTreeNodeInvoked", workingTree);
        Assert.Contains("SetWorkingTreeSelection", workingTree);
        Assert.Contains("selected.Change!", workingTree);
        Assert.Contains("if (node?.Change is null) return", workingTree);
        Assert.Contains("WorkingTreeTreeSelection", workingTree);
        Assert.Contains("controlPressed", selection);
        Assert.Contains("shiftPressed", selection);
        Assert.Contains("EnumerateVisibleLeaves", selection);
        Assert.Contains("node.Change is not null", selection);

        Assert.Contains("Command=\"{Binding StageSelectedCommand}\"", xaml);
        Assert.Contains("Command=\"{Binding StageAllCommand}\"", xaml);
        Assert.Contains("Command=\"{Binding UnstageSelectedCommand}\"", xaml);
        Assert.Contains("Command=\"{Binding UnstageAllCommand}\"", xaml);
        Assert.Contains("BatchDiscardConfirmationMessage", confirmations);
        Assert.Contains("ShowDiscardConfirmationAsync", confirmations);
        Assert.DoesNotContain("BatchDiscardConfirmationVisibility", xaml);

        Assert.Contains("x:Name=\"WorkingTreeCompactDiffList\"", xaml);
        Assert.Contains("controls:GridSplitter", xaml);
        Assert.Contains("Text=\"OLD\"", xaml);
        Assert.Contains("Text=\"NEW\"", xaml);
        Assert.Contains("WorkingTreeDiffKindText", xaml);
        Assert.Contains("ItemContainerStyle=\"{StaticResource DiffRowStyle}\"", xaml);
        Assert.Contains("ItemTemplate=\"{StaticResource DiffItemTemplate}\"", xaml);
        Assert.Contains("x:Key=\"DiffRowStyle\"", workspace);
        Assert.DoesNotContain("CompactResource<", workingTree);
        Assert.Contains("CompactDiffLine.Build", workingTree);
    }

    [Fact]
    public void WorkingTreeHierarchyIsPresentationOnlyAndUsesSharedPathBuilder()
    {
        var root = FindRepositoryRoot();
        var workingTreeNode = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "WorkingTreeTreeNode.cs"));
        var changedFileNode = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "ChangedFileTreeNode.cs"));
        var snapshotNode = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "RepositorySnapshotTreeNode.cs"));
        var builder = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "PathTreeBuilder.cs"));

        Assert.Contains("PathTreeBuilder.Build", workingTreeNode);
        Assert.Contains("PathTreeBuilder.Build", changedFileNode);
        Assert.Contains("PathTreeBuilder.Build", snapshotNode);
        Assert.Contains("TreeHierarchyGuideBuilder.Apply", workingTreeNode);
        Assert.Contains("HashSet<string>(StringComparer.Ordinal)", builder);
        Assert.Contains("StringComparer.OrdinalIgnoreCase", builder);
        Assert.Contains("StringComparer.Ordinal", builder);
        Assert.DoesNotContain("System.IO.Path", builder);
        Assert.DoesNotContain("IWorkingTreeService", builder);
        Assert.DoesNotContain("RepositoryTreeGuides", builder);
    }

    [Fact]
    public void BatchStagingUsesOnePathspecOperationAndHandlesUnbornRepositories()
    {
        var root = FindRepositoryRoot();
        var contract = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Application", "Abstractions", "IWorkingTreeService.cs"));
        var staging = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Git", "GitCliRepositoryService.Staging.cs"));
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.cs"));

        foreach (var member in new[] { "StageFilesAsync", "UnstageFilesAsync", "UnstageAllAsync" })
            Assert.Contains(member, ExtractInterface(contract, "IWorkingTreeService"));

        Assert.Contains("HashSet<string>(StringComparer.Ordinal)", staging);
        Assert.Contains("change.OriginalPath", staging);
        Assert.Contains("[command, \"--\", .. BatchPaths(changes)]", staging);
        Assert.Contains("\"restore\", \"--staged\", \"--\", .. paths", staging);
        Assert.Contains("\"rm\", \"--cached\", \"--force\"", staging);
        Assert.Contains("\"reset\", \"--mixed\"", staging);
        Assert.DoesNotContain("foreach (var change in changes)\n            await", staging);

        Assert.Contains("_selectedUnstagedChanges.ToArray()", viewModel);
        Assert.Contains("_selectedStagedChanges.ToArray()", viewModel);
        Assert.Contains("WaitAsync(0)", viewModel);
        Assert.Contains("Conflicts.Any(conflict => !conflict.IsResolved)", viewModel);
        Assert.Contains("ClearWorkingTreePresentationSelection", viewModel);
        Assert.Contains("Could not stage selected files", viewModel);
        Assert.Contains("Could not unstage selected files", viewModel);
    }

    [Fact]
    public void WorkingTreeDiffIsLazyCancellationSafeAndDiscardPreservesIndex()
    {
        var root = FindRepositoryRoot();
        var workingTree = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.WorkingTreeDiff.cs"));
        var git = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Git", "GitCliRepositoryService.cs"));
        var gitDiff = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Git", "GitCliRepositoryService.WorkingTreeDiff.cs"));
        var discardViewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.Discard.cs"));

        Assert.Contains("CancellationTokenSource", workingTree);
        Assert.Contains("_workingTreeDiffGeneration", workingTree);
        Assert.Contains("IsCurrentWorkingTreeDiffRequest", workingTree);
        Assert.Contains("OperationCanceledException", workingTree);
        Assert.Contains("ReadDiffAsync(repository, change, kind", workingTree);
        Assert.DoesNotContain("foreach (var change in _viewModel.Changes)", workingTree);

        var discardStart = git.IndexOf("public async Task DiscardFileAsync", StringComparison.Ordinal);
        var discardEnd = git.IndexOf("public async Task DiscardAllFileChangesAsync", discardStart, StringComparison.Ordinal);
        var discard = git[discardStart..discardEnd];
        Assert.Contains("\"restore\", \"--worktree\", \"--\", change.Path", discard);
        Assert.DoesNotContain("\"--source=HEAD\"", discard);
        Assert.DoesNotContain("\"--staged\"", discard);

        var stagedDiscardStart = discardEnd;
        var stagedDiscardEnd = git.IndexOf("public async Task CommitAsync", stagedDiscardStart, StringComparison.Ordinal);
        var stagedDiscard = git[stagedDiscardStart..stagedDiscardEnd];
        Assert.Contains("\"--source=HEAD\"", stagedDiscard);
        Assert.Contains("\"--staged\"", stagedDiscard);
        Assert.Contains("\"--worktree\"", stagedDiscard);
        Assert.Contains("WorkingTreeDiscard.CreateSelected(_selectedUnstagedChanges)", discardViewModel);
        Assert.Contains("WorkingTreeDiscard.CreateAll(Changes)", discardViewModel);
        Assert.Contains("WorkingTreeDiscard.ExecuteAsync", discardViewModel);

        Assert.Contains("\"--cached\"", gitDiff);
        Assert.Contains("\"--no-ext-diff\"", gitDiff);
        Assert.Contains("\"--find-renames\"", gitDiff);
        Assert.Contains("change.OriginalPath", gitDiff);
        Assert.Contains("\"--no-index\"", gitDiff);
        Assert.Contains("result.ExitCode is not 0 and not 1", gitDiff);
        Assert.Contains("RunGitForResultAsync", gitDiff);
        Assert.Contains("GitDiffParser.ParseLines", gitDiff);
    }

    private static int Count(string value, string fragment) =>
        (value.Length - value.Replace(fragment, string.Empty, StringComparison.Ordinal).Length) / fragment.Length;

    private static string ExtractInterface(string source, string name)
    {
        var start = source.IndexOf($"public interface {name}", StringComparison.Ordinal);
        var next = source.IndexOf("public interface ", start + 1, StringComparison.Ordinal);
        return source[start..(next < 0 ? source.Length : next)];
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
