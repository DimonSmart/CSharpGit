using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Desktop.Tests;

public sealed class CommitCreationViewModelTests
{
    [Fact]
    public void CommandsRequireRepositoryMutationAvailabilityAndMessage()
    {
        var fixture = new Fixture();
        fixture.ViewModel.CommitMessage = "message";

        Assert.True(fixture.ViewModel.CommitCommand.CanExecute(null));
        Assert.True(fixture.ViewModel.AmendCommand.CanExecute(null));
        Assert.True(fixture.ViewModel.EmptyCommitCommand.CanExecute(null));

        fixture.Context.CanRunRepositoryMutation = false;
        Assert.False(fixture.ViewModel.CommitCommand.CanExecute(null));

        fixture.Context.CanRunRepositoryMutation = true;
        fixture.Context.Repository = null;
        Assert.False(fixture.ViewModel.CommitCommand.CanExecute(null));

        fixture.Context.Repository = fixture.Repository;
        fixture.ViewModel.CommitMessage = string.Empty;
        Assert.False(fixture.ViewModel.CommitCommand.CanExecute(null));
    }

    [Fact]
    public async Task OrdinaryCommitUsesOneLifecycleAndClearsUnchangedMessage()
    {
        var fixture = new Fixture
        {
            HasStagedChanges = true
        };
        fixture.ViewModel.CommitMessage = "A";

        await ExecuteAsync(fixture.ViewModel.CommitCommand);

        var call = Assert.Single(fixture.Service.CommitCalls);
        Assert.Same(fixture.Repository, call.Repository);
        Assert.Equal("A", call.Message);
        Assert.False(call.Amend);
        Assert.False(call.IntentionalEmpty);
        Assert.Equal(1, fixture.Context.MutationCalls);
        Assert.Equal(1, fixture.Context.RefreshCalls);
        Assert.Equal(1, fixture.Context.ClearCommittedSelectionCalls);
        Assert.Equal(string.Empty, fixture.ViewModel.CommitMessage);
        Assert.Equal(
            ["mutation", "commit", "clear-committed-selection", "refresh"],
            fixture.Calls);
    }

    [Fact]
    public async Task CommitDoesNotEraseMessageChangedWhileRunning()
    {
        var fixture = new Fixture
        {
            HasStagedChanges = true
        };
        fixture.Service.BlockCommit = true;
        fixture.ViewModel.CommitMessage = "A";

        var execution = ExecuteAsync(fixture.ViewModel.CommitCommand);
        await fixture.Service.CommitStarted.Task;
        fixture.ViewModel.CommitMessage = "B";
        fixture.Service.ReleaseCommit();

        await execution;

        Assert.Equal("B", fixture.ViewModel.CommitMessage);
    }

    [Fact]
    public async Task FailedLifecyclePreservesMessage()
    {
        var fixture = new Fixture
        {
            HasStagedChanges = true
        };
        fixture.Context.LifecycleResult = false;
        fixture.ViewModel.CommitMessage = "A";

        await ExecuteAsync(fixture.ViewModel.CommitCommand);

        Assert.Single(fixture.Service.CommitCalls);
        Assert.Equal(1, fixture.Context.ClearCommittedSelectionCalls);
        Assert.Equal("A", fixture.ViewModel.CommitMessage);
    }

    [Fact]
    public async Task AmendAndExplicitEmptyUseExpectedFlags()
    {
        var fixture = new Fixture
        {
            HasStagedChanges = true
        };

        fixture.ViewModel.CommitMessage = "amend";
        await ExecuteAsync(fixture.ViewModel.AmendCommand);

        fixture.ViewModel.CommitMessage = "empty";
        await ExecuteAsync(fixture.ViewModel.EmptyCommitCommand);

        Assert.Equal(2, fixture.Service.CommitCalls.Count);
        Assert.True(fixture.Service.CommitCalls[0].Amend);
        Assert.False(fixture.Service.CommitCalls[0].IntentionalEmpty);
        Assert.False(fixture.Service.CommitCalls[1].Amend);
        Assert.True(fixture.Service.CommitCalls[1].IntentionalEmpty);
    }

    [Fact]
    public async Task NothingStagedWithUnstagedChangesOpensChoiceWithoutGit()
    {
        var fixture = new Fixture
        {
            HasUnstagedChanges = true
        };
        fixture.ViewModel.CommitMessage = "A";

        await ExecuteAsync(fixture.ViewModel.CommitCommand);

        Assert.True(fixture.ViewModel.IsEmptyIndexChoiceOpen);
        Assert.Empty(fixture.Service.CommitCalls);
        Assert.Equal(0, fixture.Context.MutationCalls);
    }

    [Fact]
    public async Task NoChangesPreservesDirectCommitBehavior()
    {
        var fixture = new Fixture();
        fixture.ViewModel.CommitMessage = "A";

        await ExecuteAsync(fixture.ViewModel.CommitCommand);

        Assert.False(fixture.ViewModel.IsEmptyIndexChoiceOpen);
        Assert.Single(fixture.Service.CommitCalls);
    }

    [Fact]
    public async Task StageAllAndCommitRunsInsideOneLifecycle()
    {
        var fixture = new Fixture
        {
            HasUnstagedChanges = true
        };
        fixture.ViewModel.CommitMessage = "A";

        await ExecuteAsync(fixture.ViewModel.CommitCommand);
        await ExecuteAsync(fixture.ViewModel.StageAllAndCommitCommand);

        Assert.False(fixture.ViewModel.IsEmptyIndexChoiceOpen);
        Assert.Equal(1, fixture.Service.StageAllCalls);
        Assert.Single(fixture.Service.CommitCalls);
        Assert.Equal(1, fixture.Context.MutationCalls);
        Assert.Equal(1, fixture.Context.RefreshCalls);
        Assert.Equal(1, fixture.Context.ClearPresentationSelectionCalls);
        Assert.Equal(0, fixture.Context.ClearCommittedSelectionCalls);
        Assert.Equal(string.Empty, fixture.ViewModel.CommitMessage);
        Assert.Equal(
            ["mutation", "clear-presentation-selection", "stage-all", "commit", "refresh"],
            fixture.Calls);
    }

    [Fact]
    public async Task StageFailureSkipsCommitAndPreservesMessage()
    {
        var fixture = new Fixture
        {
            HasUnstagedChanges = true
        };
        fixture.Service.StageAllFailure = new InvalidOperationException("stage failed");
        fixture.ViewModel.CommitMessage = "A";

        await ExecuteAsync(fixture.ViewModel.CommitCommand);
        await ExecuteAsync(fixture.ViewModel.StageAllAndCommitCommand);

        Assert.Equal(1, fixture.Service.StageAllCalls);
        Assert.Empty(fixture.Service.CommitCalls);
        Assert.Equal(1, fixture.Context.RefreshCalls);
        Assert.Equal("A", fixture.ViewModel.CommitMessage);
    }

    [Fact]
    public async Task CommitFailureAfterStageRefreshesWithoutRollback()
    {
        var fixture = new Fixture
        {
            HasUnstagedChanges = true
        };
        fixture.Service.CommitFailure = new InvalidOperationException("commit failed");
        fixture.ViewModel.CommitMessage = "A";

        await ExecuteAsync(fixture.ViewModel.CommitCommand);
        await ExecuteAsync(fixture.ViewModel.StageAllAndCommitCommand);

        Assert.Equal(1, fixture.Service.StageAllCalls);
        Assert.Single(fixture.Service.CommitCalls);
        Assert.Equal(1, fixture.Context.RefreshCalls);
        Assert.Equal(0, fixture.Service.UnstageAllCalls);
        Assert.Equal(0, fixture.Service.UnstageFilesCalls);
        Assert.Equal("A", fixture.ViewModel.CommitMessage);
    }

    [Fact]
    public async Task StageAllAndCommitIsDisabledWhenBulkMutationIsUnavailable()
    {
        var fixture = new Fixture
        {
            HasUnstagedChanges = true
        };
        fixture.ViewModel.CommitMessage = "A";

        await ExecuteAsync(fixture.ViewModel.CommitCommand);
        fixture.Context.CanRunBulkMutation = false;

        Assert.False(fixture.ViewModel.StageAllAndCommitCommand.CanExecute(null));
        Assert.True(fixture.ViewModel.ConfirmEmptyCommitCommand.CanExecute(null));
    }

    [Fact]
    public async Task PendingChoiceDoesNotMutateNewRepositoryAfterSwitch()
    {
        var fixture = new Fixture
        {
            HasUnstagedChanges = true
        };
        fixture.ViewModel.CommitMessage = "A";

        await ExecuteAsync(fixture.ViewModel.CommitCommand);
        fixture.Context.Repository = new Repository("/next", "/next", "/next/.git", false);

        await ExecuteAsync(fixture.ViewModel.StageAllAndCommitCommand);

        Assert.False(fixture.ViewModel.IsEmptyIndexChoiceOpen);
        Assert.Equal(0, fixture.Context.MutationCalls);
        Assert.Equal(0, fixture.Service.StageAllCalls);
        Assert.Empty(fixture.Service.CommitCalls);
        Assert.Equal("A", fixture.ViewModel.CommitMessage);
    }

    private static Task ExecuteAsync(System.Windows.Input.ICommand command) =>
        ((AsyncCommand)command).ExecuteAsync();

    private sealed class Fixture
    {
        public Fixture()
        {
            Repository = new Repository("/repo", "/repo", "/repo/.git", false);
            Calls = [];
            Context = new FakeContext(Calls)
            {
                Repository = Repository
            };
            Service = new FakeWorkingTreeService(Calls);
            ViewModel = new CommitCreationViewModel(Service);
            ViewModel.Attach(Context);
        }

        public Repository Repository { get; }
        public List<string> Calls { get; }
        public FakeContext Context { get; }
        public FakeWorkingTreeService Service { get; }
        public CommitCreationViewModel ViewModel { get; }

        public bool HasStagedChanges
        {
            set => Context.HasStagedChanges = value;
        }

        public bool HasUnstagedChanges
        {
            set => Context.HasUnstagedChanges = value;
        }
    }

    private sealed class FakeContext(List<string> calls) : ICommitCreationRepositoryContext
    {
        public Repository? Repository { get; set; }
        public bool CanRunRepositoryMutation { get; set; } = true;
        public bool CanRunBulkMutation { get; set; } = true;
        public bool HasStagedChanges { get; set; }
        public bool HasUnstagedChanges { get; set; }
        public bool LifecycleResult { get; set; } = true;
        public int MutationCalls { get; private set; }
        public int RefreshCalls { get; private set; }
        public int ClearPresentationSelectionCalls { get; private set; }
        public int ClearCommittedSelectionCalls { get; private set; }

        public async Task<bool> RunCommitMutationAsync(
            Repository expectedRepository,
            Func<Task> mutation,
            string? errorContext = null)
        {
            if (!ReferenceEquals(Repository, expectedRepository)
                || !CanRunRepositoryMutation)
            {
                return false;
            }

            MutationCalls++;
            calls.Add("mutation");
            try
            {
                await mutation();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _ = exception;
                RefreshCalls++;
                calls.Add("refresh");
                return false;
            }

            RefreshCalls++;
            calls.Add("refresh");
            return LifecycleResult;
        }

        public void ClearWorkingTreePresentationSelection()
        {
            ClearPresentationSelectionCalls++;
            calls.Add("clear-presentation-selection");
        }

        public void ClearCommittedWorkingTreePresentationSelection()
        {
            ClearCommittedSelectionCalls++;
            calls.Add("clear-committed-selection");
        }
    }

    private sealed class FakeWorkingTreeService(List<string> calls) : IWorkingTreeService
    {
        private readonly TaskCompletionSource<bool> _commitRelease =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int StageAllCalls { get; private set; }
        public int UnstageAllCalls { get; private set; }
        public int UnstageFilesCalls { get; private set; }
        public List<CommitCall> CommitCalls { get; } = [];
        public Exception? StageAllFailure { get; set; }
        public Exception? CommitFailure { get; set; }
        public bool BlockCommit { get; set; }
        public TaskCompletionSource<bool> CommitStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ReleaseCommit() => _commitRelease.TrySetResult(true);

        public Task StageFileAsync(
            Repository repository,
            WorkingTreeChange change,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task StageFilesAsync(
            Repository repository,
            IReadOnlyCollection<WorkingTreeChange> changes,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task StageAllAsync(
            Repository repository,
            CancellationToken cancellationToken = default)
        {
            StageAllCalls++;
            calls.Add("stage-all");
            return StageAllFailure is null
                ? Task.CompletedTask
                : Task.FromException(StageAllFailure);
        }

        public Task UnstageFileAsync(
            Repository repository,
            WorkingTreeChange change,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task UnstageFilesAsync(
            Repository repository,
            IReadOnlyCollection<WorkingTreeChange> changes,
            CancellationToken cancellationToken = default)
        {
            UnstageFilesCalls++;
            return Task.CompletedTask;
        }

        public Task UnstageAllAsync(
            Repository repository,
            CancellationToken cancellationToken = default)
        {
            UnstageAllCalls++;
            return Task.CompletedTask;
        }

        public Task DiscardTrackedFilesAsync(
            Repository repository,
            IReadOnlyCollection<WorkingTreeChange> changes,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DiscardFileAsync(
            Repository repository,
            WorkingTreeChange change,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DiscardAllFileChangesAsync(
            Repository repository,
            WorkingTreeChange change,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public async Task CommitAsync(
            Repository repository,
            string message,
            bool amend = false,
            bool intentionalEmpty = false,
            CancellationToken cancellationToken = default)
        {
            CommitCalls.Add(new CommitCall(repository, message, amend, intentionalEmpty));
            calls.Add("commit");
            CommitStarted.TrySetResult(true);

            if (BlockCommit)
                await _commitRelease.Task.WaitAsync(cancellationToken);

            if (CommitFailure is not null)
                throw CommitFailure;
        }
    }

    private sealed record CommitCall(
        Repository Repository,
        string Message,
        bool Amend,
        bool IntentionalEmpty);
}
