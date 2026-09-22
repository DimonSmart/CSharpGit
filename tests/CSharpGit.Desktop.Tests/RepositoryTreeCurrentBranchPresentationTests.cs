using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;

namespace CSharpGit.Desktop.Tests;

public sealed class RepositoryTreeCurrentBranchPresentationTests
{
    [Fact]
    public void CurrentLocalBranchPreservesTopLevelHierarchyTopology()
    {
        var branches = Node("group:branches", RepositoryTreeNodeKind.Group, "Branches");
        var develop = Node("local-branch:develop", RepositoryTreeNodeKind.LocalBranch, "develop", isCurrent: true);
        var main = Node("local-branch:main", RepositoryTreeNodeKind.LocalBranch, "main");
        branches.Children.Add(develop);
        branches.Children.Add(main);

        ApplyGuides(branches);

        Assert.Equal(
            new[] { RepositoryTreeGuideSegmentKind.Branch },
            develop.HierarchyGuideSegments);
        Assert.Equal(
            new[] { RepositoryTreeGuideSegmentKind.Last },
            main.HierarchyGuideSegments);
    }

    [Fact]
    public void CurrentNestedBranchPreservesAncestorContinuationAndTerminalConnector()
    {
        var branches = Node("group:branches", RepositoryTreeNodeKind.Group, "Branches");
        var feature = Node("branch-folder:local:feature", RepositoryTreeNodeKind.BranchFolder, "feature");
        var test = Node("local-branch:feature/test", RepositoryTreeNodeKind.LocalBranch, "test", isCurrent: true);
        var main = Node("local-branch:main", RepositoryTreeNodeKind.LocalBranch, "main");
        feature.Children.Add(test);
        branches.Children.Add(feature);
        branches.Children.Add(main);

        ApplyGuides(branches);

        Assert.Equal(
            new[] { RepositoryTreeGuideSegmentKind.Branch },
            feature.HierarchyGuideSegments);
        Assert.Equal(
            new[]
            {
                RepositoryTreeGuideSegmentKind.Continue,
                RepositoryTreeGuideSegmentKind.Last
            },
            test.HierarchyGuideSegments);
        Assert.Equal(
            new[] { RepositoryTreeGuideSegmentKind.Last },
            main.HierarchyGuideSegments);
    }

    [Fact]
    public void CurrentWorktreePreservesHierarchyTopology()
    {
        var worktrees = Node("group:worktrees", RepositoryTreeNodeKind.Group, "Worktrees");
        var current = Node("worktree:/repo", RepositoryTreeNodeKind.Worktree, "main", isCurrent: true);
        var secondary = Node("worktree:/repo-feature", RepositoryTreeNodeKind.Worktree, "feature");
        worktrees.Children.Add(current);
        worktrees.Children.Add(secondary);

        ApplyGuides(worktrees);

        Assert.Equal(
            new[] { RepositoryTreeGuideSegmentKind.Branch },
            current.HierarchyGuideSegments);
        Assert.Equal(
            new[] { RepositoryTreeGuideSegmentKind.Last },
            secondary.HierarchyGuideSegments);
    }

    [Fact]
    public void CurrentBranchNameAccentIsScopedToLocalBranches()
    {
        var currentLocal = Node("local-branch:develop", RepositoryTreeNodeKind.LocalBranch, "develop", isCurrent: true);
        var regularLocal = Node("local-branch:main", RepositoryTreeNodeKind.LocalBranch, "main");
        var currentRemote = Node("remote-branch:origin/main", RepositoryTreeNodeKind.RemoteBranch, "main", isCurrent: true);
        var currentWorktree = Node("worktree:/repo", RepositoryTreeNodeKind.Worktree, "main", isCurrent: true);

        Assert.True(currentLocal.IsCurrentLocalBranch);
        Assert.Equal(Visibility.Visible, currentLocal.CurrentBranchNameAccentVisibility);
        Assert.Equal(Visibility.Collapsed, currentLocal.CurrentWorktreeAccentVisibility);

        Assert.False(regularLocal.IsCurrentLocalBranch);
        Assert.Equal(Visibility.Collapsed, regularLocal.CurrentBranchNameAccentVisibility);

        Assert.False(currentRemote.IsCurrentLocalBranch);
        Assert.Equal(Visibility.Collapsed, currentRemote.CurrentBranchNameAccentVisibility);
        Assert.Equal(Visibility.Collapsed, currentRemote.CurrentWorktreeAccentVisibility);

        Assert.False(currentWorktree.IsCurrentLocalBranch);
        Assert.Equal(Visibility.Collapsed, currentWorktree.CurrentBranchNameAccentVisibility);
        Assert.Equal(Visibility.Visible, currentWorktree.CurrentWorktreeAccentVisibility);
    }

    [Fact]
    public void SwitchingCurrentStateDoesNotChangeExistingHierarchySegments()
    {
        var develop = Node("local-branch:develop", RepositoryTreeNodeKind.LocalBranch, "develop", isCurrent: true);
        develop.SetHierarchyGuideSegments(
        [
            RepositoryTreeGuideSegmentKind.Continue,
            RepositoryTreeGuideSegmentKind.Branch
        ]);
        var original = develop.HierarchyGuideSegments.ToArray();

        develop.UpdateFrom(new RepositoryTreeDescriptor(
            "local-branch:develop",
            RepositoryTreeNodeKind.LocalBranch,
            "develop",
            IsCurrent: false));

        Assert.Equal(original, develop.HierarchyGuideSegments);
        Assert.Equal(Visibility.Collapsed, develop.CurrentBranchNameAccentVisibility);
    }

    [Fact]
    public void RepositoryTreeUsesNameOnlyAccentAndKeepsStockTreeViewSelection()
    {
        var root = FindRepositoryRoot();
        var repositoryTree = File.ReadAllText(
            Path.Combine(root, "src", "CSharpGit.Presentation", "Styles", "RepositoryTree.xaml"));

        Assert.Contains(
            "BasedOn=\"{StaticResource DenseTreeItemStyle}\"",
            repositoryTree,
            StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentBranchAccentVisibility", repositoryTree, StringComparison.Ordinal);
        Assert.Contains(
            "Visibility=\"{Binding CurrentWorktreeAccentVisibility}\"",
            repositoryTree,
            StringComparison.Ordinal);
        Assert.Equal(
            2,
            CountOccurrences(repositoryTree, "Visibility=\"{Binding CurrentBranchNameAccentVisibility}\""));
        Assert.Equal(
            2,
            CountOccurrences(repositoryTree, "TextForeground=\"{ThemeResource AccentFillColorDefaultBrush}\""));
        Assert.Contains(
            "<ScaleTransform ScaleX=\"1.04\" ScaleY=\"1.08\" />",
            repositoryTree,
            StringComparison.Ordinal);
    }

    private static RepositoryTreeNode Node(
        string key,
        RepositoryTreeNodeKind kind,
        string name,
        bool isCurrent = false) =>
        new(new RepositoryTreeDescriptor(key, kind, name, IsCurrent: isCurrent));

    private static void ApplyGuides(RepositoryTreeNode root) =>
        TreeHierarchyGuideBuilder.Apply(
            new[] { root },
            node => node.Children,
            (node, segments) => node.SetHierarchyGuideSegments(segments));

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate CSharpGit repository root.");
    }
}
