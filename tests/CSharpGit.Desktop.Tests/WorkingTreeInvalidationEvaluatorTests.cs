using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation;

namespace CSharpGit.Desktop.Tests;

public sealed class WorkingTreeInvalidationEvaluatorTests
{
    [Fact]
    public void ChangedStatusSnapshotRequiresRefresh()
    {
        var displayed = Snapshot(new WorkingTreeStatusEntry("a.cs", "1 .M N... 100644 100644 100644 aaa aaa a.cs"));
        var current = Snapshot(new WorkingTreeStatusEntry("a.cs", "1 M. N... 100644 100644 100644 aaa bbb a.cs"));
        var invalidation = Batch("a.cs");

        var reason = WorkingTreeInvalidationEvaluator.Evaluate(
            displayed,
            current,
            invalidation);

        Assert.Equal(WorkingTreeInvalidationReason.StatusChanged, reason);
    }

    [Fact]
    public void RepeatedEditOfAlreadyModifiedPathRequiresRefreshEvenWhenStatusIsEqual()
    {
        var snapshot = Snapshot(new WorkingTreeStatusEntry("a.cs", "1 .M N... 100644 100644 100644 aaa aaa a.cs"));

        var reason = WorkingTreeInvalidationEvaluator.Evaluate(
            snapshot,
            snapshot,
            Batch("a.cs"));

        Assert.Equal(WorkingTreeInvalidationReason.GitVisiblePathInvalidated, reason);
    }

    [Fact]
    public void RepeatedEditOfUntrackedPathRequiresRefreshEvenWhenStatusIsEqual()
    {
        var snapshot = Snapshot(new WorkingTreeStatusEntry("notes.tmp", "? notes.tmp"));

        var reason = WorkingTreeInvalidationEvaluator.Evaluate(
            snapshot,
            snapshot,
            Batch("notes.tmp"));

        Assert.Equal(WorkingTreeInvalidationReason.GitVisiblePathInvalidated, reason);
    }

    [Fact]
    public void IgnoredPathDoesNotRequireRefreshWhenStatusIsEqual()
    {
        var snapshot = Snapshot(new WorkingTreeStatusEntry("src/a.cs", "1 .M N... 100644 100644 100644 aaa aaa src/a.cs"));

        var reason = WorkingTreeInvalidationEvaluator.Evaluate(
            snapshot,
            snapshot,
            Batch("obj/cache.tmp"));

        Assert.Equal(WorkingTreeInvalidationReason.None, reason);
    }

    [Fact]
    public void DirectoryInvalidationIntersectsGitVisibleDescendant()
    {
        var snapshot = Snapshot(new WorkingTreeStatusEntry("src/a.cs", "1 .M N... 100644 100644 100644 aaa aaa src/a.cs"));

        var reason = WorkingTreeInvalidationEvaluator.Evaluate(
            snapshot,
            snapshot,
            Batch("src"));

        Assert.Equal(WorkingTreeInvalidationReason.GitVisiblePathInvalidated, reason);
    }

    [Fact]
    public void EventInsideDirtySubmoduleIntersectsSubmoduleRoot()
    {
        var snapshot = Snapshot(
            new WorkingTreeStatusEntry(
                "vendor/library",
                "1 .M S.M. 160000 160000 160000 aaa aaa vendor/library",
                IsSubmodule: true));

        var reason = WorkingTreeInvalidationEvaluator.Evaluate(
            snapshot,
            snapshot,
            Batch("vendor/library/src/file.cs"));

        Assert.Equal(WorkingTreeInvalidationReason.GitVisiblePathInvalidated, reason);
    }

    [Fact]
    public void RenameOriginalPathAlsoParticipatesInIntersection()
    {
        var snapshot = Snapshot(
            new WorkingTreeStatusEntry(
                "new.cs",
                "2 R. N... 100644 100644 100644 aaa bbb R100 new.cs\0old.cs",
                "old.cs"));

        var reason = WorkingTreeInvalidationEvaluator.Evaluate(
            snapshot,
            snapshot,
            Batch("old.cs"));

        Assert.Equal(WorkingTreeInvalidationReason.GitVisiblePathInvalidated, reason);
    }

    private static WorkingTreeStatusSnapshot Snapshot(
        params WorkingTreeStatusEntry[] entries) =>
        new(entries);

    private static RepositoryInvalidationBatch Batch(params string[] paths) =>
        new(
            generation: 1,
            hasWorkingTreeChanges: true,
            hasRelevantMetadataChanges: false,
            hasUnknownOrOverflow: false,
            workingTreePaths: paths);
}
