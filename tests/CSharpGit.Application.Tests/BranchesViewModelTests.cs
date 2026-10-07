using System.ComponentModel;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Application.Tests;

public sealed class BranchesViewModelTests
{
    [Fact]
    public void RepositoryStatePreservesSelectionByBranchNameAcrossModelReplacement()
    {
        var repository = Repository("repo");
        var context = new FakeBranchesRepositoryContext { Repository = repository };
        var viewModel = CreateViewModel(new FakeReferenceService(), new FakeSyncService(), context);

        viewModel.ApplyRepositoryState(
            [
                new GitBranch("main", "a", true, "origin/main"),
                new GitBranch("feature/a", "b", false, "origin/feature/a")
            ],
            [new GitBranch("origin/main", "a", false, null)]);

        viewModel.SelectedLocalBranch = viewModel.LocalBranches.Single(branch => branch.Name == "feature/a");
        var replacement = new GitBranch("feature/a", "c", false, "origin/feature/a");

        viewModel.ApplyRepositoryState(
            [
                new GitBranch("main", "d", true, "origin/main"),
                replacement
            ],
            [new GitBranch("origin/main", "d", false, null)]);

        Assert.Same(replacement, viewModel.SelectedLocalBranch);
        Assert.Equal("feature/a", viewModel.SelectedLocalBranch?.Name);
        Assert.Equal("origin/feature/a", viewModel.SelectedLocalBranch?.Upstream);
    }

    [Fact]
    public async Task CreateAndSwitchUseFeatureMutationLifecycle()
    {
        var repository = Repository("repo");
        var references = new FakeReferenceService();
        var context = new FakeBranchesRepositoryContext { Repository = repository };
        var viewModel = CreateViewModel(references, new FakeSyncService(), context);
        var feature = new GitBranch("feature/a", "a", false, null);

        Assert.True(await viewModel.CreateBranchAsync(repository, " feature/new ", "main", switchToBranch: true));
        Assert.True(await viewModel.SwitchBranchAsync(repository, feature));

        Assert.Equal(
            ["create:feature/new:main:True", "switch:feature/a"],
            references.Calls);
        Assert.Equal(2, context.MutationLifecycleCalls);
        Assert.Equal("feature/a", viewModel.SelectedLocalBranch?.Name);
    }

    [Fact]
    public async Task LocalThenRemoteDeletionReportsPartialSuccessAndRefreshesOnce()
    {
        var repository = Repository("repo");
        var references = new FakeReferenceService();
        var sync = new FakeSyncService
        {
            DeleteRemoteException = new InvalidOperationException("remote failed")
        };
        var context = new FakeBranchesRepositoryContext
        {
            Repository = repository,
            Remotes = [Remote("origin")]
        };
        var viewModel = CreateViewModel(references, sync, context);
        var local = new GitBranch("feature/a", "a", false, "origin/feature/a");
        viewModel.ApplyRepositoryState(
            [local],
            [new GitBranch("origin/feature/a", "a", false, null)]);

        var result = await viewModel.DeleteLocalBranchAsync(
            repository,
            local,
            BranchDeletionMode.Safe,
            deleteRemote: true);

        Assert.True(result.LifecycleSucceeded);
        Assert.True(result.LocalDeleted);
        Assert.False(result.RemoteDeleted);
        Assert.Equal("remote failed", result.SecondaryFailureMessage);
        Assert.Equal(["delete:feature/a:Safe"], references.Calls);
        Assert.Equal(["delete-remote:origin:feature/a"], sync.Calls);
        Assert.Equal(1, context.MutationLifecycleCalls);
    }

    [Fact]
    public async Task RemoteThenLocalDeletionReportsPartialSuccessAndRefreshesOnce()
    {
        var repository = Repository("repo");
        var references = new FakeReferenceService
        {
            DeleteException = new InvalidOperationException("local failed")
        };
        var sync = new FakeSyncService();
        var context = new FakeBranchesRepositoryContext
        {
            Repository = repository,
            Remotes = [Remote("origin")]
        };
        var viewModel = CreateViewModel(references, sync, context);
        var local = new GitBranch("feature/a", "a", false, "origin/feature/a");
        var remote = new GitBranch("origin/feature/a", "a", false, null);
        viewModel.ApplyRepositoryState([local], [remote]);
        var target = viewModel.ResolveRemoteDeletionTarget(remote);

        var result = await viewModel.DeleteRemoteBranchAsync(
            repository,
            remote,
            target,
            deleteLocal: true,
            BranchDeletionMode.Force);

        Assert.True(result.LifecycleSucceeded);
        Assert.True(result.RemoteDeleted);
        Assert.False(result.LocalDeleted);
        Assert.Equal("local failed", result.SecondaryFailureMessage);
        Assert.Equal(["delete-remote:origin:feature/a"], sync.Calls);
        Assert.Equal(["delete:feature/a:Force"], references.Calls);
        Assert.Equal(1, context.MutationLifecycleCalls);
    }

    [Fact]
    public async Task StaleRepositoryDoesNotStartBranchMutation()
    {
        var first = Repository("first");
        var second = Repository("second");
        var references = new FakeReferenceService();
        var context = new FakeBranchesRepositoryContext { Repository = second };
        var viewModel = CreateViewModel(references, new FakeSyncService(), context);

        var succeeded = await viewModel.CreateBranchAsync(first, "feature/a");

        Assert.False(succeeded);
        Assert.Empty(references.Calls);
        Assert.Equal(0, context.MutationLifecycleCalls);
    }

    private static BranchesViewModel CreateViewModel(
        FakeReferenceService references,
        FakeSyncService sync,
        FakeBranchesRepositoryContext context)
    {
        var viewModel = new BranchesViewModel(references, sync);
        viewModel.Attach(context);
        return viewModel;
    }

    private static Repository Repository(string name) =>
        new($"/work/{name}", $"/work/{name}", $"/work/{name}/.git", false);

    private static GitRemote Remote(string name) =>
        new(name, $"https://example.test/{name}", $"https://example.test/{name}");

    private sealed class FakeBranchesRepositoryContext : IBranchesRepositoryContext
    {
        private Repository? _repository;
        private bool _isBusy;
        private RepositoryOperation _currentOperation;

        public event PropertyChangedEventHandler? PropertyChanged;

        public Repository? Repository
        {
            get => _repository;
            set
            {
                if (ReferenceEquals(_repository, value)) return;
                _repository = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Repository)));
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (_isBusy == value) return;
                _isBusy = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBusy)));
            }
        }

        public RepositoryOperation CurrentOperation
        {
            get => _currentOperation;
            set
            {
                if (_currentOperation == value) return;
                _currentOperation = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentOperation)));
            }
        }

        public IReadOnlyList<GitRemote> Remotes { get; set; } = [];
        public int MutationLifecycleCalls { get; private set; }

        public async Task<bool> RunBranchMutationAsync(
            Repository expectedRepository,
            Func<Task> mutation,
            string errorContext,
            bool includeHistory,
            Action? afterSuccessfulMutation = null)
        {
            if (!ReferenceEquals(Repository, expectedRepository))
                return false;

            MutationLifecycleCalls++;
            try
            {
                await mutation();
                afterSuccessfulMutation?.Invoke();
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return false;
            }
        }
    }

    private sealed class FakeReferenceService : IReferenceService
    {
        public List<string> Calls { get; } = [];
        public Exception? DeleteException { get; set; }

        public Task SwitchBranchAsync(
            Repository repository,
            string branch,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"switch:{branch}");
            return Task.CompletedTask;
        }

        public Task CheckoutAsync(
            Repository repository,
            string reference,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task CreateBranchAsync(
            Repository repository,
            string branch,
            string? startPoint = null,
            bool switchToBranch = true,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"create:{branch}:{startPoint}:{switchToBranch}");
            return Task.CompletedTask;
        }

        public Task RenameBranchAsync(
            Repository repository,
            string oldName,
            string newName,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"rename:{oldName}:{newName}");
            return Task.CompletedTask;
        }

        public Task DeleteBranchAsync(
            Repository repository,
            string branch,
            CancellationToken cancellationToken = default) =>
            DeleteBranchAsync(repository, branch, BranchDeletionMode.Safe, cancellationToken);

        public Task DeleteBranchAsync(
            Repository repository,
            string branch,
            BranchDeletionMode mode,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"delete:{branch}:{mode}");
            return DeleteException is null
                ? Task.CompletedTask
                : Task.FromException(DeleteException);
        }

        public Task CheckoutRemoteBranchAsync(
            Repository repository,
            string remoteBranch,
            string localBranch,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"checkout-remote:{remoteBranch}:{localBranch}");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSyncService : IRepositorySyncService
    {
        public List<string> Calls { get; } = [];
        public Exception? DeleteRemoteException { get; set; }
        public Exception? PublishException { get; set; }
        public PublishBranchPreparation Preparation { get; set; } =
            new("main", "origin");

        public Task FetchAsync(
            Repository repository,
            string remote,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task FetchAllAsync(
            Repository repository,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteRemoteBranchAsync(
            Repository repository,
            string remote,
            string branch,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"delete-remote:{remote}:{branch}");
            return DeleteRemoteException is null
                ? Task.CompletedTask
                : Task.FromException(DeleteRemoteException);
        }

        public Task PullAsync(
            Repository repository,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task PushAsync(
            Repository repository,
            PushOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<PublishBranchPreparation> PreparePublishBranchAsync(
            Repository repository,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Preparation);

        public Task PublishBranchAsync(
            Repository repository,
            PublishBranchRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"publish:{request.Remote}:{request.RemoteBranch}:{request.SetUpstream}");
            return PublishException is null
                ? Task.CompletedTask
                : Task.FromException(PublishException);
        }

        public Task<ForcePushWithLeaseSnapshot> PrepareForcePushWithLeaseAsync(
            Repository repository,
            string? remote = null,
            string? remoteBranch = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ForcePushWithLeaseSnapshot>(new NotSupportedException());

        public Task ForcePushWithLeaseAsync(
            Repository repository,
            ForcePushWithLeaseSnapshot snapshot,
            CancellationToken cancellationToken = default) =>
            Task.FromException(new NotSupportedException());
    }
}
