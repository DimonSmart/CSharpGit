using System.Collections.ObjectModel;
using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

internal sealed class RepositoryTreeSynchronizer
{
    private readonly ObservableCollection<RepositoryTreeNode> _roots;
    private string? _lastCurrentLocalBranch;
    private bool _hasSynchronizedRepository;

    public RepositoryTreeSynchronizer(ObservableCollection<RepositoryTreeNode> roots) =>
        _roots = roots;

    public int RepositoryReconciliationCount { get; private set; }

    public void ResetSession()
    {
        _lastCurrentLocalBranch = null;
        _hasSynchronizedRepository = false;
        RepositoryReconciliationCount = 0;
    }

    public void ReconcileRepository(
        IReadOnlyList<GitBranch> localBranches,
        IReadOnlyList<GitBranch> remoteBranches,
        IReadOnlyList<GitRemote> remotes,
        IReadOnlyList<GitTag> tags,
        IReadOnlyList<GitStash> stashes,
        IReadOnlyList<WorktreeInfo> worktrees)
    {
        var currentLocalBranch = localBranches.FirstOrDefault(branch => branch.IsCurrent)?.Name;
        var expandCurrentPath = !_hasSynchronizedRepository ||
                                !string.Equals(_lastCurrentLocalBranch, currentLocalBranch, StringComparison.Ordinal);
        var desired = RepositoryTreeDescriptorBuilder.Build(
            localBranches,
            remoteBranches,
            remotes,
            tags,
            stashes,
            worktrees);

        Reconcile(_roots, desired);
        if (expandCurrentPath && currentLocalBranch is not null)
            ExpandCurrentLocalBranchPath(currentLocalBranch);
        ApplyHierarchyGuides();

        _lastCurrentLocalBranch = currentLocalBranch;
        _hasSynchronizedRepository = true;
        RepositoryReconciliationCount++;
    }

    public void ReconcileWorktrees(IReadOnlyList<WorktreeInfo> worktrees)
    {
        var desired = RepositoryTreeDescriptorBuilder.BuildWorktreesRoot(worktrees);
        var root = _roots.FirstOrDefault(node => node.Key == RepositoryTreeDescriptorBuilder.WorktreesRootKey);
        if (root is null)
        {
            root = new RepositoryTreeNode(desired);
            _roots.Insert(0, root);
        }
        else
        {
            root.UpdateFrom(desired);
        }

        Reconcile(root.Children, desired.Children);
        ApplyBranchWorktreeIndicators(worktrees);
        ApplyHierarchyGuides(root);
    }

    private static void Reconcile(
        ObservableCollection<RepositoryTreeNode> target,
        IReadOnlyList<RepositoryTreeDescriptor> desired) =>
        IncrementalTreeReconciler.Reconcile(
            target,
            desired,
            node => node.Key,
            descriptor => descriptor.Key,
            (node, descriptor) => node.UpdateFrom(descriptor),
            descriptor => new RepositoryTreeNode(descriptor),
            node => node.Children,
            descriptor => descriptor.Children,
            StringComparer.Ordinal);

    private void ExpandCurrentLocalBranchPath(string currentLocalBranch)
    {
        var branches = _roots.FirstOrDefault(node => node.Key == RepositoryTreeDescriptorBuilder.BranchesRootKey);
        if (branches is null) return;

        branches.IsExpanded = true;
        RepositoryTreeExpansion.ExpandPathToReference(
            branches,
            currentLocalBranch,
            node => node.Kind,
            node => node.ReferenceName,
            node => node.Children,
            node => node.IsExpanded = true);
    }

    private void ApplyBranchWorktreeIndicators(IReadOnlyList<WorktreeInfo> worktrees)
    {
        var byBranch = worktrees
            .Where(worktree => !string.IsNullOrWhiteSpace(worktree.Branch))
            .GroupBy(worktree => worktree.Branch!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Path, StringComparer.Ordinal);

        var branches = _roots.FirstOrDefault(node => node.Key == RepositoryTreeDescriptorBuilder.BranchesRootKey);
        if (branches is not null) ApplyBranchWorktreeIndicators(branches, byBranch);
    }

    private static void ApplyBranchWorktreeIndicators(
        RepositoryTreeNode node,
        IReadOnlyDictionary<string, string> worktreesByBranch)
    {
        if (node.Kind == RepositoryTreeNodeKind.LocalBranch && node.ReferenceName is { } branch)
            node.SetAssociatedWorktreePath(worktreesByBranch.GetValueOrDefault(branch));

        foreach (var child in node.Children)
            ApplyBranchWorktreeIndicators(child, worktreesByBranch);
    }

    private void ApplyHierarchyGuides()
    {
        foreach (var root in _roots)
        {
            root.SetHierarchyGuideSegments(Array.Empty<RepositoryTreeGuideSegmentKind>());
            ApplyHierarchyGuides(root.Children, Array.Empty<bool>());
        }
    }

    private static void ApplyHierarchyGuides(RepositoryTreeNode root)
    {
        root.SetHierarchyGuideSegments(Array.Empty<RepositoryTreeGuideSegmentKind>());
        ApplyHierarchyGuides(root.Children, Array.Empty<bool>());
    }

    private static void ApplyHierarchyGuides(
        IList<RepositoryTreeNode> nodes,
        IReadOnlyList<bool> ancestorHasFollowingSiblings)
    {
        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            var isLastSibling = index == nodes.Count - 1;
            node.SetHierarchyGuideSegments(
                RepositoryTreeGuideLayout.BuildSegments(ancestorHasFollowingSiblings, isLastSibling));

            if (node.Children.Count == 0) continue;

            var childAncestors = new bool[ancestorHasFollowingSiblings.Count + 1];
            for (var ancestorIndex = 0; ancestorIndex < ancestorHasFollowingSiblings.Count; ancestorIndex++)
                childAncestors[ancestorIndex] = ancestorHasFollowingSiblings[ancestorIndex];
            childAncestors[^1] = !isLastSibling;
            ApplyHierarchyGuides(node.Children, childAncestors);
        }
    }
}
