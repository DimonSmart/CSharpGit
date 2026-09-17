using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private GitBranch? _branchDragSource;

    private void InitializeBranchDragDrop()
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

    private async void RepositoryTree_Drop(object sender, DragEventArgs args)
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
        await ExecuteBranchRenameAsync(sourceBranch, newName);
    }

    private void RepositoryTree_DragItemsCompleted(TreeView sender, TreeViewDragItemsCompletedEventArgs args) =>
        _branchDragSource = null;

    private static RepositoryTreeNode? ResolveBranchDropTarget(DragEventArgs args) =>
        ResolveNode((args.OriginalSource as FrameworkElement)?.DataContext);
}
