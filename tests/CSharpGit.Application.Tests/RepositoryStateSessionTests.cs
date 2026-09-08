using CSharpGit.Application;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Application.Tests;

public sealed class RepositoryStateSessionTests
{
    [Fact]
    public async Task PollingPublishesExternalGitStateChangesWithinReasonableTime()
    {
        var repository = new Repository("work", "root", "git", false);
        var service = new FakeStateService(repository);
        await using var session = await new RepositoryStateSessionFactory(service).CreateAsync(repository);
        var changed = new TaskCompletionSource<RepositoryState>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.StateChanged += (_, state) => changed.TrySetResult(state);

        service.ExternalHead = "external-commit";
        var observed = await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("external-commit", observed.HeadCommit);
        Assert.Equal("external-commit", session.Current.HeadCommit);
    }

    [Fact]
    public async Task RefreshesStateEvenWhenMutationFails()
    {
        var repository = new Repository("work", "root", "git", false);
        var service = new FakeStateService(repository);
        await using var session = await new RepositoryStateSessionFactory(service).CreateAsync(repository);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.RunMutationAsync(_ => throw new InvalidOperationException("failure")));

        Assert.Equal(2, service.ReadCount);
        Assert.Equal("commit-2", session.Current.HeadCommit);
    }

    private sealed class FakeStateService(Repository repository) : IRepositoryStateService
    {
        public int ReadCount { get; private set; }
        public string? ExternalHead { get; set; }

        public Task<RepositoryState> ReadAsync(Repository ignored, CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return Task.FromResult(new RepositoryState(
                repository, "main", ExternalHead ?? $"commit-{ReadCount}", false, RepositoryOperation.None,
                [], new Dictionary<string, string>(), new Dictionary<string, string>(), DateTimeOffset.UtcNow));
        }
    }
}
