using System.ComponentModel;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Application.Tests;

public sealed class InteractiveRebaseViewModelTests
{
    [Fact]
    public async Task PreparePublishesTodoOntoAndUsesPresentationBusyLifecycle()
    {
        var repository = Repository("repo");
        var context = new FakeContext { Repository = repository };
        var viewModel = CreateViewModel(
            context,
            out var rebase,
            out _,
            out _,
            out _);
        rebase.ReadTodoResult = Todo("base", "pick a first");

        var succeeded = await viewModel.PrepareInteractiveRebaseFromCommitAsync("base");

        Assert.True(succeeded);
        Assert.Equal(["base"], rebase.PreparedFromCommits);
        Assert.Equal("base", viewModel.RebaseOnto);
        Assert.Equal("pick a first", viewModel.RebaseTodoText);
        Assert.Equal(1, context.EnterBusyCalls);
        Assert.Equal(1, context.ExitBusyCalls);
    }

    [Fact]
    public async Task RepositorySwitchDuringPreparationDoesNotPublishStaleTodo()
    {
        var first = Repository("first");
        var second = Repository("second");
        var context = new FakeContext { Repository = first };
        var viewModel = CreateViewModel(
            context,
            out var rebase,
            out _,
            out _,
            out _);
        var completion = new TaskCompletionSource<InteractiveRebaseTodo>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        rebase.ReadTodo = (_, _, _) => completion.Task;

        var preparation = viewModel.PrepareInteractiveRebaseFromCommitAsync("base");
        await rebase.ReadStarted.Task;
        context.Repository = second;
        completion.SetResult(Todo("base", "pick stale stale"));

        Assert.False(await preparation);
        Assert.Equal(string.Empty, viewModel.RebaseOnto);
        Assert.Equal(string.Empty, viewModel.RebaseTodoText);
    }

    [Fact]
    public async Task RepeatedPreparationCancelsPreviousRequestAndPublishesNewestTodo()
    {
        var repository = Repository("repo");
        var context = new FakeContext { Repository = repository };
        var viewModel = CreateViewModel(
            context,
            out var rebase,
            out _,
            out _,
            out _);
        var firstCompletion = new TaskCompletionSource<InteractiveRebaseTodo>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var call = 0;
        rebase.ReadTodo = (_, commit, cancellationToken) =>
        {
            if (Interlocked.Increment(ref call) == 1)
                return firstCompletion.Task.WaitAsync(cancellationToken);

            return Task.FromResult(Todo(commit, "pick b newest"));
        };

        var first = viewModel.PrepareInteractiveRebaseFromCommitAsync("old-base");
        await rebase.ReadStarted.Task;
        var second = viewModel.PrepareInteractiveRebaseFromCommitAsync("new-base");

        Assert.False(await first);
        Assert.True(await second);
        Assert.Equal("new-base", viewModel.RebaseOnto);
        Assert.Equal("pick b newest", viewModel.RebaseTodoText);
        Assert.Equal(2, context.EnterBusyCalls);
        Assert.Equal(2, context.ExitBusyCalls);
    }

    [Fact]
    public async Task StartUsesEditedTodoMutationLifecyclePublishesMessageAndInvalidatesPlan()
    {
        var repository = Repository("repo");
        var context = new FakeContext { Repository = repository };
        var viewModel = CreateViewModel(
            context,
            out var rebase,
            out _,
            out _,
            out _);
        rebase.ReadTodoResult = Todo("base", "pick a first");
        rebase.StartResult = new RebaseResult(RebaseResultKind.Conflicts, "resolve conflicts");
        Assert.True(await viewModel.PrepareInteractiveRebaseFromCommitAsync("base"));

        await viewModel.StartPreparedInteractiveRebaseAsync("reword a changed");

        Assert.Equal(1, context.MutationLifecycleCalls);
        Assert.Equal("reword a changed", rebase.StartedTodo?.TodoText);
        Assert.Equal("resolve conflicts", context.PublishedMessage);
        Assert.Equal(string.Empty, viewModel.RebaseOnto);
        Assert.Equal(string.Empty, viewModel.RebaseTodoText);

        await viewModel.StartPreparedInteractiveRebaseAsync("pick a ignored");
        Assert.Equal(1, context.MutationLifecycleCalls);
    }

    [Fact]
    public async Task ReadIdentityRejectsResultFromPreviousRepository()
    {
        var first = Repository("first");
        var second = Repository("second");
        var context = new FakeContext { Repository = first };
        var viewModel = CreateViewModel(
            context,
            out _,
            out var identity,
            out _,
            out _);
        var completion = new TaskCompletionSource<RepositoryIdentitySnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        identity.Read = (_, _) => completion.Task;

        var read = viewModel.ReadInteractiveRebaseAuthorIdentityAsync();
        await identity.ReadStarted.Task;
        context.Repository = second;
        completion.SetResult(Identity("Dmitry", "d@example.test"));

        Assert.Null(await read);
    }

    [Fact]
    public async Task ResetIdentityWithoutDateResetReadsOriginalAuthorDates()
    {
        var repository = Repository("repo");
        var context = new FakeContext { Repository = repository };
        var viewModel = CreateViewModel(
            context,
            out _,
            out _,
            out var authorChanges,
            out var authorDates);
        authorChanges.TargetCommits = ["a", "b"];
        authorChanges.ApplyWithDatesResult = new InteractiveRebaseAuthorChangeResult(
            "changed",
            EligibleCount: 2,
            ChangedCount: 2,
            UnsupportedCount: 0);
        authorDates.Result = new Dictionary<string, string>
        {
            ["a"] = "100 +0000",
            ["b"] = "200 +0000"
        };
        var request = new InteractiveRebaseAuthorChangeRequest(
            "pick a A\npick b B",
            0,
            0,
            InteractiveRebaseAuthorChangeScope.AllEligibleCommits,
            string.Empty,
            string.Empty,
            ResetAuthorDate: false,
            ResetToCurrentGitIdentity: true);

        var result = await viewModel.ApplyInteractiveRebaseAuthorChangeAsync(request);

        Assert.Equal("changed", result.TodoText);
        Assert.Equal(["a", "b"], authorDates.LastCommits);
        Assert.Same(authorDates.Result, authorChanges.LastAuthorDates);
        Assert.Equal(1, authorChanges.ApplyWithDatesCalls);
        Assert.Equal(0, authorChanges.ApplyCalls);
    }

    [Fact]
    public async Task RepositorySwitchDuringAuthorDateReadFailsWithoutApplyingChange()
    {
        var first = Repository("first");
        var second = Repository("second");
        var context = new FakeContext { Repository = first };
        var viewModel = CreateViewModel(
            context,
            out _,
            out _,
            out var authorChanges,
            out var authorDates);
        authorChanges.TargetCommits = ["a"];
        var completion = new TaskCompletionSource<IReadOnlyDictionary<string, string>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        authorDates.Read = (_, _, _) => completion.Task;
        var request = new InteractiveRebaseAuthorChangeRequest(
            "pick a A",
            0,
            0,
            InteractiveRebaseAuthorChangeScope.AllEligibleCommits,
            string.Empty,
            string.Empty,
            ResetAuthorDate: false,
            ResetToCurrentGitIdentity: true);

        var apply = viewModel.ApplyInteractiveRebaseAuthorChangeAsync(request);
        await authorDates.ReadStarted.Task;
        context.Repository = second;
        completion.SetResult(new Dictionary<string, string> { ["a"] = "100 +0000" });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => apply);
        Assert.Contains("repository changed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, authorChanges.ApplyWithDatesCalls);
    }

    private static InteractiveRebaseViewModel CreateViewModel(
        FakeContext context,
        out FakeInteractiveRebaseService rebase,
        out FakeRepositoryIdentityService identity,
        out FakeAuthorChangeService authorChanges,
        out FakeCommitAuthorDateReader authorDates)
    {
        rebase = new FakeInteractiveRebaseService();
        identity = new FakeRepositoryIdentityService();
        authorChanges = new FakeAuthorChangeService();
        authorDates = new FakeCommitAuthorDateReader();
        var viewModel = new InteractiveRebaseViewModel(
            rebase,
            identity,
            authorChanges,
            authorDates);
        viewModel.Attach(context);
        return viewModel;
    }

    private static Repository Repository(string name) =>
        new($"/work/{name}", $"/work/{name}", $"/work/{name}/.git", false);

    private static InteractiveRebaseTodo Todo(string onto, string text) =>
        new(
            onto,
            text,
            new InteractiveRebaseSourceSnapshot(
                "head",
                "refs/heads/main"));

    private static RepositoryIdentitySnapshot Identity(string name, string email) =>
        new(
            new GitIdentityValue(
                name,
                GitConfigSource.Repository,
                ".git/config",
                name,
                true),
            new GitIdentityValue(
                email,
                GitConfigSource.Repository,
                ".git/config",
                email,
                true));

    private sealed class FakeContext : IInteractiveRebaseRepositoryContext
    {
        private Repository? _repository;

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

        public bool IsBusy { get; private set; }
        public RepositoryOperation CurrentOperation { get; set; }
        public int EnterBusyCalls { get; private set; }
        public int ExitBusyCalls { get; private set; }
        public int MutationLifecycleCalls { get; private set; }
        public string? PublishedMessage { get; private set; }
        public string? ErrorMessage { get; private set; }

        public void EnterInteractiveRebaseBusy()
        {
            EnterBusyCalls++;
            IsBusy = true;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBusy)));
        }

        public void ExitInteractiveRebaseBusy()
        {
            ExitBusyCalls++;
            IsBusy = false;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBusy)));
        }

        public async Task<bool> RunInteractiveRebaseMutationAsync(
            Repository expectedRepository,
            Func<Task> mutation,
            string? errorContext = null)
        {
            if (!ReferenceEquals(Repository, expectedRepository))
                return false;

            MutationLifecycleCalls++;
            await mutation();
            return true;
        }

        public void PublishOperationMessage(string message) =>
            PublishedMessage = message;

        public void ReportInteractiveRebaseError(string message) =>
            ErrorMessage = message;
    }

    private sealed class FakeInteractiveRebaseService : IInteractiveRebaseService
    {
        public List<string> PreparedFromCommits { get; } = [];
        public InteractiveRebaseTodo ReadTodoResult { get; set; } =
            Todo("base", "pick a default");
        public RebaseResult StartResult { get; set; } =
            new(RebaseResultKind.Completed, "done");
        public InteractiveRebaseTodo? StartedTodo { get; private set; }
        public TaskCompletionSource<bool> ReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Func<Repository, string, CancellationToken, Task<InteractiveRebaseTodo>>? ReadTodo { get; set; }

        public Task<InteractiveRebasePlan> ReadInteractiveRebasePlanAsync(
            Repository repository,
            string onto,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<InteractiveRebasePlan> ReadInteractiveRebasePlanFromCommitAsync(
            Repository repository,
            string firstCommit,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<InteractiveRebaseTodo> ReadInteractiveRebaseTodoAsync(
            Repository repository,
            string onto,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<InteractiveRebaseTodo> ReadInteractiveRebaseTodoFromCommitAsync(
            Repository repository,
            string firstCommit,
            CancellationToken cancellationToken = default)
        {
            PreparedFromCommits.Add(firstCommit);
            ReadStarted.TrySetResult(true);
            return ReadTodo?.Invoke(repository, firstCommit, cancellationToken)
                   ?? Task.FromResult(ReadTodoResult);
        }

        public Task<RebaseResult> StartInteractiveRebaseAsync(
            Repository repository,
            InteractiveRebasePlan plan,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RebaseResult> StartInteractiveRebaseTodoAsync(
            Repository repository,
            InteractiveRebaseTodo todo,
            CancellationToken cancellationToken = default)
        {
            StartedTodo = todo;
            return Task.FromResult(StartResult);
        }

        public Task<RebaseResult> ContinueRebaseAsync(
            Repository repository,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AbortRebaseAsync(
            Repository repository,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeRepositoryIdentityService : IRepositoryIdentityService
    {
        public TaskCompletionSource<bool> ReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Func<Repository, CancellationToken, Task<RepositoryIdentitySnapshot>>? Read { get; set; }

        public Task<RepositoryIdentitySnapshot> ReadAsync(
            Repository repository,
            CancellationToken cancellationToken = default)
        {
            ReadStarted.TrySetResult(true);
            return Read?.Invoke(repository, cancellationToken)
                   ?? Task.FromResult(Identity("Dmitry", "d@example.test"));
        }

        public Task SaveAsync(
            Repository repository,
            RepositoryIdentityEdit edit,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RemoveOverrideAsync(
            Repository repository,
            RepositoryIdentityField field,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeAuthorChangeService : IInteractiveRebaseAuthorChangeService
    {
        public IReadOnlyList<string> TargetCommits { get; set; } = [];
        public InteractiveRebaseAuthorChangeResult ApplyResult { get; set; } =
            new("changed", 1, 1, 0);
        public InteractiveRebaseAuthorChangeResult ApplyWithDatesResult { get; set; } =
            new("changed-with-dates", 1, 1, 0);
        public IReadOnlyDictionary<string, string>? LastAuthorDates { get; private set; }
        public int ApplyCalls { get; private set; }
        public int ApplyWithDatesCalls { get; private set; }

        public InteractiveRebaseAuthorChangeAnalysis Analyze(
            string todoText,
            int selectionStart,
            int selectionLength) =>
            new(1, 1, 0, 0);

        public IReadOnlyList<string> GetTargetCommits(
            InteractiveRebaseAuthorChangeRequest request) =>
            TargetCommits;

        public InteractiveRebaseAuthorChangeResult Apply(
            InteractiveRebaseAuthorChangeRequest request)
        {
            ApplyCalls++;
            return ApplyResult;
        }

        public InteractiveRebaseAuthorChangeResult Apply(
            InteractiveRebaseAuthorChangeRequest request,
            IReadOnlyDictionary<string, string> originalAuthorDates)
        {
            ApplyWithDatesCalls++;
            LastAuthorDates = originalAuthorDates;
            return ApplyWithDatesResult;
        }
    }

    private sealed class FakeCommitAuthorDateReader : ICommitAuthorDateReader
    {
        public IReadOnlyDictionary<string, string> Result { get; set; } =
            new Dictionary<string, string>();
        public IReadOnlyList<string>? LastCommits { get; private set; }
        public TaskCompletionSource<bool> ReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Func<Repository, IReadOnlyList<string>, CancellationToken, Task<IReadOnlyDictionary<string, string>>>? Read { get; set; }

        public Task<IReadOnlyDictionary<string, string>> ReadCommitAuthorDatesAsync(
            Repository repository,
            IReadOnlyList<string> commits,
            CancellationToken cancellationToken = default)
        {
            LastCommits = commits;
            ReadStarted.TrySetResult(true);
            return Read?.Invoke(repository, commits, cancellationToken)
                   ?? Task.FromResult(Result);
        }
    }
}
