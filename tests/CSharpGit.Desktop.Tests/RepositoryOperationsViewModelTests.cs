using System.ComponentModel;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Desktop.Tests;

public sealed class RepositoryOperationsViewModelTests
{
    [Fact]
    public void RepositoryStateOwnsOperationConflictsAndMergeSelection()
    {
        var fixture = new Fixture();
        var conflict = Conflict("src/a.cs");
        var branches = new[]
        {
            Branch("main", isCurrent: true),
            Branch("feature"),
            Branch("other")
        };

        fixture.ViewModel.ApplyRepositoryState(
            RepositoryOperation.Rebase,
            new RepositoryOperationState(
                RepositoryOperation.Rebase,
                [conflict],
                CanContinue: true,
                CanAbort: true,
                CanSkip: true),
            branches);

        Assert.Equal(RepositoryOperation.Rebase, fixture.ViewModel.CurrentOperation);
        Assert.True(fixture.ViewModel.HasActiveOperation);
        Assert.Equal("Operation in progress: Rebase", fixture.ViewModel.OperationDisplay);
        Assert.Same(conflict, fixture.ViewModel.SelectedConflict);
        Assert.Equal("feature", fixture.ViewModel.SelectedMergeBranch?.Name);

        fixture.ViewModel.SelectedMergeBranch = branches[2];
        var replacement = Conflict("src/a.cs");
        fixture.ViewModel.ApplyRepositoryState(
            RepositoryOperation.Rebase,
            new RepositoryOperationState(
                RepositoryOperation.Rebase,
                [replacement],
                CanContinue: true,
                CanAbort: true,
                CanSkip: false),
            branches.Select(branch => branch with { }).ToArray());

        Assert.Equal(replacement.Path, fixture.ViewModel.SelectedConflict?.Path);
        Assert.Equal("other", fixture.ViewModel.SelectedMergeBranch?.Name);
        Assert.False(fixture.ViewModel.OperationState.CanSkip);
    }

    [Fact]
    public async Task GenericOperationCommandsUseBackendAvailabilityAndMutationLifecycle()
    {
        var fixture = new Fixture();
        fixture.ViewModel.ApplyRepositoryState(
            RepositoryOperation.CherryPick,
            new RepositoryOperationState(
                RepositoryOperation.CherryPick,
                [],
                CanContinue: true,
                CanAbort: true,
                CanSkip: true),
            [Branch("main", isCurrent: true)]);

        await ExecuteAsync(fixture.ViewModel.ContinueOperationCommand);
        await ExecuteAsync(fixture.ViewModel.AbortOperationCommand);
        await ExecuteAsync(fixture.ViewModel.SkipOperationCommand);

        Assert.Equal(3, fixture.Context.MutationCalls);
        Assert.Equal(1, fixture.OperationService.ContinueCalls);
        Assert.Equal(1, fixture.OperationService.AbortCalls);
        Assert.Equal(1, fixture.OperationService.SkipCalls);

        fixture.ViewModel.ApplyRepositoryState(
            RepositoryOperation.Merge,
            new RepositoryOperationState(
                RepositoryOperation.Merge,
                [],
                CanContinue: false,
                CanAbort: false,
                CanSkip: false),
            [Branch("main", isCurrent: true)]);

        Assert.False(fixture.ViewModel.ContinueOperationCommand.CanExecute(null));
        Assert.False(fixture.ViewModel.AbortOperationCommand.CanExecute(null));
        Assert.False(fixture.ViewModel.SkipOperationCommand.CanExecute(null));
    }

    [Fact]
    public async Task ConflictCommandsUseConflictCapabilityFlags()
    {
        var fixture = new Fixture();
        var conflict = Conflict("src/a.cs");
        fixture.ViewModel.ApplyRepositoryState(
            RepositoryOperation.Merge,
            new RepositoryOperationState(
                RepositoryOperation.Merge,
                [conflict],
                CanContinue: false,
                CanAbort: true,
                CanSkip: false),
            [Branch("main", isCurrent: true)]);

        await ExecuteAsync(fixture.ViewModel.ChooseCurrentCommand);
        await ExecuteAsync(fixture.ViewModel.ChooseIncomingCommand);
        await ExecuteAsync(fixture.ViewModel.KeepDeletionCommand);
        await ExecuteAsync(fixture.ViewModel.StageConflictCommand);
        await ExecuteAsync(fixture.ViewModel.MergeToolCommand);
        await ExecuteAsync(fixture.ViewModel.MergeToolWorkflowCommand);
        await ExecuteAsync(fixture.ViewModel.OpenConflictCommand);

        Assert.Equal(
            ["current:src/a.cs", "incoming:src/a.cs", "delete:src/a.cs", "stage:src/a.cs"],
            fixture.ConflictService.Calls);
        Assert.Equal(
            ["file:src/a.cs", "all", "open:src/a.cs"],
            fixture.ExternalToolService.Calls);
        Assert.Equal(7, fixture.Context.MutationCalls);
    }

    [Fact]
    public async Task MergeUsesSelectedBranchAndPublishesBackendMessage()
    {
        var fixture = new Fixture();
        fixture.MergeService.Result = new MergeResult(
            MergeResultKind.MergeCommit,
            "Merged feature");
        fixture.ViewModel.ApplyRepositoryState(
            RepositoryOperation.None,
            RepositoryOperationState.None,
            [Branch("main", isCurrent: true), Branch("feature")]);

        await ExecuteAsync(fixture.ViewModel.MergeCommand);

        Assert.Equal("feature", fixture.MergeService.LastBranch);
        Assert.Equal("Merged feature", fixture.ViewModel.OperationDisplay);
        Assert.Equal(1, fixture.Context.MutationCalls);
    }

    private static Task ExecuteAsync(System.Windows.Input.ICommand command) =>
        ((AsyncCommand)command).ExecuteAsync();

    private static GitBranch Branch(string name, bool isCurrent = false) =>
        new(name, $"commit-{name}", isCurrent);

    private static ConflictFile Conflict(string path) =>
        new(
            path,
            ConflictKind.Textual,
            IsResolved: false,
            CanOpenManually: true,
            CanChooseCurrentLocal: true,
            CanChooseIncomingRemote: true,
            CanKeepDeletion: true,
            CanStage: true,
            CanRunMergeTool: true,
            "Current",
            "Incoming");

    private sealed class Fixture
    {
        public Fixture()
        {
            Context = new FakeContext();
            MergeService = new FakeMergeService();
            ConflictService = new FakeConflictResolutionService();
            OperationService = new FakeOperationService();
            ExternalToolService = new FakeExternalToolService();
            ViewModel = new RepositoryOperationsViewModel(
                MergeService,
                ConflictService,
                OperationService,
                ExternalToolService);
            ViewModel.Attach(Context);
        }

        public FakeContext Context { get; }
        public FakeMergeService MergeService { get; }
        public FakeConflictResolutionService ConflictService { get; }
        public FakeOperationService OperationService { get; }
        public FakeExternalToolService ExternalToolService { get; }
        public RepositoryOperationsViewModel ViewModel { get; }
    }

    private sealed class FakeContext : IRepositoryOperationsContext
    {
        public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }

        public Repository? Repository { get; set; } =
            new("/work/repo", "/work/repo", "/work/repo/.git", false);

        public bool IsBusy { get; set; }
        public bool CanRunRepositoryMutation { get; set; } = true;
        public int MutationCalls { get; private set; }

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

            MutationCalls++;
            await mutation();
            return true;
        }
    }

    private sealed class FakeMergeService : IMergeService
    {
        public MergeResult Result { get; set; } =
            new(MergeResultKind.UpToDate, "Already up to date");
        public string? LastBranch { get; private set; }

        public Task<MergeResult> MergeAsync(
            Repository repository,
            string branch,
            CancellationToken cancellationToken = default)
        {
            LastBranch = branch;
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
            Calls.Add(side == ConflictResolutionSide.CurrentLocal
                ? $"current:{conflict.Path}"
                : $"incoming:{conflict.Path}");
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

    private sealed class FakeOperationService : IRepositoryOperationService
    {
        public int ContinueCalls { get; private set; }
        public int AbortCalls { get; private set; }
        public int SkipCalls { get; private set; }

        public Task ContinueOperationAsync(
            Repository repository,
            CancellationToken cancellationToken = default)
        {
            ContinueCalls++;
            return Task.CompletedTask;
        }

        public Task AbortOperationAsync(
            Repository repository,
            CancellationToken cancellationToken = default)
        {
            AbortCalls++;
            return Task.CompletedTask;
        }

        public Task SkipOperationAsync(
            Repository repository,
            CancellationToken cancellationToken = default)
        {
            SkipCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeExternalToolService : IExternalGitToolService
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
            Calls.Add($"file:{conflict.Path}");
            return Task.CompletedTask;
        }

        public Task RunMergeToolWorkflowAsync(
            Repository repository,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("all");
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
