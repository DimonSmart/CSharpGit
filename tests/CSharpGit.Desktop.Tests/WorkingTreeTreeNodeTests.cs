using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Desktop.Tests;

public sealed class WorkingTreeTreeNodeTests
{
    [Fact]
    public void UsesStatusFromTreeKindAndBuildsBothRepresentations()
    {
        var change = new WorkingTreeChange("src/A.cs", 'A', 'M');

        var unstaged = Leaf(WorkingTreeTreeNode.Build([change], WorkingTreeDiffKind.Unstaged));
        var staged = Leaf(WorkingTreeTreeNode.Build([change], WorkingTreeDiffKind.Staged));

        Assert.Equal("M", unstaged.Status);
        Assert.Equal("A", staged.Status);
        Assert.Same(change, unstaged.Change);
        Assert.Same(change, staged.Change);
        Assert.Equal(string.Empty, Assert.Single(WorkingTreeTreeNode.Build([change], WorkingTreeDiffKind.Unstaged)).Status);
    }

    [Fact]
    public void NewFileStatusUsesQuestionMarkOnlyBeforeStaging()
    {
        var untracked = new WorkingTreeChange("new.txt", '?', '?');
        var stagedAdded = new WorkingTreeChange("new.txt", 'A', ' ');

        Assert.Equal("?", WorkingTreeTreeNode.FormatStatus(untracked, WorkingTreeDiffKind.Unstaged));
        Assert.Equal("A", WorkingTreeTreeNode.FormatStatus(untracked, WorkingTreeDiffKind.Staged));
        Assert.Equal("A", WorkingTreeTreeNode.FormatStatus(stagedAdded, WorkingTreeDiffKind.Staged));
    }

    [Fact]
    public void RenameUsesOnlyCurrentPathAndTooltipShowsOriginalPath()
    {
        var change = new WorkingTreeChange("new/A.cs", 'R', ' ', "old/A.cs");

        var leaf = Leaf(WorkingTreeTreeNode.Build([change], WorkingTreeDiffKind.Staged));

        Assert.Equal("new/A.cs", leaf.Path);
        Assert.Equal("old/A.cs → new/A.cs", leaf.ToolTipText);
        Assert.DoesNotContain("old", WorkingTreeTreeSelection.GetLeaves(WorkingTreeTreeNode.Build([change], WorkingTreeDiffKind.Staged)).Select(node => node.Path));
    }

    [Fact]
    public void AppliesHierarchyGuides()
    {
        var roots = WorkingTreeTreeNode.Build(
            [new WorkingTreeChange("src/A.cs", ' ', 'M'), new WorkingTreeChange("src/B.cs", ' ', 'M')],
            WorkingTreeDiffKind.Unstaged);

        var src = Assert.Single(roots);
        Assert.Empty(src.HierarchyGuideSegments);
        Assert.All(src.Children, child => Assert.NotEmpty(child.HierarchyGuideSegments));
    }

    [Fact]
    public void DescendantChangesForLeafReturnsOnlyItsChange()
    {
        var change = new WorkingTreeChange("A.cs", ' ', 'M');
        var leaf = Leaf(WorkingTreeTreeNode.Build([change], WorkingTreeDiffKind.Unstaged));

        Assert.Same(change, Assert.Single(leaf.GetDescendantChanges()));
    }

    [Fact]
    public void DescendantChangesForFolderIncludesNestedLeavesAndExcludesSiblingSubtree()
    {
        var fooA = new WorkingTreeChange("src/Foo/A.cs", ' ', 'M');
        var fooNested = new WorkingTreeChange("src/Foo/Nested/B.cs", ' ', 'M');
        var foobar = new WorkingTreeChange("src/Foobar/C.cs", ' ', 'M');
        var roots = WorkingTreeTreeNode.Build([fooA, fooNested, foobar], WorkingTreeDiffKind.Unstaged);

        var foo = FindNode(roots, "src/Foo");
        var changes = foo.GetDescendantChanges();

        Assert.Equal(2, changes.Count);
        Assert.Contains(fooA, changes);
        Assert.Contains(fooNested, changes);
        Assert.DoesNotContain(foobar, changes);
    }

    [Fact]
    public void DescendantChangesIgnoreExpansionState()
    {
        var first = new WorkingTreeChange("src/A.cs", ' ', 'M');
        var second = new WorkingTreeChange("src/Nested/B.cs", ' ', 'M');
        var folder = Assert.Single(WorkingTreeTreeNode.Build([first, second], WorkingTreeDiffKind.Unstaged));

        folder.IsExpanded = false;
        var collapsed = folder.GetDescendantChanges();
        folder.IsExpanded = true;
        var expanded = folder.GetDescendantChanges();

        Assert.Equal(collapsed, expanded);
        Assert.Equal(2, expanded.Count);
    }

    [Fact]
    public void DescendantChangesWorkForCollapsedSingleChildFolderChain()
    {
        var change = new WorkingTreeChange("A/B/C/File.cs", ' ', 'M');
        var root = Assert.Single(WorkingTreeTreeNode.Build([change], WorkingTreeDiffKind.Unstaged));

        Assert.True(root.IsFolder);
        Assert.Same(change, Assert.Single(root.GetDescendantChanges()));
    }

    [Fact]
    public void RenameBelongsToFolderByCurrentDisplayedPath()
    {
        var change = new WorkingTreeChange("src/Backend/A.cs", 'R', ' ', "old/A.cs");
        var roots = WorkingTreeTreeNode.Build([change], WorkingTreeDiffKind.Staged);
        var folder = Assert.Single(roots);

        Assert.Same(change, Assert.Single(folder.GetDescendantChanges()));
        Assert.DoesNotContain("old", folder.Path, StringComparison.Ordinal);
    }

    private static WorkingTreeTreeNode FindNode(
        IEnumerable<WorkingTreeTreeNode> nodes,
        string path)
    {
        foreach (var node in nodes)
        {
            if (string.Equals(node.Path, path, StringComparison.Ordinal)) return node;
            var match = FindNodeOrDefault(node.Children, path);
            if (match is not null) return match;
        }

        throw new InvalidOperationException($"Working Tree node '{path}' was not found.");
    }

    private static WorkingTreeTreeNode? FindNodeOrDefault(
        IEnumerable<WorkingTreeTreeNode> nodes,
        string path)
    {
        foreach (var node in nodes)
        {
            if (string.Equals(node.Path, path, StringComparison.Ordinal)) return node;
            var match = FindNodeOrDefault(node.Children, path);
            if (match is not null) return match;
        }

        return null;
    }

    private static WorkingTreeTreeNode Leaf(IReadOnlyList<WorkingTreeTreeNode> roots) =>
        Assert.Single(WorkingTreeTreeSelection.GetLeaves(roots));
}
