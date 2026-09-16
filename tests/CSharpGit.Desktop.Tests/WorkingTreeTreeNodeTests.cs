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

    private static WorkingTreeTreeNode Leaf(IReadOnlyList<WorkingTreeTreeNode> roots) =>
        Assert.Single(WorkingTreeTreeSelection.GetLeaves(roots));
}
