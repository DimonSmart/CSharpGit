using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Desktop.Tests;

public sealed class WorkingTreeViewModelTests
{
    [Fact]
    public void RepositoryStateIsTheOnlySourceOfWorkingTreeChanges()
    {
        var fixture = new Fixture();
        var changes = new[]
        {
            new WorkingTreeChange("modified.cs", ' ', 'M'),
            new WorkingTreeChange("staged.cs", 'M', ' '),
            new WorkingTreeChange("both.cs", 'M', 'M'),
            new WorkingTreeChange("untracked.cs", '?', '?'),
            new WorkingTreeChange("deleted.cs", ' ', 'D'),
            new WorkingTreeChange("renamed.cs", 'R', ' ', "old.cs"),
            new WorkingTreeChange("conflict.cs", 'U', 'U')
        };

        fixture.ViewModel.ApplyRepositoryState(fixture.Repository, changes);

        Assert.Equal(changes, fixture.ViewModel.Changes);
        Assert.Single(fixture.ViewModel.Changes.Where(change => change.Path == "both.cs"));
    }

    [Fact]
    public async Task StageSelectedUsesBatchOperationAndOneMutationLifecycle()
    {
        var fixture = new Fixture();
        var first = new WorkingTreeChange("a.cs", ' ', 'M');
        var second = new WorkingTreeChange("b.cs", '?', '?');
        fixture.ViewModel.ApplyRepositoryState(fixture.Repository, [first, second]);
        fixture.ViewModel.SetSelection(WorkingTreeDiffKind.Unstaged, [first, second]);

        await ExecuteAsync(fixture.ViewModel.StageSelectedCommand);

        Assert.Equal(1, fixture.Service.StageFilesCalls);
        Assert.Equal(new[] { "a.cs", "b.cs" }, fixture.Service.LastStageFiles.Select(change => change.Path));
        Assert.Equal(1, fixture.Context.MutationCalls);
    }

    [Fact]
    public async Task UnstageAllUsesBackendAllOperation()
    {
        var fixture = new Fixture();
        fixture.ViewModel.ApplyRepositoryState(
            fixture.Repository,
            [new WorkingTreeChange("a.cs", 'M', ' ')]);

        await ExecuteAsync(fixture.ViewModel.UnstageAllCommand);

        Assert.Equal(1, fixture.Service.UnstageAllCalls);
        Assert.Equal(1, fixture.Context.MutationCalls);
    }

    [Fact]
    public async Task DiscardSelectedKeepsApplicationWorkflowAndRefreshesOnce()
    {
        var fixture = new Fixture();
        var first = new WorkingTreeChange("a.cs", ' ', 'M');
        var second = new WorkingTreeChange("b.cs", ' ', 'D');
        fixture.ViewModel.ApplyRepositoryState(fixture.Repository, [first, second]);
        fixture.ViewModel.SetSelection(WorkingTreeDiffKind.Unstaged, [first, second]);

        await ExecuteAsync(fixture.ViewModel.RequestDiscardSelectedCommand);
        Assert.Contains("2 selected files", fixture.ViewModel.BatchDiscardConfirmationMessage, StringComparison.Ordinal);

        await ExecuteAsync(fixture.ViewModel.ConfirmBatchDiscardCommand);

        Assert.Equal(1, fixture.Service.DiscardTrackedFilesCalls);
        Assert.Equal(1, fixture.Context.MutationCalls);
        Assert.Empty(fixture.ViewModel.SelectedUnstagedChanges);
    }

    [Fact]
    public async Task StagedAndUnstagedSamePathKeepDistinctDiffContexts()
    {
        var fixture = new Fixture();
        var both = new WorkingTreeChange("both.cs", 'M', 'M');
        fixture.ViewModel.ApplyRepositoryState(fixture.Repository, [both]);

        await fixture.ViewModel.SelectChangeAsync(both, WorkingTreeDiffKind.Unstaged);
        await fixture.ViewModel.SelectChangeAsync(both, WorkingTreeDiffKind.Staged);

        Assert.Equal(
            new[] { WorkingTreeDiffKind.Unstaged, WorkingTreeDiffKind.Staged },
            fixture.DiffService.Kinds);
        Assert.Equal(WorkingTreeDiffKind.Staged, fixture.ViewModel.SelectedDiffKind);
        Assert.Equal("both.cs", fixture.ViewModel.SelectedDiff?.Path);
    }

    [Fact]
    public async Task LatestDiffSelectionWinsOutOfOrderCompletion()
    {
        var fixture = new Fixture(controlledDiff: true);
        var a = new WorkingTreeChange("a.cs", ' ', 'M');
        var b = new WorkingTreeChange("b.cs", ' ', 'M');
        fixture.ViewModel.ApplyRepositoryState(fixture.Repository, [a, b]);

        var aLoad = fixture.ViewModel.SelectChangeAsync(a, WorkingTreeDiffKind.Unstaged);
        var bLoad = fixture.ViewModel.SelectChangeAsync(b, WorkingTreeDiffKind.Unstaged);

        fixture.DiffService.Complete("b.cs");
        await bLoad;
        fixture.DiffService.Complete("a.cs");
        await aLoad;

        Assert.Same(b, fixture.ViewModel.SelectedChange);
        Assert.Equal("b.cs", fixture.ViewModel.SelectedDiff?.Path);
    }

    [Fact]
    public async Task RepositorySwitchRejectsOldDiffCompletion()
    {
        var fixture = new Fixture(controlledDiff: true);
        var a = new WorkingTreeChange("a.cs", ' ', 'M');
        fixture.ViewModel.ApplyRepositoryState(fixture.Repository, [a]);

        var load = fixture.ViewModel.SelectChangeAsync(a, WorkingTreeDiffKind.Unstaged);
        var nextRepository = new Repository("/next", "/next", "/next/.git", false);
        fixture.Context.Repository = nextRepository;
        fixture.ViewModel.OnRepositoryChanged(nextRepository);

        fixture.DiffService.Complete("a.cs");
        await load;

        Assert.Null(fixture.ViewModel.SelectedChange);
        Assert.Null(fixture.ViewModel.SelectedDiff);
        Assert.Empty(fixture.ViewModel.Changes);
    }

    [Fact]
    public async Task BackendFailureIsPublishedAsFeatureMutationError()
    {
        var fixture = new Fixture();
        var change = new WorkingTreeChange("a.cs", ' ', 'M');
        fixture.ViewModel.ApplyRepositoryState(fixture.Repository, [change]);
        fixture.ViewModel.SetSelection(WorkingTreeDiffKind.Unstaged, [change]);
        fixture.Service.StageFilesFailure = new InvalidOperationException("stage failed");

        await ExecuteAsync(fixture.ViewModel.StageSelectedCommand);

        Assert.Contains("stage failed", fixture.ViewModel.MutationErrorMessage, StringComparison.Ordinal);
        Assert.Equal(1, fixture.Context.MutationCalls);
    }

    private static Task ExecuteAsync(System.Windows.Input.ICommand command) =>
        ((AsyncCommand)command).ExecuteAsync();

    private sealed class Fixture
    {
        public Fixture(bool controlledDiff = false)
        {
            Repository = new Repository("/repo", "/repo", "/repo/.git", false);
            Service = new FakeWorkingTreeService();
            DiffService = new FakeDiffService(controlledDiff);
            Context = new FakeContext { Repository = Repository };
            ViewModel = new WorkingTreeViewModel(Service, DiffService);
            ViewModel.Attach(Context);
            ViewModel.OnRepositoryChanged(Repository);
        }

        public Repository Repository { get; }
        public FakeWorkingTreeService Service { get; }
        public FakeDiffService DiffService { get; }
        public FakeContext Context { get; }
        public WorkingTreeViewModel ViewModel { get; }
    }

    private sealed class FakeContext : IWorkingTreeRepositoryContext
    {
        public Repository? Repository { get; set; }
        public bool IsBusy { get; set; }
        public bool CanRunRepositoryMutation { get; set; } = true;
        public bool CanRunWorkingTreeMutation { get; set; } = true;
        public string? ErrorMessage { get; private set; }
        public int MutationCalls { get; private set; }

        public async Task<bool> RunWorkingTreeMutationAsync(
            Repository expectedRepository,
            Func<Task> mutation,
            string errorContext,
            Action? beforeMutation = null)
        {
            if (!ReferenceEquals(expectedRepository, Repository)) return false;
            MutationCalls++;
            beforeMutation?.Invoke();
            try
            {
                await mutation();
                ErrorMessage = null;
                return true;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                ErrorMessage = $"{errorContext}\nGit: {exception.Message}";
                return false;
            }
        }

        public void ReportWorkingTreeError(string message) => ErrorMessage = message;
    }

    private sealed class FakeWorkingTreeService : IWorkingTreeService
    {
        public int StageFilesCalls { get; private set; }
        public int UnstageAllCalls { get; private set; }
        public int DiscardTrackedFilesCalls { get; private set; }
        public IReadOnlyCollection<WorkingTreeChange> LastStageFiles { get; private set; } = [];
        public Exception? StageFilesFailure { get; set; }

        public Task StageFileAsync(Repository repository, WorkingTreeChange change, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task StageFilesAsync(
            Repository repository,
            IReadOnlyCollection<WorkingTreeChange> changes,
            CancellationToken cancellationToken = default)
        {
            StageFilesCalls++;
            LastStageFiles = changes.ToArray();
            return StageFilesFailure is null
                ? Task.CompletedTask
                : Task.FromException(StageFilesFailure);
        }

        public Task StageAllAsync(Repository repository, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task UnstageFileAsync(Repository repository, WorkingTreeChange change, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task UnstageFilesAsync(
            Repository repository,
            IReadOnlyCollection<WorkingTreeChange> changes,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task UnstageAllAsync(Repository repository, CancellationToken cancellationToken = default)
        {
            UnstageAllCalls++;
            return Task.CompletedTask;
        }

        public Task DiscardTrackedFilesAsync(
            Repository repository,
            IReadOnlyCollection<WorkingTreeChange> changes,
            CancellationToken cancellationToken = default)
        {
            DiscardTrackedFilesCalls++;
            return Task.CompletedTask;
        }

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

        public Task CommitAsync(
            Repository repository,
            string message,
            bool amend = false,
            bool intentionalEmpty = false,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeDiffService : IWorkingTreeDiffService
    {
        private readonly bool _controlled;
        private readonly Dictionary<string, TaskCompletionSource<FileDiff>> _requests =
            new(StringComparer.Ordinal);

        public FakeDiffService(bool controlled) => _controlled = controlled;

        public List<WorkingTreeDiffKind> Kinds { get; } = [];

        public Task<FileDiff> ReadDiffAsync(
            Repository repository,
            WorkingTreeChange change,
            WorkingTreeDiffKind kind,
            CancellationToken cancellationToken = default)
        {
            Kinds.Add(kind);
            if (!_controlled)
                return Task.FromResult(Diff(change.Path));

            var source = new TaskCompletionSource<FileDiff>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _requests[change.Path] = source;
            return source.Task;
        }

        public void Complete(string path) => _requests[path].TrySetResult(Diff(path));

        private static FileDiff Diff(string path) =>
            new(path, false, [new DiffLine("+change", DiffLineKind.Added)]);
    }
}
