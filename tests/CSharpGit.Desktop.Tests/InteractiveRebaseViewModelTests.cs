using System.ComponentModel;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Desktop.Tests;

public sealed class InteractiveRebaseViewModelTests
{
    [Fact]
    public async Task PrepareAndStartOwnTodoAndPublishOperationMessage()
    {
        var fixture = new Fixture();
        fixture.Service.NextTodo = new InteractiveRebaseTodo(
            "base",
            "pick abc subject",
            new InteractiveRebaseSourceSnapshot("head", "refs/heads/main"));
        fixture.Service.StartResult = new RebaseResult(
            RebaseResultKind.Paused,
            "Rebase started");

        Assert.True(await fixture.ViewModel.PrepareInteractiveRebaseFromCommitAsync("abc"));
        Assert.Equal("base", fixture.ViewModel.RebaseOnto);
        Assert.Equal("pick abc subject", fixture.ViewModel.RebaseTodoText);

        await fixture.ViewModel.StartPreparedInteractiveRebaseAsync("reword abc subject");

        Assert.Equal("reword abc subject", fixture.Service.StartedTodo?.TodoText);
        Assert.Equal("Rebase started", fixture.Context.PublishedMessage);
        Assert.Equal(1, fixture.Context.MutationCalls);
        Assert.Equal(string.Empty, fixture.ViewModel.RebaseOnto);
        Assert.Equal(string.Empty, fixture.ViewModel.RebaseTodoText);
    }

    [Fact]
    public async Task RepositorySwitchDuringPreparationDoesNotPublishStaleTodo()
    {
        var fixture = new Fixture();
        var completion = new TaskCompletionSource<InteractiveRebaseTodo>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Service.ReadTodoAsync = (_, _, _) => completion.Task;

        var prepare = fixture.ViewModel.PrepareInteractiveRebaseFromCommitAsync("abc");
        fixture.Context.Repository = Repository("other");
        fixture.Context.RaiseRepositoryChanged();

        completion.SetResult(new InteractiveRebaseTodo(
            "old-base",
            "pick old old",
            new InteractiveRebaseSourceSnapshot("old-head", "refs/heads/main")));

        Assert.False(await prepare);
        Assert.Equal(string.Empty, fixture.ViewModel.RebaseOnto);
        Assert.Equal(string.Empty, fixture.ViewModel.RebaseTodoText);
    }

    [Fact]
    public async Task ResetIdentityWithoutDateResetReadsAndPreservesOriginalAuthorDates()
    {
        var fixture = new Fixture();
        fixture.AuthorChange.TargetCommits = ["abc", "def"];
        fixture.AuthorDateReader.Result = new Dictionary<string, string>
        {
            ["abc"] = "100 +0000",
            ["def"] = "200 +0000"
        };
        var request = new InteractiveRebaseAuthorChangeRequest(
            "pick abc one\npick def two",
            0,
            0,
            InteractiveRebaseAuthorChangeScope.AllEligibleCommits,
            string.Empty,
            string.Empty,
            ResetAuthorDate: false,
            ResetToCurrentGitIdentity: true);

        var result = await fixture.ViewModel.ApplyInteractiveRebaseAuthorChangeAsync(request);

        Assert.Equal(["abc", "def"], fixture.AuthorDateReader.LastCommits);
        Assert.Same(fixture.AuthorDateReader.Result, fixture.AuthorChange.LastAuthorDates);
        Assert.Equal("changed", result.TodoText);
    }

    [Fact]
    public void ClearRepositoryStateInvalidatesPreparedPresentation()
    {
        var fixture = new Fixture();
        fixture.ViewModel.RebaseTodoText = "edited";

        fixture.ViewModel.ClearRepositoryState();

        Assert.Equal(string.Empty, fixture.ViewModel.RebaseOnto);
        Assert.Equal(string.Empty, fixture.ViewModel.RebaseTodoText);
    }

    private static Repository Repository(string name) =>
        new($"/work/{name}", $"/work/{name}", $"/work/{name}/.git", false);

    private sealed class Fixture
    {
        public Fixture()
        {
            Context = new FakeContext { Repository = Repository("repo") };
            Service = new FakeInteractiveRebaseService();
            Identity = new FakeIdentityService();
            AuthorChange = new FakeAuthorChangeService();
            AuthorDateReader = new FakeAuthorDateReader();
            ViewModel = new InteractiveRebaseViewModel(
                Service,
                Identity,
                AuthorChange,
                AuthorDateReader);
            ViewModel.Attach(Context);
        }

        public FakeContext Context { get; }
        public FakeInteractiveRebaseService Service { get; }
        public FakeIdentityService Identity { get; }
        public FakeAuthorChangeService AuthorChange { get; }
        public FakeAuthorDateReader AuthorDateReader { get; }
        public InteractiveRebaseViewModel ViewModel { get; }
    }

    private sealed class FakeContext : IInteractiveRebaseRepositoryContext
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public Repository? Repository { get; set; }
        public bool IsBusy { get; set; }
        public RepositoryOperation CurrentOperation { get; set; }
        public int MutationCalls { get; private set; }
        public string? PublishedMessage { get; private set; }
        public string? ErrorMessage { get; private set; }

        public async Task<bool> RunInteractiveRebaseMutationAsync(
            Repository expectedRepository,
            Func<Task> mutation,
            string? errorContext = null)
        {
            if (!ReferenceEquals(Repository, expectedRepository)
                || CurrentOperation != RepositoryOperation.None)
            {
                return false;
            }

            MutationCalls++;
            await mutation();
            return true;
        }

        public void PublishOperationMessage(string message) =>
            PublishedMessage = message;

        public void ReportInteractiveRebaseError(string message) =>
            ErrorMessage = message;

        public void RaiseRepositoryChanged() =>
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(
                    nameof(IInteractiveRebaseRepositoryContext.Repository)));
    }

    private sealed class FakeInteractiveRebaseService : IInteractiveRebaseService
    {
        public InteractiveRebaseTodo NextTodo { get; set; } =
            new(
                "base",
                "pick abc subject",
                new InteractiveRebaseSourceSnapshot("head", "refs/heads/main"));

        public Func<Repository, string, CancellationToken, Task<InteractiveRebaseTodo>>? ReadTodoAsync { get; set; }
        public RebaseResult StartResult { get; set; } =
            new(RebaseResultKind.Completed, "done");
        public InteractiveRebaseTodo? StartedTodo { get; private set; }

        public Task<InteractiveRebaseTodo> ReadInteractiveRebaseTodoFromCommitAsync(
            Repository repository,
            string firstCommit,
            CancellationToken cancellationToken = default) =>
            ReadTodoAsync?.Invoke(repository, firstCommit, cancellationToken)
            ?? Task.FromResult(NextTodo);

        public Task<RebaseResult> StartInteractiveRebaseTodoAsync(
            Repository repository,
            InteractiveRebaseTodo todo,
            CancellationToken cancellationToken = default)
        {
            StartedTodo = todo;
            return Task.FromResult(StartResult);
        }

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

        public Task<RebaseResult> StartInteractiveRebaseAsync(
            Repository repository,
            InteractiveRebasePlan plan,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RebaseResult> ContinueRebaseAsync(
            Repository repository,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AbortRebaseAsync(
            Repository repository,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeIdentityService : IRepositoryIdentityService
    {
        public Task<RepositoryIdentitySnapshot> ReadAsync(
            Repository repository,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SaveAsync(
            Repository repository,
            RepositoryIdentityEdit edit,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RemoveOverrideAsync(
            Repository repository,
            RepositoryIdentityField field,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeAuthorChangeService : IInteractiveRebaseAuthorChangeService
    {
        public IReadOnlyList<string> TargetCommits { get; set; } = [];
        public IReadOnlyDictionary<string, string>? LastAuthorDates { get; private set; }

        public InteractiveRebaseAuthorChangeAnalysis Analyze(
            string todoText,
            int selectionStart,
            int selectionLength) =>
            new(0, 0, 0, 0);

        public IReadOnlyList<string> GetTargetCommits(
            InteractiveRebaseAuthorChangeRequest request) =>
            TargetCommits;

        public InteractiveRebaseAuthorChangeResult Apply(
            InteractiveRebaseAuthorChangeRequest request) =>
            new("changed", 0, 0, 0);

        public InteractiveRebaseAuthorChangeResult Apply(
            InteractiveRebaseAuthorChangeRequest request,
            IReadOnlyDictionary<string, string> originalAuthorDates)
        {
            LastAuthorDates = originalAuthorDates;
            return new("changed", TargetCommits.Count, TargetCommits.Count, 0);
        }
    }

    private sealed class FakeAuthorDateReader : ICommitAuthorDateReader
    {
        public IReadOnlyDictionary<string, string> Result { get; set; } =
            new Dictionary<string, string>();

        public IReadOnlyList<string>? LastCommits { get; private set; }

        public Task<IReadOnlyDictionary<string, string>> ReadCommitAuthorDatesAsync(
            Repository repository,
            IReadOnlyList<string> commits,
            CancellationToken cancellationToken = default)
        {
            LastCommits = commits;
            return Task.FromResult(Result);
        }
    }
}
