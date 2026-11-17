using CSharpGit.Domain;
using CSharpGit.Presentation;

namespace CSharpGit.Desktop.Tests;

public sealed class RepositoryTreeNavigationCoordinatorTests
{
    private static readonly Repository RepositoryA = CreateRepository("a");
    private static readonly Repository RepositoryB = CreateRepository("b");

    [Fact]
    public void NewInteractionInvalidatesPreviouslyDeferredRequest()
    {
        var coordinator = new RepositoryTreeNavigationCoordinator();
        var first = coordinator.BeginInteraction();
        coordinator.DeferCheckout(RepositoryA, first, "main");

        var next = coordinator.BeginInteraction();
        Assert.Equal(first + 1, next);
        Assert.False(coordinator.HasPendingCheckout);
        Assert.False(coordinator.TryTakeCheckout(RepositoryA, out _));
    }

    [Fact]
    public void RepositoryChangeRejectsDelayedCheckoutEvenWhenReturningToSameRepository()
    {
        var coordinator = new RepositoryTreeNavigationCoordinator();
        var version = coordinator.BeginInteraction();
        coordinator.DeferCheckout(RepositoryA, version, "feature");
        coordinator.ResetForRepositoryChange();
        coordinator.ResetForRepositoryChange();

        Assert.False(coordinator.TryTakeCheckout(RepositoryA, out _));
    }

    [Fact]
    public void ReferenceRequestIsRejectedWhenUserSelectsAnotherHistoryRow()
    {
        var coordinator = new RepositoryTreeNavigationCoordinator();
        var version = coordinator.BeginInteraction();
        coordinator.DeferReference(RepositoryA, version, 10, "oid", preserveSelectedStash: true);

        Assert.False(coordinator.TryTakeReference(RepositoryA, 11, out _));
        Assert.False(coordinator.HasPendingReference);
    }

    [Fact]
    public void RequestIsNotResumedAgainstAnotherRepositoryInstance()
    {
        var coordinator = new RepositoryTreeNavigationCoordinator();
        var version = coordinator.BeginInteraction();
        coordinator.DeferReference(RepositoryA, version, 1, "oid", preserveSelectedStash: false);

        Assert.False(coordinator.TryTakeReference(RepositoryB, 1, out _));
    }

    [Fact]
    public void ValidReferenceCanBeTakenExactlyOnce()
    {
        var coordinator = new RepositoryTreeNavigationCoordinator();
        var version = coordinator.BeginInteraction();
        coordinator.DeferReference(RepositoryA, version, 42, "commit", preserveSelectedStash: true);

        Assert.True(coordinator.TryTakeReference(RepositoryA, 42, out var pending));
        Assert.Equal(version, pending.Version);
        Assert.Equal("commit", pending.CommitHash);
        Assert.True(pending.PreserveSelectedStash);
        Assert.False(coordinator.TryTakeReference(RepositoryA, 42, out _));
    }

    [Fact]
    public void MostRecentDeferredCheckoutWinsWithinOneInteraction()
    {
        var coordinator = new RepositoryTreeNavigationCoordinator();
        var version = coordinator.BeginInteraction();
        coordinator.DeferCheckout(RepositoryA, version, "old");
        coordinator.DeferCheckout(RepositoryA, version, "new");

        Assert.True(coordinator.TryTakeCheckout(RepositoryA, out var pending));
        Assert.Equal("new", pending.BranchName);
        Assert.False(coordinator.HasPendingCheckout);
    }

    private static Repository CreateRepository(string name) =>
        new($"/workspace/{name}", $"/workspace/{name}", $"/workspace/{name}/.git", false);
}
