using System.Globalization;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Desktop.Tests;

public sealed class WorktreePresentationTests
{
    [Fact]
    public void BranchWorktreeUsesFullBranchNameAsPrimaryLabel()
    {
        var worktree = Worktree("/repo-feature", "1234567890", "feature/rec-050-006-add-feedback-e2e");

        Assert.Equal("feature/rec-050-006-add-feedback-e2e", WorktreePresentation.GetPrimaryLabel(worktree));
    }

    [Theory]
    [InlineData("E:\\Work\\experiments\\detached-test\\", "1234567890", "detached-test")]
    [InlineData("/work/experiments/detached-test/", "1234567890", "detached-test")]
    [InlineData("/", "1234567890", "12345678")]
    [InlineData("/", "", "detached")]
    public void DetachedWorktreeUsesDirectoryThenDeterministicFallback(string path, string head, string expected)
    {
        var worktree = Worktree(path, head, branch: null, detached: true);

        Assert.Equal(expected, WorktreePresentation.GetPrimaryLabel(worktree));
    }

    [Fact]
    public void StateAndTooltipKeepFullSemanticValues()
    {
        var worktree = Worktree(
            "E:\\Work\\repo-feature",
            "1234567890",
            "feature/very-long-name",
            current: true,
            primary: true,
            detached: false,
            locked: true,
            lockReason: "temporary experiment",
            prunable: true);

        Assert.Equal("[locked, prunable]", WorktreePresentation.GetStateIndicator(worktree));
        var tooltip = WorktreePresentation.BuildToolTip(worktree);
        Assert.Contains("Branch: feature/very-long-name", tooltip, StringComparison.Ordinal);
        Assert.Contains("Path: E:\\Work\\repo-feature", tooltip, StringComparison.Ordinal);
        Assert.Contains("Primary worktree", tooltip, StringComparison.Ordinal);
        Assert.Contains("Current worktree", tooltip, StringComparison.Ordinal);
        Assert.Contains("State: Locked, Prunable", tooltip, StringComparison.Ordinal);
        Assert.Contains("Lock reason: temporary experiment", tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void TooltipAddsPrimaryAndCurrentIndependently()
    {
        var primary = Worktree("/repo-primary", "11111111", "feature/a", primary: true);
        var current = Worktree("/repo-current", "22222222", "feature/b", current: true);

        var primaryTooltip = WorktreePresentation.BuildToolTip(primary);
        Assert.Contains("Primary worktree", primaryTooltip, StringComparison.Ordinal);
        Assert.DoesNotContain("Current worktree", primaryTooltip, StringComparison.Ordinal);

        var currentTooltip = WorktreePresentation.BuildToolTip(current);
        Assert.DoesNotContain("Primary worktree", currentTooltip, StringComparison.Ordinal);
        Assert.Contains("Current worktree", currentTooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void ClipboardValuesAreExactAndDetachedHasNoBranchValue()
    {
        var branchWorktree = Worktree("E:\\Work\\repo-feature", "12345678", "feature/exact-name");
        var detached = Worktree("E:\\Work\\detached", "abcdef12", branch: null, detached: true);

        Assert.Equal("feature/exact-name", WorktreePresentation.GetBranchNameForCopy(branchWorktree));
        Assert.Equal("E:\\Work\\repo-feature", WorktreePresentation.GetPathForCopy(branchWorktree));
        Assert.Null(WorktreePresentation.GetBranchNameForCopy(detached));
        Assert.Equal("E:\\Work\\detached", WorktreePresentation.GetPathForCopy(detached));
    }

    [Fact]
    public void WorktreesSortPrimaryFirstThenByFullPrimaryLabelIndependentlyOfCurrent()
    {
        var primary = Worktree("/repo-primary", "11111111", "z-primary", primary: true);
        var current = Worktree("/repo-beta", "22222222", "beta", current: true);
        var alpha = Worktree("/repo-alpha", "33333333", "alpha");

        var root = RepositoryTreeDescriptorBuilder.BuildWorktreesRoot([current, alpha, primary]);

        Assert.Equal(
            new[] { "worktree:/repo-primary", "worktree:/repo-alpha", "worktree:/repo-beta" },
            root.Children.Select(child => child.Key).ToArray());
    }

    [Fact]
    public void WorktreesUsePathAsDeterministicTieBreaker()
    {
        var later = Worktree("/repo-z", "11111111", "same");
        var earlier = Worktree("/repo-a", "22222222", "SAME");

        var root = RepositoryTreeDescriptorBuilder.BuildWorktreesRoot([later, earlier]);

        Assert.Equal(
            new[] { "worktree:/repo-a", "worktree:/repo-z" },
            root.Children.Select(child => child.Key).ToArray());
    }

    [Fact]
    public void MiddleEllipsisKeepsFullTextWhenItFits()
    {
        Assert.Equal("feature/short-name", MiddleEllipsis.Fit("feature/short-name", 20, GraphemeWidth));
    }

    [Fact]
    public void MiddleEllipsisPreservesPrefixAndSlightlyLongerSuffix()
    {
        Assert.Equal("ab…hij", MiddleEllipsis.Fit("abcdefghij", 6, GraphemeWidth));
        Assert.Equal("…", MiddleEllipsis.Fit("abcdefghij", 1, GraphemeWidth));
    }

    [Fact]
    public void MiddleEllipsisDoesNotSplitGraphemeClusters()
    {
        var result = MiddleEllipsis.Fit("abxx👩‍💻", 4, GraphemeWidth);

        Assert.Equal("a…x👩‍💻", result);
        Assert.DoesNotContain('\uFFFD', result);
    }

    private static double GraphemeWidth(string text) => StringInfo.ParseCombiningCharacters(text).Length;

    private static WorktreeInfo Worktree(
        string path,
        string head,
        string? branch,
        bool current = false,
        bool primary = false,
        bool detached = false,
        bool locked = false,
        string? lockReason = null,
        bool prunable = false) =>
        new(path, head, branch, current, detached, locked, lockReason, prunable)
        {
            IsPrimary = primary
        };
}
