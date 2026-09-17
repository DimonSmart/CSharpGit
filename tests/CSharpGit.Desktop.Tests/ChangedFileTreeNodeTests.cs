using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Desktop.Tests;

public sealed class ChangedFileTreeNodeTests
{
    [Fact]
    public void BuildsCompactHierarchyAndAggregatesStatistics()
    {
        var roots = ChangedFileTreeNode.Build(
        [
            Entry("M", ".idd/intent/spec.md", 3, 1),
            Entry("M", "src/CSharpGit.Presentation/Controls/Widget.cs", 5, 2),
            Entry("A", "src/CSharpGit.Presentation/ViewModels/NewNode.cs", 12, 0),
            Entry("M", "src/App.cs", 2, 4),
            Entry("M", "README.md", 1, 1)
        ]);

        Assert.Equal(3, roots.Count);

        var intent = Assert.Single(roots, node => node.DisplayName == ".idd/intent");
        var spec = Assert.Single(intent.Children);
        Assert.Equal("spec.md", spec.DisplayName);
        Assert.Equal("M", spec.Status);
        Assert.Equal("+3", intent.AddedDisplay);
        Assert.Equal("-1", intent.RemovedDisplay);

        var src = Assert.Single(roots, node => node.DisplayName == "src");
        Assert.Equal(19, src.AddedLines);
        Assert.Equal(6, src.RemovedLines);
        Assert.Contains(src.Children, node => node.DisplayName == "CSharpGit.Presentation");
        Assert.Contains(src.Children, node => node.DisplayName == "App.cs");

        var presentation = Assert.Single(src.Children, node => node.DisplayName == "CSharpGit.Presentation");
        Assert.Equal(2, presentation.Children.Count);
        Assert.Contains(presentation.Children, node => node.DisplayName == "Controls");
        Assert.Contains(presentation.Children, node => node.DisplayName == "ViewModels");
    }

    [Fact]
    public void KeepsRootFilesAndDeduplicatesSamePath()
    {
        var roots = ChangedFileTreeNode.Build(
        [
            Entry("M", "README.md", 4, 2),
            Entry("M", "README.md", 99, 99)
        ]);

        var readme = Assert.Single(roots);
        Assert.Equal("README.md", readme.DisplayName);
        Assert.Equal("+4", readme.AddedDisplay);
        Assert.Equal("-2", readme.RemovedDisplay);
        Assert.Empty(readme.Children);
    }

    [Fact]
    public void IdenticalTreePreservesInstancesExpansionAndHasNoCollectionMutations()
    {
        var roots = new ObservableCollection<ChangedFileTreeNode>();
        ChangedFileTreeSynchronizer.Reconcile(roots,
        [
            Entry("M", "src/A.cs", 1, 2),
            Entry("A", "src/B.cs", 3, 0)
        ]);
        var src = Assert.Single(roots);
        var a = Assert.Single(src.Children, node => node.Path == "src/A.cs");
        src.IsExpanded = false;

        var rootChanges = new List<NotifyCollectionChangedAction>();
        var childChanges = new List<NotifyCollectionChangedAction>();
        roots.CollectionChanged += (_, args) => rootChanges.Add(args.Action);
        src.Children.CollectionChanged += (_, args) => childChanges.Add(args.Action);

        ChangedFileTreeSynchronizer.Reconcile(roots,
        [
            Entry("M", "src/A.cs", 1, 2),
            Entry("A", "src/B.cs", 3, 0)
        ]);

        Assert.Same(src, Assert.Single(roots));
        Assert.Same(a, Assert.Single(src.Children, node => node.Path == "src/A.cs"));
        Assert.False(src.IsExpanded);
        Assert.Empty(rootChanges);
        Assert.Empty(childChanges);
    }

    [Fact]
    public void MetadataChangeReusesNodesUpdatesDomainObjectAndDirectoryAggregates()
    {
        var roots = new ObservableCollection<ChangedFileTreeNode>();
        ChangedFileTreeSynchronizer.Reconcile(roots,
        [
            Entry("M", "src/A.cs", 1, 2),
            Entry("M", "src/B.cs", 2, 1)
        ]);
        var src = Assert.Single(roots);
        var a = Assert.Single(src.Children, node => node.Path == "src/A.cs");
        var oldFile = a.Entry!.File;
        var aProperties = new List<string?>();
        var srcProperties = new List<string?>();
        a.PropertyChanged += (_, args) => aProperties.Add(args.PropertyName);
        src.PropertyChanged += (_, args) => srcProperties.Add(args.PropertyName);

        var replacement = new ChangedFile("src/A.cs", 8, 4, false);
        ChangedFileTreeSynchronizer.Reconcile(roots,
        [
            new ChangedFileTreeEntry("A", replacement),
            Entry("M", "src/B.cs", 2, 1)
        ]);

        Assert.Same(src, Assert.Single(roots));
        Assert.Same(a, Assert.Single(src.Children, node => node.Path == "src/A.cs"));
        Assert.NotSame(oldFile, a.Entry!.File);
        Assert.Same(replacement, a.Entry.File);
        Assert.Equal("A", a.Status);
        Assert.Equal(10, src.AddedLines);
        Assert.Equal(5, src.RemovedLines);
        Assert.Contains(nameof(ChangedFileTreeNode.Entry), aProperties);
        Assert.Contains(nameof(ChangedFileTreeNode.Status), aProperties);
        Assert.Contains(nameof(ChangedFileTreeNode.AddedDisplay), aProperties);
        Assert.Contains(nameof(ChangedFileTreeNode.AddedLines), srcProperties);
        Assert.Contains(nameof(ChangedFileTreeNode.RemovedLines), srcProperties);
    }

    [Fact]
    public void AddAndRemoveOnlyMutateAffectedSiblings()
    {
        var roots = new ObservableCollection<ChangedFileTreeNode>();
        ChangedFileTreeSynchronizer.Reconcile(roots,
        [
            Entry("M", "src/A.cs", 1, 0),
            Entry("M", "src/B.cs", 1, 0)
        ]);
        var src = Assert.Single(roots);
        var a = Assert.Single(src.Children, node => node.Path == "src/A.cs");
        var actions = new List<NotifyCollectionChangedAction>();
        src.Children.CollectionChanged += (_, args) => actions.Add(args.Action);

        ChangedFileTreeSynchronizer.Reconcile(roots,
        [
            Entry("M", "src/A.cs", 1, 0),
            Entry("A", "src/C.cs", 1, 0)
        ]);

        Assert.Same(src, Assert.Single(roots));
        Assert.Same(a, Assert.Single(src.Children, node => node.Path == "src/A.cs"));
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, actions);
        Assert.Contains(NotifyCollectionChangedAction.Remove, actions);
        Assert.Contains(NotifyCollectionChangedAction.Add, actions);
    }

    [Fact]
    public void FileAndDirectoryAtSamePathHaveDifferentIdentity()
    {
        var roots = new ObservableCollection<ChangedFileTreeNode>();
        ChangedFileTreeSynchronizer.Reconcile(roots, [Entry("M", "src", 1, 0)]);
        var file = Assert.Single(roots);

        ChangedFileTreeSynchronizer.Reconcile(roots, [Entry("M", "src/A.cs", 1, 0)]);

        var directory = Assert.Single(roots);
        Assert.NotSame(file, directory);
        Assert.Null(directory.Entry);
    }

    [Fact]
    public void CaseSensitivePathsRemainDistinct()
    {
        var roots = new ObservableCollection<ChangedFileTreeNode>();
        ChangedFileTreeSynchronizer.Reconcile(roots,
        [
            Entry("M", "src/Case.cs", 1, 0),
            Entry("M", "src/case.cs", 1, 0)
        ]);

        var src = Assert.Single(roots);
        Assert.Equal(2, src.Children.Count);
        Assert.Contains(src.Children, node => node.Path == "src/Case.cs");
        Assert.Contains(src.Children, node => node.Path == "src/case.cs");
    }

    private static ChangedFileTreeEntry Entry(string status, string path, int added, int removed) =>
        new(status, new ChangedFile(path, added, removed, false));
}
