using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation;
using CSharpGit.Presentation.Threading;

namespace CSharpGit.Desktop.Tests;

public sealed class RepositoryChangeCoordinatorTests
{
    [Fact]
    public void MetadataChangeRequiresRefreshUntilNewBaseline()
    {
        var repository = CreateRepository();
        try
        {
            using var coordinator = NewCoordinator(WorkingTreeStatusSnapshot.Empty);
            coordinator.Initialize();
            coordinator.ChangeRepository(repository, WorkingTreeStatusSnapshot.Empty, 10);

            var notifications = 0;
            coordinator.RefreshRequirementChanged += () => notifications++;
            coordinator.HandleInvalidationBatch(MetadataChange(1));

            Assert.True(coordinator.IsRefreshRequired);
            coordinator.PublishBaseline(repository, WorkingTreeStatusSnapshot.Empty, 11);
            Assert.False(coordinator.IsRefreshRequired);
            Assert.Equal(2, notifications);
        }
        finally
        {
            DeleteRepository(repository);
        }
    }

    [Fact]
    public void QueuedDeliveryFromPreviousSessionCannotLatchAfterAtoBtoA()
    {
        var repository = CreateRepository();
        try
        {
            using var coordinator = NewCoordinator(WorkingTreeStatusSnapshot.Empty);
            coordinator.Initialize();
            coordinator.ChangeRepository(repository, WorkingTreeStatusSnapshot.Empty, 1);
            var staleDelivery = coordinator.CapturePendingMonitorDeliveryForDesktopCheck();

            coordinator.ChangeRepository(repository with { }, WorkingTreeStatusSnapshot.Empty, 2);
            coordinator.ChangeRepository(repository with { }, WorkingTreeStatusSnapshot.Empty, 3);
            staleDelivery();

            Assert.False(coordinator.IsRefreshRequired);
        }
        finally
        {
            DeleteRepository(repository);
        }
    }

    [Fact]
    public void ChangedGitStatusRequiresRefresh()
    {
        var repository = CreateRepository();
        try
        {
            var baseline = new WorkingTreeStatusSnapshot([new WorkingTreeStatusEntry("tracked.txt", "old")]);
            var current = new WorkingTreeStatusSnapshot([new WorkingTreeStatusEntry("tracked.txt", "new")]);
            using var coordinator = NewCoordinator(current);
            coordinator.Initialize();
            coordinator.ChangeRepository(repository, baseline, 1);

            coordinator.HandleInvalidationBatch(new RepositoryInvalidationBatch(
                1,
                hasWorkingTreeChanges: true,
                hasRelevantMetadataChanges: false,
                hasUnknownOrOverflow: false,
                workingTreePaths: ["tracked.txt"]));

            Assert.True(coordinator.IsRefreshRequired);
        }
        finally
        {
            DeleteRepository(repository);
        }
    }

    [Fact]
    public void UnrelatedFileChangeWithSameGitStatusDoesNotRequireRefresh()
    {
        var repository = CreateRepository();
        try
        {
            using var coordinator = NewCoordinator(WorkingTreeStatusSnapshot.Empty);
            coordinator.Initialize();
            coordinator.ChangeRepository(repository, WorkingTreeStatusSnapshot.Empty, 1);

            coordinator.HandleInvalidationBatch(new RepositoryInvalidationBatch(
                1,
                hasWorkingTreeChanges: true,
                hasRelevantMetadataChanges: false,
                hasUnknownOrOverflow: false,
                workingTreePaths: ["ignored.tmp"]));

            Assert.False(coordinator.IsRefreshRequired);
        }
        finally
        {
            DeleteRepository(repository);
        }
    }

    private static RepositoryChangeCoordinator NewCoordinator(WorkingTreeStatusSnapshot snapshot) =>
        new(new SnapshotReader(snapshot), new InlineDispatcher());

    private static RepositoryInvalidationBatch MetadataChange(long generation) =>
        new(generation,
            hasWorkingTreeChanges: false,
            hasRelevantMetadataChanges: true,
            hasUnknownOrOverflow: false,
            metadataPaths: ["HEAD"]);

    private static Repository CreateRepository()
    {
        var root = Path.Combine(Path.GetTempPath(), $"csharpgit-refresh-coordinator-{Guid.NewGuid():N}");
        var gitDirectory = Path.Combine(root, ".git");
        Directory.CreateDirectory(gitDirectory);
        return new Repository(root, root, gitDirectory, false);
    }

    private static void DeleteRepository(Repository repository) =>
        Directory.Delete(repository.WorkingDirectory, recursive: true);

    private sealed class SnapshotReader(WorkingTreeStatusSnapshot snapshot) : IWorkingTreeStatusReader
    {
        public Task<WorkingTreeStatusSnapshot> ReadAsync(
            Repository repository, CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);
    }

    private sealed class InlineDispatcher : IUiDispatcher
    {
        public bool HasThreadAccess => true;

        public bool TryEnqueue(Action action)
        {
            action();
            return true;
        }
    }
}
