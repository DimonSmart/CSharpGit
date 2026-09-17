using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Desktop.Tests;

public sealed class RepositorySnapshotTreeTests
{
    [Fact]
    public void BuildPreservesCaseSensitiveGitPathIdentityAndDeterministicOrder()
    {
        var entries = new[]
        {
            Entry("src/case.cs"),
            Entry("README.md"),
            Entry("src/Case.cs"),
            Entry("tests/Z.cs"),
            Entry("tests/a.cs")
        };

        var roots = RepositorySnapshotTreeNode.Build(entries);

        Assert.Equal(new[] { "src", "tests", "README.md" }, roots.Select(node => node.DisplayName));
        var src = Assert.Single(roots, node => node.DisplayName == "src");
        Assert.Equal(2, src.Children.Count);
        Assert.Contains(src.Children, node => node.Path == "src/Case.cs");
        Assert.Contains(src.Children, node => node.Path == "src/case.cs");
    }

    [Fact]
    public void NameSearchMatchesFullPathAndRetainsOnlyRequiredAncestors()
    {
        var entries = new[]
        {
            Entry("src/CSharpGit.Application/Abstractions/IHistoryService.cs"),
            Entry("src/CSharpGit.Git/GitReferenceHistoryService.cs"),
            Entry("tests/OtherTests.cs")
        };

        var roots = RepositorySnapshotTreeNode.Build(entries, "historyservice");

        var src = Assert.Single(roots);
        Assert.Equal("src", src.DisplayName);
        Assert.Equal(2, LeafPaths(src).Count);
        Assert.DoesNotContain(LeafPaths(src), path => path.StartsWith("tests/", StringComparison.Ordinal));
    }

    [Fact]
    public void IdenticalTreeIsStructuralNoOpAndPreservesExpansion()
    {
        var roots = new ObservableCollection<RepositorySnapshotTreeNode>();
        RepositorySnapshotTreeSynchronizer.Reconcile(roots,
        [
            Entry("src/A.cs"),
            Entry("src/B.cs")
        ]);
        var src = Assert.Single(roots);
        var a = Assert.Single(src.Children, node => node.Path == "src/A.cs");
        src.IsExpanded = true;
        var rootChanges = new List<NotifyCollectionChangedAction>();
        var childChanges = new List<NotifyCollectionChangedAction>();
        roots.CollectionChanged += (_, args) => rootChanges.Add(args.Action);
        src.Children.CollectionChanged += (_, args) => childChanges.Add(args.Action);

        RepositorySnapshotTreeSynchronizer.Reconcile(roots,
        [
            Entry("src/A.cs"),
            Entry("src/B.cs")
        ]);

        Assert.Same(src, Assert.Single(roots));
        Assert.Same(a, Assert.Single(src.Children, node => node.Path == "src/A.cs"));
        Assert.True(src.IsExpanded);
        Assert.Empty(rootChanges);
        Assert.Empty(childChanges);
    }

    [Fact]
    public void NameSearchNarrowingReusesIntersectionWithoutReset()
    {
        var snapshot = new[]
        {
            Entry("src/Service.cs"),
            Entry("src/Settings.cs"),
            Entry("tests/ServiceTests.cs")
        };
        var roots = new ObservableCollection<RepositorySnapshotTreeNode>();
        RepositorySnapshotTreeSynchronizer.Reconcile(roots, snapshot, "se");
        var src = Assert.Single(roots, node => node.Path == "src");
        var service = Assert.Single(src.Children, node => node.Path == "src/Service.cs");
        var actions = new List<NotifyCollectionChangedAction>();
        roots.CollectionChanged += (_, args) => actions.Add(args.Action);
        src.Children.CollectionChanged += (_, args) => actions.Add(args.Action);

        RepositorySnapshotTreeSynchronizer.Reconcile(roots, snapshot, "service");

        Assert.Same(src, Assert.Single(roots, node => node.Path == "src"));
        Assert.Same(service, Assert.Single(src.Children, node => node.Path == "src/Service.cs"));
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, actions);
    }

    [Fact]
    public void LeafKindChangeReusesNodeAndUpdatesDisplay()
    {
        var roots = new ObservableCollection<RepositorySnapshotTreeNode>();
        RepositorySnapshotTreeSynchronizer.Reconcile(roots, [Entry("link", RepositorySnapshotEntryKind.File)]);
        var node = Assert.Single(roots);
        var changedProperties = new List<string?>();
        node.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);
        var replacement = Entry("link", RepositorySnapshotEntryKind.Symlink);

        RepositorySnapshotTreeSynchronizer.Reconcile(roots, [replacement]);

        Assert.Same(node, Assert.Single(roots));
        Assert.Same(replacement, node.Entry);
        Assert.Equal("↗ link", node.DisplayLabel);
        Assert.Contains(nameof(RepositorySnapshotTreeNode.Entry), changedProperties);
        Assert.Contains(nameof(RepositorySnapshotTreeNode.DisplayLabel), changedProperties);
    }

    [Fact]
    public void FileAndDirectoryAtSamePathDoNotReusePresentationNode()
    {
        var roots = new ObservableCollection<RepositorySnapshotTreeNode>();
        RepositorySnapshotTreeSynchronizer.Reconcile(roots, [Entry("src")]);
        var file = Assert.Single(roots);

        RepositorySnapshotTreeSynchronizer.Reconcile(roots, [Entry("src/A.cs")]);

        var directory = Assert.Single(roots);
        Assert.NotSame(file, directory);
        Assert.True(directory.IsDirectory);
    }

    [Fact]
    public void SmallStructuralChangeKeepsExistingNodesAndAvoidsReset()
    {
        var roots = new ObservableCollection<RepositorySnapshotTreeNode>();
        RepositorySnapshotTreeSynchronizer.Reconcile(roots,
        [
            Entry("src/A.cs"),
            Entry("src/B.cs")
        ]);
        var src = Assert.Single(roots);
        var a = Assert.Single(src.Children, node => node.Path == "src/A.cs");
        var actions = new List<NotifyCollectionChangedAction>();
        src.Children.CollectionChanged += (_, args) => actions.Add(args.Action);

        RepositorySnapshotTreeSynchronizer.Reconcile(roots,
        [
            Entry("src/A.cs"),
            Entry("src/C.cs")
        ]);

        Assert.Same(src, Assert.Single(roots));
        Assert.Same(a, Assert.Single(src.Children, node => node.Path == "src/A.cs"));
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, actions);
        Assert.Contains(NotifyCollectionChangedAction.Remove, actions);
        Assert.Contains(NotifyCollectionChangedAction.Add, actions);
    }

    [Fact]
    public void SnapshotCacheIsLruBoundedAndTouchesHits()
    {
        var cache = new RepositorySnapshotCache(2);
        cache.Set("repo", "a", [Entry("a.txt")]);
        cache.Set("repo", "b", [Entry("b.txt")]);
        Assert.True(cache.TryGet("repo", "a", out _));

        cache.Set("repo", "c", [Entry("c.txt")]);

        Assert.True(cache.TryGet("repo", "a", out _));
        Assert.False(cache.TryGet("repo", "b", out _));
        Assert.True(cache.TryGet("repo", "c", out _));
        Assert.Equal(2, cache.Count);
    }

    private static RepositorySnapshotEntry Entry(
        string path,
        RepositorySnapshotEntryKind kind = RepositorySnapshotEntryKind.File) =>
        new(path, kind, new string('a', 40), kind == RepositorySnapshotEntryKind.Symlink ? "120000" : "100644", "blob");

    private static IReadOnlyList<string> LeafPaths(RepositorySnapshotTreeNode root)
    {
        var result = new List<string>();
        AddLeaves(root, result);
        return result;
    }

    private static void AddLeaves(RepositorySnapshotTreeNode node, ICollection<string> result)
    {
        if (node.Entry is not null)
        {
            result.Add(node.Path);
            return;
        }
        foreach (var child in node.Children) AddLeaves(child, result);
    }
}
