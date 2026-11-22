using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

using CSharpGit.Presentation.Views;

namespace CSharpGit.Presentation.Workflows;

// Drag/drop emits a semantic rename request; it does not mutate tree nodes.
internal sealed class BranchDragDropController(
    TreeView tree,
    Func<bool> canRename,
    Func<GitBranch, string, Task> renameBranch,
    Func<bool> isShuttingDown,
    Action<Func<Task>, string> runUiOperation)
{
    private readonly TreeView _tree = tree;
    private readonly Func<bool> _canRename = canRename;
    private readonly Func<GitBranch, string, Task> _renameBranch = renameBranch;
    private readonly Func<bool> _isShuttingDown = isShuttingDown;
    private readonly Action<Func<Task>, string> _runUiOperation = runUiOperation;
    private TreeView RepositoryTree => _tree;
    private static RepositoryTreeNode? ResolveNode(object? value) => RepositoryExplorerView.ResolveNode(value);
    private bool CanRenameBranch() => _canRename();
    private Task ExecuteBranchRenameAsync(GitBranch branch, string name) => _renameBranch(branch, name);

    private GitBranch? _branchDragSource;

    internal void Shutdown()
    {
        RepositoryTree.DragItemsStarting -= RepositoryTree_DragItemsStarting;
        RepositoryTree.DragOver -= RepositoryTree_DragOver;
        RepositoryTree.Drop -= RepositoryTree_Drop;
        RepositoryTree.DragItemsCompleted -= RepositoryTree_DragItemsCompleted;
        _branchDragSource = null;
    }

    internal void Initialize()
    {
        RepositoryTree.CanDragItems = true;
        RepositoryTree.AllowDrop = true;
        RepositoryTree.CanReorderItems = false;

        RepositoryTree.DragItemsStarting -= RepositoryTree_DragItemsStarting;
        RepositoryTree.DragItemsStarting += RepositoryTree_DragItemsStarting;
        RepositoryTree.DragOver -= RepositoryTree_DragOver;
        RepositoryTree.DragOver += RepositoryTree_DragOver;
        RepositoryTree.Drop -= RepositoryTree_Drop;
        RepositoryTree.Drop += RepositoryTree_Drop;
        RepositoryTree.DragItemsCompleted -= RepositoryTree_DragItemsCompleted;
        RepositoryTree.DragItemsCompleted += RepositoryTree_DragItemsCompleted;
    }

    private void RepositoryTree_DragItemsStarting(TreeView sender, TreeViewDragItemsStartingEventArgs args)
    {
        _branchDragSource = null;

        var node = args.Items.Count == 1
            ? ResolveNode(args.Items[0])
            : null;
        var branch = node is null
            ? null
            : BranchMovePlanning.GetSourceBranch(node.Kind, node.Value);

        if (branch is null || !CanRenameBranch())
        {
            args.Cancel = true;
            return;
        }

        _branchDragSource = branch;
        args.Data.RequestedOperation = DataPackageOperation.Move;
        args.Data.SetText(branch.Name);
    }

    private void RepositoryTree_DragOver(object sender, DragEventArgs args)
    {
        args.AcceptedOperation = CanAcceptBranchDrop(args)
            ? DataPackageOperation.Move
            : DataPackageOperation.None;
    }

    private bool CanAcceptBranchDrop(DragEventArgs args)
    {
        if (_branchDragSource is null || !CanRenameBranch()) return false;

        var target = ResolveBranchDropTarget(args);
        return target is not null
            && BranchMovePlanning.TryGetDestinationPrefix(
                target.Kind,
                target.Key,
                target.Value,
                out _);
    }

    private void RepositoryTree_Drop(object sender, DragEventArgs args) =>
        _runUiOperation(() => RepositoryTree_DropCoreAsync(sender, args), "RepositoryTree_Drop");

    private async Task RepositoryTree_DropCoreAsync(object sender, DragEventArgs args)
    {
        var sourceBranch = _branchDragSource;
        var target = ResolveBranchDropTarget(args);
        args.AcceptedOperation = DataPackageOperation.None;

        if (sourceBranch is null || target is null || !CanRenameBranch()) return;
        if (!BranchMovePlanning.TryGetDestinationPrefix(
                target.Kind,
                target.Key,
                target.Value,
                out var destinationPrefix))
        {
            return;
        }

        var newName = BranchMovePlanning.BuildTargetName(sourceBranch, destinationPrefix);
        if (string.Equals(sourceBranch.Name, newName, StringComparison.Ordinal)) return;

        args.AcceptedOperation = DataPackageOperation.Move;
        if (!_isShuttingDown()) await ExecuteBranchRenameAsync(sourceBranch, newName);
    }

    private void RepositoryTree_DragItemsCompleted(TreeView sender, TreeViewDragItemsCompletedEventArgs args) =>
        _branchDragSource = null;

    private static RepositoryTreeNode? ResolveBranchDropTarget(DragEventArgs args) =>
        ResolveNode((args.OriginalSource as FrameworkElement)?.DataContext);
}
