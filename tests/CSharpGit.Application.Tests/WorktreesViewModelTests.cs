using System.ComponentModel;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Application.Tests;

public sealed class WorktreesViewModelTests
{
    [Fact]
    public async Task LoadPublishesWorktreesForCurrentRepository()
    {
        var repository = Repository("repo");
        var worktrees = new[]
        {
            Worktree("/work/repo", "main", primary: true, current: true),
            Worktree("/work/repo-feature", "feature/a")
        };
        var service = new FakeWorktreeService { ListedWorktrees = worktrees };
        var context = new FakeWorktreesRepositoryContext { Repository = repository };
        var viewModel = CreateViewModel(service, context);

        await viewModel.RefreshAsync();

        Assert.Equal(worktrees, viewModel.Worktrees);
        Assert.Equal(repository, service.LastListRepository);
        Assert.Null(viewModel.RefreshErrorMessage);
    }

    [Fact]
    public async Task RepositorySwitchRejectsStaleListResult()
    {
        var first = Repository("first");
        var second = Repository("second");
        var firstResult = new TaskCompletionSource<IReadOnlyList<WorktreeInfo>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeWorktreeService
        {
            ListHandler = (repository, _) =>
                ReferenceEquals(repository, first)
                    ? firstResult.Task
                    : Task.FromResult<IReadOnlyList<WorktreeInfo>>(
                        [Worktree("/work/second", "second", primary: true, current: true)])
        };
        var context = new FakeWorktreesRepositoryContext { Repository = first };
        var viewModel = CreateViewModel(service, context);

        context.Repository = second;
        await viewModel.RefreshAsync();

        firstResult.SetResult([Worktree("/work/first", "first", primary: true, current: true)]);
        await Task.Yield();

        Assert.Single(viewModel.Worktrees);
        Assert.Equal("/work/second", viewModel.Worktrees[0].Path);
    }

    [Fact]
    public async Task CreateFromExistingBranchUsesServiceAndRepositoryRefresh()
    {
        var repository = Repository("repo");
        var service = new FakeWorktreeService();
        var context = new FakeWorktreesRepositoryContext { Repository = repository };
        var viewModel = CreateViewModel(service, context);
        await viewModel.RefreshAsync();

        var result = await viewModel.CreateFromBranchAsync(
            repository,
            "../repo-worktrees/feature-a",
            new GitBranch("feature/a", "abc", false, null));

        Assert.True(result.Succeeded);
        Assert.Equal("feature/a", service.LastAddedBranch);
        Assert.Equal(repository, service.LastMutationRepository);
        Assert.Equal(1, context.MutationLifecycleCalls);
        Assert.NotNull(result.WorktreePath);
        Assert.True(Path.IsPathRooted(result.WorktreePath));
    }

    [Fact]
    public async Task CreateRejectsBranchAlreadyCheckedOutWithoutCallingService()
    {
        var repository = Repository("repo");
        var service = new FakeWorktreeService
        {
            ListedWorktrees = [Worktree("/work/repo-feature", "feature/a")]
        };
        var context = new FakeWorktreesRepositoryContext { Repository = repository };
        var viewModel = CreateViewModel(service, context);
        await viewModel.RefreshAsync();

        var result = await viewModel.CreateFromBranchAsync(
            repository,
            "/work/another",
            new GitBranch("feature/a", "abc", false, null));

        Assert.True(result.Failed);
        Assert.Equal("Branch already has a worktree", result.ErrorTitle);
        Assert.Contains("/work/repo-feature", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Null(service.LastAddedBranch);
        Assert.Equal(0, context.MutationLifecycleCalls);
    }

    [Fact]
    public async Task CreateNewBranchValidatesInputsAndPassesRequest()
    {
        var repository = Repository("repo");
        var service = new FakeWorktreeService();
        var context = new FakeWorktreesRepositoryContext { Repository = repository };
        var viewModel = CreateViewModel(service, context);
        await viewModel.RefreshAsync();

        var invalid = await viewModel.CreateNewBranchAsync(repository, "../new", " ", "HEAD");

        Assert.True(invalid.Failed);
        Assert.Contains("required", invalid.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Null(service.LastNewBranch);

        var result = await viewModel.CreateNewBranchAsync(
            repository,
            "../new",
            " feature/new ",
            " main ");

        Assert.True(result.Succeeded);
        Assert.Equal("feature/new", service.LastNewBranch);
        Assert.Equal("main", service.LastStartPoint);
    }

    [Fact]
    public async Task MutationFailureIsInterpretedWithinRepositoryMutationLifecycle()
    {
        var repository = Repository("repo");
        var service = new FakeWorktreeService
        {
            AddException = new InvalidOperationException("destination already exists")
        };
        var context = new FakeWorktreesRepositoryContext { Repository = repository };
        var viewModel = CreateViewModel(service, context);
        await viewModel.RefreshAsync();

        var result = await viewModel.CreateFromBranchAsync(
            repository,
            "../existing",
            new GitBranch("feature/a", "abc", false, null));

        Assert.True(result.Failed);
        Assert.Contains("destination already exists", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Choose another directory", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(1, context.MutationLifecycleCalls);
    }

    [Fact]
    public async Task RemoveProtectsPrimaryCurrentAndLockedWorktrees()
    {
        var repository = Repository("repo");
        var service = new FakeWorktreeService();
        var context = new FakeWorktreesRepositoryContext { Repository = repository };
        var viewModel = CreateViewModel(service, context);
        await viewModel.RefreshAsync();

        var primary = await viewModel.RemoveAsync(
            repository,
            Worktree("/work/repo", "main", primary: true));
        var current = await viewModel.RemoveAsync(
            repository,
            Worktree("/work/current", "other", current: true));
        var locked = await viewModel.RemoveAsync(
            repository,
            Worktree("/work/locked", "locked", locked: true));

        Assert.True(primary.Failed);
        Assert.True(current.Failed);
        Assert.True(locked.Failed);
        Assert.Empty(service.RemoveCalls);
    }

    [Fact]
    public async Task ForceRemovePassesForceAndRefreshes()
    {
        var repository = Repository("repo");
        var service = new FakeWorktreeService();
        var context = new FakeWorktreesRepositoryContext { Repository = repository };
        var viewModel = CreateViewModel(service, context);
        await viewModel.RefreshAsync();
        var worktree = Worktree("/work/feature", "feature/a");

        var result = await viewModel.RemoveAsync(repository, worktree, force: true);

        Assert.True(result.Succeeded);
        Assert.Equal([true], service.RemoveCalls);
        Assert.Equal(1, context.MutationLifecycleCalls);
    }

    [Fact]
    public async Task LockUnlockAndPruneUseFeatureMutationLifecycle()
    {
        var repository = Repository("repo");
        var service = new FakeWorktreeService();
        var context = new FakeWorktreesRepositoryContext { Repository = repository };
        var viewModel = CreateViewModel(service, context);
        await viewModel.RefreshAsync();

        var unlocked = Worktree("/work/feature", "feature/a");
        var locked = Worktree("/work/feature", "feature/a", locked: true);

        Assert.True((await viewModel.LockAsync(repository, unlocked, " reason ")).Succeeded);
        Assert.Equal("reason", service.LastLockReason);
        Assert.True((await viewModel.UnlockAsync(repository, locked)).Succeeded);
        Assert.True((await viewModel.PruneAsync(repository)).Succeeded);
        Assert.Equal(3, context.MutationLifecycleCalls);
    }

    [Fact]
    public async Task RepositorySwitchCancelsInFlightMutationAndDoesNotRefreshNewRepository()
    {
        var first = Repository("first");
        var second = Repository("second");
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeWorktreeService
        {
            AddHandler = async (_, _, _, cancellationToken) =>
            {
                entered.SetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
        };
        var context = new FakeWorktreesRepositoryContext { Repository = first };
        var viewModel = CreateViewModel(service, context);
        await viewModel.RefreshAsync();

        var operation = viewModel.CreateFromBranchAsync(
            first,
            "../first-feature",
            new GitBranch("feature/a", "abc", false, null));
        await entered.Task;

        context.Repository = second;
        var result = await operation;

        Assert.True(result.Canceled);
        Assert.Equal(1, context.MutationLifecycleCalls);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task AvailabilityFollowsRepositoryBusyOperationAndFeatureBusyState()
    {
        var repository = Repository("repo");
        var service = new FakeWorktreeService();
        var context = new FakeWorktreesRepositoryContext { Repository = repository };
        var viewModel = CreateViewModel(service, context);
        await viewModel.RefreshAsync();
        var removable = Worktree("/work/feature", "feature/a");

        Assert.True(viewModel.CanMutate);
        Assert.True(viewModel.CanRemove(removable));

        context.IsBusy = true;
        Assert.False(viewModel.CanMutate);
        context.IsBusy = false;

        context.CurrentOperation = RepositoryOperation.Rebase;
        Assert.False(viewModel.CanMutate);
        context.CurrentOperation = RepositoryOperation.None;

        context.Repository = null;
        Assert.False(viewModel.CanMutate);
    }

    private static WorktreesViewModel CreateViewModel(
        FakeWorktreeService service,
        FakeWorktreesRepositoryContext context)
    {
        var viewModel = new WorktreesViewModel(service);
        viewModel.Attach(context);
        return viewModel;
    }

    private static Repository Repository(string name)
    {
        var workingDirectory = Path.Combine(Path.GetTempPath(), "csharpgit-tests", name);
        return new(
            workingDirectory,
            workingDirectory,
            Path.Combine(workingDirectory, ".git"),
            false);
    }

    private static WorktreeInfo Worktree(
        string path,
        string branch,
        bool primary = false,
        bool current = false,
        bool locked = false) =>
        new(path, "1234567890", branch, current, false, locked, locked ? "reason" : null, false)
        {
            IsPrimary = primary
        };

    private sealed class FakeWorktreesRepositoryContext : IWorktreesRepositoryContext
    {
        private Repository? _repository;
        private bool _isBusy;
        private RepositoryOperation _currentOperation;
        private string _headDisplay = string.Empty;

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

        public string HeadDisplay
        {
            get => _headDisplay;
            set
            {
                if (string.Equals(_headDisplay, value, StringComparison.Ordinal)) return;
                _headDisplay = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HeadDisplay)));
            }
        }

        public string? ErrorMessage { get; private set; }
        public int MutationLifecycleCalls { get; private set; }

        public async Task<bool> RunWorktreeMutationAsync(
            Repository expectedRepository,
            Func<Task> mutation)
        {
            if (!ReferenceEquals(Repository, expectedRepository) || IsBusy)
                return false;

            MutationLifecycleCalls++;
            ErrorMessage = null;
            IsBusy = true;
            try
            {
                await mutation();
                return ReferenceEquals(Repository, expectedRepository);
            }
            finally
            {
                IsBusy = false;
            }
        }
    }

    private sealed class FakeWorktreeService : IWorktreeService
    {
        public IReadOnlyList<WorktreeInfo> ListedWorktrees { get; set; } = [];
        public Func<Repository, CancellationToken, Task<IReadOnlyList<WorktreeInfo>>>? ListHandler { get; set; }
        public Func<Repository, string, string, CancellationToken, Task>? AddHandler { get; set; }
        public Exception? AddException { get; set; }
        public Repository? LastListRepository { get; private set; }
        public Repository? LastMutationRepository { get; private set; }
        public string? LastAddedBranch { get; private set; }
        public string? LastNewBranch { get; private set; }
        public string? LastStartPoint { get; private set; }
        public string? LastLockReason { get; private set; }
        public List<bool> RemoveCalls { get; } = [];

        public Task<IReadOnlyList<WorktreeInfo>> ListAsync(
            Repository repository,
            CancellationToken cancellationToken = default)
        {
            LastListRepository = repository;
            return ListHandler?.Invoke(repository, cancellationToken)
                   ?? Task.FromResult(ListedWorktrees);
        }

        public Task AddAsync(
            Repository repository,
            string path,
            string branch,
            CancellationToken cancellationToken = default)
        {
            LastMutationRepository = repository;
            LastAddedBranch = branch;
            if (AddException is not null)
                return Task.FromException(AddException);
            return AddHandler?.Invoke(repository, path, branch, cancellationToken)
                   ?? Task.CompletedTask;
        }

        public Task AddNewBranchAsync(
            Repository repository,
            string path,
            string newBranch,
            string startPoint,
            CancellationToken cancellationToken = default)
        {
            LastMutationRepository = repository;
            LastNewBranch = newBranch;
            LastStartPoint = startPoint;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(
            Repository repository,
            WorktreeInfo worktree,
            bool force = false,
            CancellationToken cancellationToken = default)
        {
            LastMutationRepository = repository;
            RemoveCalls.Add(force);
            return Task.CompletedTask;
        }

        public Task LockAsync(
            Repository repository,
            WorktreeInfo worktree,
            string? reason = null,
            CancellationToken cancellationToken = default)
        {
            LastMutationRepository = repository;
            LastLockReason = reason;
            return Task.CompletedTask;
        }

        public Task UnlockAsync(
            Repository repository,
            WorktreeInfo worktree,
            CancellationToken cancellationToken = default)
        {
            LastMutationRepository = repository;
            return Task.CompletedTask;
        }

        public Task PruneAsync(
            Repository repository,
            CancellationToken cancellationToken = default)
        {
            LastMutationRepository = repository;
            return Task.CompletedTask;
        }
    }
}
