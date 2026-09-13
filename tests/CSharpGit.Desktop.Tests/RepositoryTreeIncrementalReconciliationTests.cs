using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Desktop.Tests;

public sealed class RepositoryTreeIncrementalReconciliationTests
{
    [Fact]
    public void DeleteBranchPreservesUnchangedNodesAndDoesNotResetCollections()
    {
        var roots = Reconcile([], Build(local: [Branch("main", current: true), Branch("feature/a"), Branch("feature/b")]));
        var branches = Find(roots, "group:branches");
        var main = Find(roots, "local-branch:main");
        var feature = Find(roots, "branch-folder:local:feature");
        var branchB = Find(roots, "local-branch:feature/b");
        var rootActions = Observe(roots);
        var featureActions = Observe(feature.Children);

        Reconcile(roots, Build(local: [Branch("main", current: true), Branch("feature/b")]));

        Assert.Same(branches, Find(roots, "group:branches"));
        Assert.Same(main, Find(roots, "local-branch:main"));
        Assert.Same(feature, Find(roots, "branch-folder:local:feature"));
        Assert.Same(branchB, Find(roots, "local-branch:feature/b"));
        Assert.Null(TryFind(roots, "local-branch:feature/a"));
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, rootActions);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, featureActions);
        Assert.Contains(NotifyCollectionChangedAction.Remove, featureActions);
    }

    [Fact]
    public void CreateAndDeleteLastNestedBranchUseOnlyNecessaryNodes()
    {
        var roots = Reconcile([], Build(local: [Branch("main", current: true)]));
        var branches = Find(roots, "group:branches");
        var main = Find(roots, "local-branch:main");
        var branchActions = Observe(branches.Children);

        Reconcile(roots, Build(local: [Branch("main", current: true), Branch("feature/ui/tree")]));
        var feature = Find(roots, "branch-folder:local:feature");
        var ui = Find(roots, "branch-folder:local:feature/ui");
        var tree = Find(roots, "local-branch:feature/ui/tree");

        Assert.Same(main, Find(roots, "local-branch:main"));
        Assert.Contains(NotifyCollectionChangedAction.Add, branchActions);
        Assert.Equal(new[] { "local-branch:main", "branch-folder:local:feature" }, branches.Children.Select(node => node.Key).ToArray());

        Reconcile(roots, Build(local: [Branch("main", current: true)]));

        Assert.Same(branches, Find(roots, "group:branches"));
        Assert.Same(main, Find(roots, "local-branch:main"));
        Assert.DoesNotContain(feature, branches.Children);
        Assert.Null(TryFind(roots, ui.Key));
        Assert.Null(TryFind(roots, tree.Key));
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, branchActions);
    }

    [Fact]
    public void SwitchCurrentBranchReusesNodesAndExpandsOnlyNewCurrentPath()
    {
        var roots = Reconcile([], Build(local:
        [
            Branch("main", current: true),
            Branch("feature/ui/tree"),
            Branch("topic/manual")
        ]));
        var branches = Find(roots, "group:branches");
        var main = Find(roots, "local-branch:main");
        var feature = Find(roots, "branch-folder:local:feature");
        var ui = Find(roots, "branch-folder:local:feature/ui");
        var tree = Find(roots, "local-branch:feature/ui/tree");
        var topic = Find(roots, "branch-folder:local:topic");
        feature.IsExpanded = false;
        ui.IsExpanded = false;
        topic.IsExpanded = true;
        var rootActions = Observe(roots);

        Reconcile(roots, Build(local:
        [
            Branch("main"),
            Branch("feature/ui/tree", current: true),
            Branch("topic/manual")
        ]));
        RepositoryTreeExpansion.ExpandPathToReference(
            branches,
            "feature/ui/tree",
            node => node.Kind,
            node => node.ReferenceName,
            node => node.Children,
            node => node.IsExpanded = true);

        Assert.Same(main, Find(roots, "local-branch:main"));
        Assert.Same(tree, Find(roots, "local-branch:feature/ui/tree"));
        Assert.False(main.IsCurrent);
        Assert.True(tree.IsCurrent);
        Assert.True(feature.IsExpanded);
        Assert.True(ui.IsExpanded);
        Assert.True(topic.IsExpanded);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, rootActions);
    }

    [Fact]
    public void RemoteRefsReconcileWithoutReplacingRemoteOrFolders()
    {
        var origin = new GitRemote("origin", "fetch", "push");
        var roots = Reconcile([], Build(
            remote: [Branch("origin/main"), Branch("origin/feature/a"), Branch("origin/feature/b")],
            remotes: [origin]));
        var remotesRoot = Find(roots, "group:remotes");
        var originNode = Find(roots, "remote:origin");
        var feature = Find(roots, "branch-folder:remote:origin:feature");
        var branchB = Find(roots, "remote-branch:origin/feature/b");
        originNode.IsExpanded = true;
        var remoteActions = Observe(originNode.Children);

        Reconcile(roots, Build(
            remote: [Branch("origin/main"), Branch("origin/feature/b"), Branch("origin/feature/c")],
            remotes: [origin]));

        Assert.Same(remotesRoot, Find(roots, "group:remotes"));
        Assert.Same(originNode, Find(roots, "remote:origin"));
        Assert.Same(feature, Find(roots, "branch-folder:remote:origin:feature"));
        Assert.Same(branchB, Find(roots, "remote-branch:origin/feature/b"));
        Assert.True(originNode.IsExpanded);
        Assert.Null(TryFind(roots, "remote-branch:origin/feature/a"));
        Assert.NotNull(TryFind(roots, "remote-branch:origin/feature/c"));
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, remoteActions);

        Reconcile(roots, Build(remote: [Branch("origin/main")], remotes: [origin]));
        Assert.Same(originNode, Find(roots, "remote:origin"));
        Assert.Null(TryFind(roots, "branch-folder:remote:origin:feature"));
        Assert.Same(Find(roots, "remote-branch:origin/main"), originNode.Children.Single());
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, remoteActions);
    }

    [Fact]
    public void TagOrderingUsesMoveAddRemoveWithoutReplacingSurvivors()
    {
        var roots = Reconcile([], Build(tags: [Tag("v1"), Tag("v2"), Tag("v3")]));
        var tags = Find(roots, "group:tags");
        var v1 = Find(roots, "tag:v1");
        var v3 = Find(roots, "tag:v3");
        var actions = Observe(tags.Children);

        Reconcile(roots, Build(tags: [Tag("v3"), Tag("v4"), Tag("v1")]));

        Assert.Same(v3, Find(roots, "tag:v3"));
        Assert.Same(v1, Find(roots, "tag:v1"));
        Assert.Equal(new[] { "tag:v3", "tag:v4", "tag:v1" }, tags.Children.Select(node => node.Key).ToArray());
        Assert.Contains(NotifyCollectionChangedAction.Move, actions);
        Assert.Contains(NotifyCollectionChangedAction.Add, actions);
        Assert.Contains(NotifyCollectionChangedAction.Remove, actions);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, actions);
    }

    [Fact]
    public void StashReindexKeepsNodeIdentityByCommit()
    {
        var stashA = new GitStash("stash@{0}", "aaaaaaaa", "A");
        var stashB = new GitStash("stash@{1}", "bbbbbbbb", "B");
        var roots = Reconcile([], Build(stashes: [stashA, stashB]));
        var nodeB = Find(roots, "stash:bbbbbbbb");

        Reconcile(roots, Build(stashes: [stashB with { Name = "stash@{0}" }]));

        Assert.Same(nodeB, Find(roots, "stash:bbbbbbbb"));
        Assert.Equal("stash@{0}: B", nodeB.Name);
        Assert.Equal("stash@{0}", ((GitStash)nodeB.Value!).Name);
        Assert.Null(TryFind(roots, "stash:aaaaaaaa"));
    }

    [Fact]
    public void WorktreeChangesReuseRootChildAndAssociatedBranch()
    {
        var worktree = new WorktreeInfo("/repo", "11111111", "main", true, false, false, null, false);
        var roots = Reconcile([], Build(local: [Branch("main", current: true)], worktrees: [worktree]));
        var worktreesRoot = Find(roots, "group:worktrees");
        var worktreeNode = Find(roots, "worktree:/repo");
        var main = Find(roots, "local-branch:main");
        var actions = Observe(worktreesRoot.Children);

        var updated = worktree with { IsLocked = true, LockReason = "maintenance" };
        var second = new WorktreeInfo("/repo-feature", "22222222", "feature", false, false, false, null, false);
        Reconcile(roots, Build(
            local: [Branch("main", current: true), Branch("feature")],
            worktrees: [updated, second]));

        Assert.Same(worktreesRoot, Find(roots, "group:worktrees"));
        Assert.Same(worktreeNode, Find(roots, "worktree:/repo"));
        Assert.Same(main, Find(roots, "local-branch:main"));
        Assert.Contains("locked: maintenance", worktreeNode.Name, StringComparison.Ordinal);
        Assert.Equal("/repo", main.AssociatedWorktreePath);
        Assert.Equal("/repo-feature", Find(roots, "local-branch:feature").AssociatedWorktreePath);
        Assert.Contains(NotifyCollectionChangedAction.Add, actions);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, actions);

        var feature = Find(roots, "local-branch:feature");
        Reconcile(roots, Build(local: [Branch("main", current: true), Branch("feature")], worktrees: [updated]));
        Assert.Same(worktreesRoot, Find(roots, "group:worktrees"));
        Assert.Null(TryFind(roots, "worktree:/repo-feature"));
        Assert.Same(feature, Find(roots, "local-branch:feature"));
        Assert.Null(feature.AssociatedWorktreePath);
    }

    [Fact]
    public void SelectionSurvivesExistingNodeAndFallsBackWithinSameAreaAfterDeletion()
    {
        var roots = Reconcile([], Build(local: [Branch("main", current: true), Branch("feature/a"), Branch("feature/b")]));
        var branchB = Find(roots, "local-branch:feature/b");
        var preserved = RepositoryTreeSelection.Capture(
            roots,
            branchB,
            node => node.Key,
            node => node.Children);

        Reconcile(roots, Build(local: [Branch("main", current: true), Branch("feature/a"), Branch("feature/b"), Branch("feature/c")]));
        Assert.Same(branchB, RepositoryTreeSelection.Resolve(roots, preserved, node => node.Key, node => node.Children));

        var branchA = Find(roots, "local-branch:feature/a");
        var deleted = RepositoryTreeSelection.Capture(roots, branchA, node => node.Key, node => node.Children);
        Reconcile(roots, Build(local: [Branch("main", current: true), Branch("feature/b"), Branch("feature/c")]));
        Assert.Same(branchB, RepositoryTreeSelection.Resolve(roots, deleted, node => node.Key, node => node.Children));

        var feature = Find(roots, "branch-folder:local:feature");
        var lastChild = Find(roots, "local-branch:feature/b");
        var nested = RepositoryTreeSelection.Capture(roots, lastChild, node => node.Key, node => node.Children);
        Reconcile(roots, Build(local: [Branch("main", current: true), Branch("feature/c")]));
        Assert.Same(Find(roots, "local-branch:feature/c"), RepositoryTreeSelection.Resolve(roots, nested, node => node.Key, node => node.Children));
        Assert.Same(feature, Find(roots, "branch-folder:local:feature"));

        Reconcile(roots, Build(local: [Branch("main", current: true), Branch("obsolete/only")]));
        var only = Find(roots, "local-branch:obsolete/only");
        var disappearingFolder = RepositoryTreeSelection.Capture(roots, only, node => node.Key, node => node.Children);
        Reconcile(roots, Build(local: [Branch("main", current: true)]));
        Assert.Same(Find(roots, "local-branch:main"), RepositoryTreeSelection.Resolve(roots, disappearingFolder, node => node.Key, node => node.Children));
    }

    [Fact]
    public void IdenticalResultingTreeIsStructuralNoOp()
    {
        var desired = Build(
            local: [Branch("main", current: true), Branch("feature/a")],
            tags: [Tag("v1")],
            stashes: [new GitStash("stash@{0}", "aaaa", "keep")]);
        var roots = Reconcile([], desired);
        var allCollections = EnumerateCollections(roots).ToArray();
        var actions = allCollections.Select(Observe).ToArray();

        Reconcile(roots, desired);

        Assert.All(actions, list => Assert.Empty(list));
    }

    private static IReadOnlyList<RepositoryTreeDescriptor> Build(
        IReadOnlyList<GitBranch>? local = null,
        IReadOnlyList<GitBranch>? remote = null,
        IReadOnlyList<GitRemote>? remotes = null,
        IReadOnlyList<GitTag>? tags = null,
        IReadOnlyList<GitStash>? stashes = null,
        IReadOnlyList<WorktreeInfo>? worktrees = null) =>
        RepositoryTreeDescriptorBuilder.Build(
            local ?? [],
            remote ?? [],
            remotes ?? [],
            tags ?? [],
            stashes ?? [],
            worktrees ?? []);

    private static GitBranch Branch(string name, bool current = false) => new(name, name + "-commit", current);
    private static GitTag Tag(string name) => new(name, name + "-commit");

    private static ObservableCollection<FakeNode> Reconcile(
        ObservableCollection<FakeNode> roots,
        IReadOnlyList<RepositoryTreeDescriptor> desired)
    {
        IncrementalTreeReconciler.Reconcile(
            roots,
            desired,
            node => node.Key,
            descriptor => descriptor.Key,
            (node, descriptor) => node.Update(descriptor),
            descriptor => new FakeNode(descriptor),
            node => node.Children,
            descriptor => descriptor.Children,
            StringComparer.Ordinal);
        return roots;
    }

    private static FakeNode Find(IEnumerable<FakeNode> roots, string key) =>
        TryFind(roots, key) ?? throw new Xunit.Sdk.XunitException($"Node '{key}' was not found.");

    private static FakeNode? TryFind(IEnumerable<FakeNode> roots, string key)
    {
        foreach (var node in roots)
        {
            if (node.Key == key) return node;
            if (TryFind(node.Children, key) is { } child) return child;
        }
        return null;
    }

    private static List<NotifyCollectionChangedAction> Observe(INotifyCollectionChanged collection)
    {
        var actions = new List<NotifyCollectionChangedAction>();
        collection.CollectionChanged += (_, args) => actions.Add(args.Action);
        return actions;
    }

    private static IEnumerable<ObservableCollection<FakeNode>> EnumerateCollections(IEnumerable<FakeNode> roots)
    {
        if (roots is ObservableCollection<FakeNode> rootCollection) yield return rootCollection;
        foreach (var node in roots)
        {
            yield return node.Children;
            foreach (var nested in EnumerateCollections(node.Children).Skip(1)) yield return nested;
        }
    }

    private sealed class FakeNode
    {
        public FakeNode(RepositoryTreeDescriptor descriptor)
        {
            Key = descriptor.Key;
            Kind = descriptor.Kind;
            Update(descriptor);
        }

        public string Key { get; }
        public RepositoryTreeNodeKind Kind { get; }
        public string Name { get; private set; } = string.Empty;
        public string? ReferenceName { get; private set; }
        public object? Value { get; private set; }
        public bool IsCurrent { get; private set; }
        public string? AssociatedWorktreePath { get; private set; }
        public bool IsExpanded { get; set; }
        public ObservableCollection<FakeNode> Children { get; } = [];

        public void Update(RepositoryTreeDescriptor descriptor)
        {
            Name = descriptor.Name;
            ReferenceName = descriptor.ReferenceName;
            Value = descriptor.Value;
            IsCurrent = descriptor.IsCurrent;
            AssociatedWorktreePath = descriptor.AssociatedWorktreePath;
        }
    }
}
