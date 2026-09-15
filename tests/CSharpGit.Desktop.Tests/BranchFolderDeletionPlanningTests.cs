using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Desktop.Tests;

public sealed class BranchFolderDeletionPlanningTests
{
    [Fact]
    public void CollectUsesRecursiveTreeStructureAndFullGitBranchIdentity()
    {
        var directBranch = new GitBranch("feature/direct", "1");
        var nestedBranch = new GitBranch("feature/auth/login", "2");
        var root = Folder(
            "feature",
            Leaf("display-only-direct", directBranch),
            Folder("auth", Leaf("display-only-login", nestedBranch)));

        var result = BranchFolderDeletionPlanner.Collect(
            root,
            node => node.Children,
            node => node.Kind == RepositoryTreeNodeKind.LocalBranch && node.Value is GitBranch branch
                ? new BranchFolderDeletionCandidate(branch, node.AssociatedWorktreePath)
                : null);

        Assert.Equal(new[] { "feature/direct", "feature/auth/login" }, result.Select(item => item.Branch.Name));
        Assert.DoesNotContain(result, item => item.Branch.Name == "display-only-login");
    }

    [Fact]
    public void LocalPlanSortsDeterministicallyAndSkipsCurrentBeforeWorktreeReason()
    {
        var candidates = new[]
        {
            new BranchFolderDeletionCandidate(new GitBranch("feature/b", "1"), null),
            new BranchFolderDeletionCandidate(new GitBranch("feature/B", "2"), null),
            new BranchFolderDeletionCandidate(new GitBranch("feature/current", "3", IsCurrent: true), "/tmp/current"),
            new BranchFolderDeletionCandidate(new GitBranch("feature/worktree", "4"), "/tmp/worktree"),
            new BranchFolderDeletionCandidate(new GitBranch("feature/a", "5"), null)
        };

        var plan = BranchFolderDeletionPlanner.CreateLocal(candidates);

        Assert.Equal(
            new[] { "feature/a", "feature/B", "feature/b", "feature/current", "feature/worktree" },
            plan.Branches.Select(item => item.Branch.Name));
        Assert.Equal(new[] { "feature/a", "feature/B", "feature/b" }, plan.EligibleBranches.Select(item => item.Branch.Name));
        Assert.Equal(5, plan.Total);
        Assert.Equal(2, plan.Skipped);
        Assert.Equal(3, plan.Attempted);
        Assert.Equal(BranchFolderDeletionSkipReason.CurrentBranch, plan.SkippedBranches[0].Reason);
        Assert.Equal(BranchFolderDeletionSkipReason.UsedByWorktree, plan.SkippedBranches[1].Reason);
    }

    [Fact]
    public void LocalPlanCanHaveZeroEligibleBranchesAndEligibilityDoesNotDependOnDeletionMode()
    {
        var candidates = new[]
        {
            new BranchFolderDeletionCandidate(new GitBranch("feature/current", "1", IsCurrent: true), null),
            new BranchFolderDeletionCandidate(new GitBranch("feature/worktree", "2"), "/tmp/worktree")
        };

        var safePlan = BranchFolderDeletionPlanner.CreateLocal(candidates);
        var forcePlan = BranchFolderDeletionPlanner.CreateLocal(candidates);

        Assert.Equal(0, safePlan.Attempted);
        Assert.Equal(2, safePlan.Skipped);
        Assert.Equal(
            safePlan.SkippedBranches.Select(item => (item.BranchName, item.Reason)),
            forcePlan.SkippedBranches.Select(item => (item.BranchName, item.Reason)));
        Assert.Equal(
            safePlan.EligibleBranches.Select(item => item.Branch.Name),
            forcePlan.EligibleBranches.Select(item => item.Branch.Name));
    }

    [Fact]
    public void RemoteTargetsRemoveExactConfiguredRemotePrefixAndPreserveNestedBranchName()
    {
        var folder = new BranchFolderInfo(BranchFolderScope.Remote, "feature", "company/origin");
        var candidates = new[]
        {
            new BranchFolderDeletionCandidate(new GitBranch("company/origin/feature/auth/login", "1"), null),
            new BranchFolderDeletionCandidate(new GitBranch("company/origin/feature/z", "2"), null)
        };

        var targets = BranchFolderDeletionPlanner.CreateRemoteTargets(folder, candidates);

        Assert.Equal("feature/auth/login", targets[0].RelativeBranchName);
        Assert.Equal("company/origin/feature/auth/login", targets[0].BranchName);
        Assert.Equal("feature/z", targets[1].RelativeBranchName);
    }

    [Fact]
    public void RemoteTargetsRejectInconsistentTreeStateInsteadOfGuessingAnotherRemote()
    {
        var folder = new BranchFolderInfo(BranchFolderScope.Remote, "feature", "origin");
        var candidates = new[]
        {
            new BranchFolderDeletionCandidate(new GitBranch("upstream/feature/a", "1"), null)
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            BranchFolderDeletionPlanner.CreateRemoteTargets(folder, candidates));

        Assert.Contains("configured remote 'origin'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecutorContinuesAfterPerBranchFailureAndPreservesOrder()
    {
        var items = new[] { "feature/a", "feature/b", "feature/c" };
        var calls = new List<string>();

        var result = await BranchFolderDeletionExecutor.ExecuteAsync(
            items,
            item => item,
            item =>
            {
                calls.Add(item);
                return item == "feature/b"
                    ? Task.FromException(new InvalidOperationException("not merged"))
                    : Task.CompletedTask;
            });

        Assert.Equal(items, calls);
        Assert.Equal(new[] { "feature/a", "feature/c" }, result.SuccessfulBranches);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("feature/b", failure.BranchName);
        Assert.Equal("not merged", failure.Message);
    }

    [Fact]
    public async Task ExecutorIsSequential()
    {
        var active = 0;
        var maxActive = 0;

        await BranchFolderDeletionExecutor.ExecuteAsync(
            new[] { "a", "b", "c" },
            item => item,
            async _ =>
            {
                active++;
                maxActive = Math.Max(maxActive, active);
                await Task.Yield();
                active--;
            });

        Assert.Equal(1, maxActive);
    }

    [Fact]
    public async Task ExecutorLetsOperationCanceledExceptionEscapeAndStopsFollowingDeletes()
    {
        var calls = new List<string>();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            BranchFolderDeletionExecutor.ExecuteAsync(
                new[] { "a", "b", "c" },
                item => item,
                item =>
                {
                    calls.Add(item);
                    return item == "b"
                        ? Task.FromException(new OperationCanceledException())
                        : Task.CompletedTask;
                }));

        Assert.Equal(new[] { "a", "b" }, calls);
    }

    private static RepositoryTreeDescriptor Folder(
        string name,
        params RepositoryTreeDescriptor[] children) =>
        new(
            $"folder:{name}:{Guid.NewGuid():N}",
            RepositoryTreeNodeKind.BranchFolder,
            name,
            ChildNodes: children);

    private static RepositoryTreeDescriptor Leaf(string displayName, GitBranch branch) =>
        new(
            $"branch:{branch.Name}",
            RepositoryTreeNodeKind.LocalBranch,
            displayName,
            branch.Name,
            branch);
}
