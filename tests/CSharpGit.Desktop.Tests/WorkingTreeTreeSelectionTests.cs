using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Desktop.Tests;

public sealed class WorkingTreeTreeSelectionTests
{
    [Fact]
    public void ClickCtrlAndCtrlToggleUseLeafOnlyDesktopSemantics()
    {
        var roots = Build("A.cs", "B.cs", "C.cs");
        var leaves = WorkingTreeTreeSelection.GetLeaves(roots);
        var selection = new WorkingTreeTreeSelection();

        selection.Apply(leaves[0], roots, controlPressed: false, shiftPressed: false);
        Assert.Equal(["A.cs"], selection.SelectedPaths);

        selection.Apply(leaves[1], roots, controlPressed: true, shiftPressed: false);
        Assert.Equal(new[] { "A.cs", "B.cs" }, selection.SelectedPaths.Order(StringComparer.Ordinal));

        selection.Apply(leaves[0], roots, controlPressed: true, shiftPressed: false);
        Assert.Equal(["B.cs"], selection.SelectedPaths);
    }

    [Fact]
    public void ShiftSelectsVisibleLeafRangeAndSkipsFolderRows()
    {
        var roots = Build("A.cs", "folder/B.cs", "folder/C.cs", "D.cs");
        var leaves = WorkingTreeTreeSelection.GetLeaves(roots);
        var selection = new WorkingTreeTreeSelection();

        selection.Apply(leaves[0], roots, controlPressed: false, shiftPressed: false);
        selection.Apply(leaves[^1], roots, controlPressed: false, shiftPressed: true);

        Assert.Equal(4, selection.SelectedPaths.Count);
        Assert.All(selection.GetSelectedLeaves(roots), node => Assert.NotNull(node.Change));
    }

    [Fact]
    public void FolderInvocationDoesNotChangeSelection()
    {
        var roots = Build("folder/A.cs", "folder/B.cs");
        var folder = Assert.Single(roots);
        var leaf = WorkingTreeTreeSelection.GetLeaves(roots)[0];
        var selection = new WorkingTreeTreeSelection();
        selection.Apply(leaf, roots, controlPressed: false, shiftPressed: false);

        selection.Apply(folder, roots, controlPressed: false, shiftPressed: false);

        Assert.Equal([leaf.Path], selection.SelectedPaths);
    }

    [Fact]
    public void CollapsedFolderLeavesDoNotParticipateInShiftRange()
    {
        var roots = Build("A.cs", "folder/B.cs", "folder/C.cs", "Z.cs");
        var folder = Assert.Single(roots, node => node.IsFolder);
        folder.IsExpanded = false;
        var visibleLeaves = WorkingTreeTreeSelection.GetLeaves(roots)
            .Where(node => node.Path is "A.cs" or "Z.cs")
            .ToArray();
        var selection = new WorkingTreeTreeSelection();

        selection.Apply(visibleLeaves[0], roots, controlPressed: false, shiftPressed: false);
        selection.Apply(visibleLeaves[1], roots, controlPressed: false, shiftPressed: true);

        Assert.Equal(new[] { "A.cs", "Z.cs" }, selection.SelectedPaths.Order(StringComparer.Ordinal));
    }

    private static IReadOnlyList<WorkingTreeTreeNode> Build(params string[] paths) =>
        WorkingTreeTreeNode.Build(
            paths.Select(path => new WorkingTreeChange(path, ' ', 'M')),
            WorkingTreeDiffKind.Unstaged);
}
