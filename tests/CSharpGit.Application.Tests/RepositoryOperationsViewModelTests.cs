using System.ComponentModel;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Application.Tests;

public sealed class RepositoryOperationsViewModelTests
{
    [Fact]
    public void RepositoryStatePublishesOperationConflictsAndPreservesSelectionsByIdentity()
    {
        var repository = Repository("repo");
        var context = new FakeContext { Repository = repository };
        var viewModel = CreateViewModel(context, out _, out _, out _, out _);
        var main = new GitBranch("main", "1", true);
        var feature = new GitBranch("feature/a", "2");
        var first = Conflict("a.txt", currentLabel: "ours", incomingLabel: "theirs");
        var second = Conflict("b.txt");

        viewModel.ApplyRepositoryState(
            RepositoryOperation.Rebase,
            new RepositoryOperationState(
                RepositoryOperation.Rebase,
                [first, second],
                CanContinue: true,
                CanAbort: true,
                CanSkip: true),
            [main, feature]);

        Assert.True(viewModel.HasActiveOperation);
        Assert.Equal(RepositoryOperation.Rebase, viewModel.CurrentOperation);
        Assert.Equal("Operation in progress: Rebase", viewModel.OperationDisplay);
        Assert.Same(first, viewModel.SelectedConflict);
        Assert.Equal("ours", viewModel.CurrentSideLabel);
        Assert.Equal("theirs", viewModel.IncomingSideLabel);
        Assert.Same(feature, viewModel.SelectedMergeBranch);

        viewModel.SelectedConflict = second;
        var replacementFeature = new GitBranch("feature/a", "3");
        var replacementSecond = Conflict("b.txt", currentLabel: "current-2");

        viewModel.ApplyRepositoryState(
            RepositoryOperation.Merge,
            new RepositoryOperationState(
                RepositoryOperation.Merge,
                [Conflict("a.txt"), replacementSecond],
                CanContinue: false,
                CanAbort: true,
                CanSkip: false),
            [new GitBranch("main", "4", true), replacementFeature]);

        Assert.Same(replacementSecond, viewModel.SelectedConflict);
        Assert.Equal("current-2", viewModel.CurrentSideLabel);
        Assert.Same(replacementFeature, viewModel.SelectedMergeBranch);
    }

    [Fact]
    public void CommandAvailabilityUsesBackendAndConflictCapabilityFlags()
    {
        var repository = Repository("repo");
        var context = new FakeContext { Repository = repository };
        var viewModel = CreateViewModel(context, out _, out _, out _, out _);
        var conflict = Conflict(
            "conflict.txt",
            canOpen: true,
            canChooseCurrent: true,
            canChooseIncoming: false,
            canKeepDeletion: true,
            canStage: false,
            canRunMergeTool: true);

        viewModel.ApplyRepositoryState(
            RepositoryOperation.CherryPick,
            new RepositoryOperationState(
                RepositoryOperation.CherryPick,
                [conflict],
                CanContinue: true,
                CanAbort: false,
                CanSkip: true),
            [new GitBranch("main", "1", true), new GitBranch("feature/a", "2")]);

        Assert.True(viewModel.ContinueOperationCommand.CanExecute(null));
        Assert.False(viewModel.AbortOperationCommand.CanExecute(null));
        Assert.True(viewModel.SkipOperationCommand.CanExecute(null));
        Assert.True(viewModel.OpenConflictCommand.CanExecute(null));
        Assert.True(viewModel.ChooseCurrentCommand.CanExecute(null));
        Assert.False(viewModel.ChooseIncomingCommand.CanExecute(null));
        Assert.True(viewModel.KeepDeletionCommand.CanExecute(null));
        Assert.False(viewModel.StageConflictCommand.CanExecute(null));
        Assert.True(viewModel.MergeToolCommand.CanExecute(null));
        Assert.True(viewModel.MergeToolWorkflowCommand.CanExecute(null));

        context.IsBusy = true;

        Assert.False(viewModel.ContinueOperationCommand.CanExecute(null));
        Assert.False(viewModel.OpenConflictCommand.CanExecute(null));
        Assert.False(viewModel.MergeToolWorkflowCommand.CanExecute(null));
    }

    [Fact]
    public async Task MergeUsesRepositoryMutationLifecycleAndPublishesResultMessage()
    {
        var repository = Repository("repo");
        var context = new FakeContext { Repository = repository };
        var viewModel = CreateViewModel(context, out var merge, out _, out _, out _);
        var branch = new GitBranch("feature/a", "2");
        merge.Result = new MergeResult(MergeResultKind.Conflicts, "merge stopped on conflicts");

        viewModel.ApplyRepositoryState(
            RepositoryOperation.None,
            RepositoryOperationState.None,
            [new GitBranch("main", "1", true), branch]);

        await ExecuteAsync(viewModel.MergeCommand);

        Assert.Equal(["feature/a"], merge.Branches);
        Assert.Equal(1, context.MutationLifecycleCalls);
        Assert.Equal("merge stopped on conflicts", viewModel.OperationDisplay);
    }

    [Fact]
    public async Task ConflictActionsUseFeatureServicesAndRepositoryLifecycle()
    {
        var repository = Repository("repo");
        var context = new FakeContext { Repository = repository };
        var viewModel = CreateViewModel(
            context,
            out _,
            out var conflicts,
            out _,
            out var externalTools);
        var conflict = Conflict(
            "conflict.txt",
            canOpen: true,
            canChooseCurrent: true,
            canChooseIncoming: true,
            canKeepDeletion: true,
            canStage: true,
            canRunMergeTool: true);

        viewModel.ApplyRepositoryState(
            RepositoryOperation.Merge,
            new RepositoryOperationState(
                RepositoryOperation.Merge,
                [conflict],
                CanContinue: true,
                CanAbort: true,
                CanSkip: false),
            []);

        await ExecuteAsync(viewModel.OpenConflictCommand);
        await ExecuteAsync(viewModel.ChooseCurrentCommand);
        await ExecuteAsync(viewModel.ChooseIncomingCommand);
        await ExecuteAsync(viewModel.KeepDeletionCommand);
        await ExecuteAsync(viewModel.StageConflictCommand);
        await ExecuteAsync(viewModel.MergeToolCommand);
        await ExecuteAsync(viewModel.MergeToolWorkflowCommand);

        Assert.Equal(
            [
                "choose:conflict.txt:CurrentLocal",
                "choose:conflict.txt:IncomingRemote",
                "delete:conflict.txt",
                "stage:conflict.txt"
            ],
            conflicts.Calls);
        Assert.Equal(
            [
                "open:conflict.txt",
                "merge-file:conflict.txt",
                "merge-all"
            ],
            externalTools.Calls);
        Assert.Equal(7, context.MutationLifecycleCalls);
    }

    [Fact]
    public async Task ContinueAbortAndSkipUseGenericRepositoryOperationService()
    {
        var repository = Repository("repo");
        var context = new FakeContext { Repository = repository };
        var viewModel = CreateViewModel(context, out _, out _, out var operations, out _);

        viewModel.ApplyRepositoryState(
            RepositoryOperation.Rebase,
            new RepositoryOperationState(
                RepositoryOperation.Rebase,
                [],
                CanContinue: true,
                CanAbort: true,
                CanSkip: true),
            []);

        await ExecuteAsync(viewModel.ContinueOperationCommand);
        await ExecuteAsync(viewModel.AbortOperationCommand);
        await ExecuteAsync(viewModel.SkipOperationCommand);

        Assert.Equal(["continue", "abort", "skip"], operations.Calls);
        Assert.Equal(3, context.MutationLifecycleCalls);
    }

    [Fact]
    public void ClearRepositoryStateRemovesAllRepositorySpecificPresentationState()
    {
        var repository = Repository("repo");
        var context = new FakeContext { Repository = repository };
        var viewModel = CreateViewModel(context, out _, out _, out _, out _);

        viewModel.ApplyRepositoryState(
            RepositoryOperation.Revert,
            new RepositoryOperationState(
                RepositoryOperation.Revert,
                [Conflict("a.txt")],
                CanContinue: true,
                CanAbort: true,
                CanSkip: false),
            [new GitBranch("main", "1", true), new GitBranch("feature/a", "2")]);

        viewModel.ClearRepositoryState();

        Assert.Equal(RepositoryOperation.None, viewModel.CurrentOperation);
        Assert.Equal(RepositoryOperationState.None, viewModel.OperationState);
        Assert.False(viewModel.HasActiveOperation);
        Assert.Equal(string.Empty, viewModel.OperationDisplay);
        Assert.Empty(viewModel.Conflicts);
        Assert.Null(viewModel.SelectedConflict);
        Assert.Null(viewModel.SelectedMergeBranch);
    }

    private static RepositoryOperationsViewModel CreateViewModel(
        FakeContext context,
        out FakeMergeService merge,
        out FakeConflictResolutionService conflicts,
        out FakeRepositoryOperationService operations,
        out FakeExternalGitToolService externalTools)
    {
        merge = new FakeMergeService();
        conflicts = new FakeConflictResolutionService();
        operations = new FakeRepositoryOperationService();
        externalTools = new FakeExternalGitToolService();
        var viewModel = new RepositoryOperationsViewModel(
            merge,
            conflicts,
            operations,
            externalTools);
        viewModel.Attach(context);
        return viewModel;
    }

    private static Task ExecuteAsync(System.Windows.Input.ICommand command) =>
        ((AsyncCommand)command).ExecuteAsync();

    private static Repository Repository(string name) =>
        new($"/work/{name}", $"/work/{name}", $"/work/{name}/.git", false);

    private static ConflictFile Conflict(
        string path,
        bool canOpen = true,
        bool canChooseCurrent = true,
        bool canChooseIncoming = true,
        bool canKeepDeletion = true,
        bool canStage = true,
        bool canRunMergeTool = true,
        string currentLabel = "Current/local",
        string incomingLabel = "Incoming/remote") =>
        new(
            path,
            ConflictKind.Textual,
            IsResolved: false,
            canOpen,
            canChooseCurrent,
            canChooseIncoming,
            canKeepDeletion,
            canStage,
            canRunMergeTool,
            currentLabel,
            incomingLabel);

    private sealed class FakeContext : IRepositoryOperationsContext
    {
        private Repository? _repository;
        private bool _isBusy;

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

        public bool CanRunRepositoryMutation =>
            Repository is not null && !IsBusy;

        public int MutationLifecycleCalls { get; private set; }

        public async Task<bool> RunRepositoryOperationMutationAsync(
            Repository expectedRepository,
            Func<Task> mutation,
            string? errorContext = null)
        {
            if (!ReferenceEquals(Repository, expectedRepository)
                || !CanRunRepositoryMutation)
            {
                return false;
            }

            MutationLifecycleCalls++;
            await mutation();
            return true;
        }
    }

    private sealed class FakeMergeService : IMergeService
    {
        public List<string> Branches { get; } = [];
        public MergeResult Result { get; set; } =
            new(MergeResultKind.UpToDate, "up to date");

        public Task<MergeResult> MergeAsync(
            Repository repository,
            string branch,
            CancellationToken cancellationToken = default)
        {
            Branches.Add(branch);
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeConflictResolutionService : IConflictResolutionService
    {
        public List<string> Calls { get; } = [];

        public Task ChooseConflictSideAsync(
            Repository repository,
            ConflictFile conflict,
            ConflictResolutionSide side,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"choose:{conflict.Path}:{side}");
            return Task.CompletedTask;
        }

        public Task KeepConflictDeletionAsync(
            Repository repository,
            ConflictFile conflict,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"delete:{conflict.Path}");
            return Task.CompletedTask;
        }

        public Task StageResolvedConflictAsync(
            Repository repository,
            ConflictFile conflict,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"stage:{conflict.Path}");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRepositoryOperationService : IRepositoryOperationService
    {
        public List<string> Calls { get; } = [];

        public Task ContinueOperationAsync(
            Repository repository,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("continue");
            return Task.CompletedTask;
        }

        public Task AbortOperationAsync(
            Repository repository,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("abort");
            return Task.CompletedTask;
        }

        public Task SkipOperationAsync(
            Repository repository,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("skip");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeExternalGitToolService : IExternalGitToolService
    {
        public List<string> Calls { get; } = [];

        public Task OpenEditorAsync(
            Repository? repository,
            string filePath,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RunExternalDiffAsync(
            Repository repository,
            DiffFileVersionPair pair,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RunMergeToolForFileAsync(
            Repository repository,
            ConflictFile conflict,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"merge-file:{conflict.Path}");
            return Task.CompletedTask;
        }

        public Task RunMergeToolWorkflowAsync(
            Repository repository,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("merge-all");
            return Task.CompletedTask;
        }

        public Task OpenConflictInEditorAsync(
            Repository repository,
            ConflictFile conflict,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"open:{conflict.Path}");
            return Task.CompletedTask;
        }

        public Task TestAsync(
            Repository? repository,
            GitToolKind kind,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
