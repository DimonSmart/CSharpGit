using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Application.Tests;

public sealed class RepositoryHistoryRewriteViewModelTests
{
    private static Repository Repository(string name) =>
        new($"/work/{name}", $"/work/{name}", $"/work/{name}/.git", false);

    [Fact]
    public async Task PreparationClassifiesToolAvailabilityEmptyHistoryAndRepositorySwitch()
    {
        var repo = Repository("first");
        var context = new Context { Repository = repo };
        var service = new Service();
        var vm = new RepositoryHistoryRewriteViewModel(service);
        vm.Attach(context);

        service.Available = false;
        Assert.Equal(PathRemovalPreparationStatus.ToolUnavailable,
            (await vm.PreparePathRemovalAsync(repo, "file")).Status);

        service.Available = true;
        Assert.Equal(PathRemovalPreparationStatus.NothingToRemove,
            (await vm.PreparePathRemovalAsync(repo, "file")).Status);

        service.Count = 3;
        var ready = await vm.PreparePathRemovalAsync(repo, "file");
        Assert.Equal(PathRemovalPreparationStatus.Ready, ready.Status);
        Assert.Equal(3, ready.Analysis!.PathHistoryCommitCount);

        context.Repository = Repository("second");
        Assert.Equal(PathRemovalPreparationStatus.RepositoryChanged,
            (await vm.PreparePathRemovalAsync(repo, "file")).Status);
        Assert.Equal(0, service.ExecuteCalls);
    }

    [Fact]
    public async Task ExecutionRetainsResultIfRefreshFailsAndResetsProgress()
    {
        var repo = Repository("first");
        var context = new Context { Repository = repo, RefreshSucceeds = false };
        var service = new Service();
        var vm = new RepositoryHistoryRewriteViewModel(service);
        vm.Attach(context);

        var result = await vm.RemovePathAsync(repo, "secret");
        Assert.Equal(PathRemovalExecutionStatus.RewriteCompletedButRefreshFailed, result.Status);
        Assert.Equal("backup", result.BackupPath);
        Assert.Equal("new-head", result.Result!.HeadObjectId);
        Assert.Equal(4, result.Result.RewrittenCommitCount);
        Assert.False(vm.IsInProgress);
        Assert.Equal(1, context.RewriteCalls);
        Assert.Equal(1, service.ExecuteCalls);
    }

    [Fact]
    public async Task ConcurrentExecutionIsRejectedAndWindowGuardStaysActive()
    {
        var repo = Repository("first");
        var context = new Context { Repository = repo };
        var service = new Service();
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Delay = pending.Task;
        var vm = new RepositoryHistoryRewriteViewModel(service);
        vm.Attach(context);

        var running = vm.RemovePathAsync(repo, "first");
        Assert.True(vm.IsInProgress);
        var second = await vm.RemovePathAsync(repo, "second");
        Assert.Equal(PathRemovalExecutionStatus.Failed, second.Status);
        Assert.Equal(1, service.ExecuteCalls);
        pending.SetResult(true);
        Assert.Equal(PathRemovalExecutionStatus.Completed, (await running).Status);
        Assert.False(vm.IsInProgress);
    }

    [Fact]
    public async Task DestructiveFailureKeepsSafetyMetadata()
    {
        var repo = Repository("first");
        var context = new Context { Repository = repo };
        var service = new Service { ThrowDestructiveFailure = true };
        var vm = new RepositoryHistoryRewriteViewModel(service);
        vm.Attach(context);

        var result = await vm.RemovePathAsync(repo, "secret");
        Assert.Equal(PathRemovalExecutionStatus.Failed, result.Status);
        Assert.True(result.DestructivePhaseStarted);
        Assert.Equal("backup", result.BackupPath);
        Assert.Equal(HistoryRewriteFailureKind.RewriteFailed, result.FailureKind);
        Assert.False(vm.IsInProgress);
    }

    private sealed class Context : IRepositoryHistoryRewriteContext
    {
        public Repository? Repository { get; set; }
        public bool IsBusy { get; set; }
        public bool RefreshSucceeds { get; set; } = true;
        public int RewriteCalls { get; private set; }

        public async Task<bool> RunHistoryRewriteMutationAsync(
            Repository expectedRepository, Func<Task> mutation, string errorContext)
        {
            if (!ReferenceEquals(expectedRepository, Repository)) return false;
            RewriteCalls++;
            try
            {
                await mutation();
                return RefreshSucceeds;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    private sealed class Service : IRepositoryHistoryRewriteService
    {
        public bool Available { get; set; } = true;
        public int Count { get; set; }
        public int ExecuteCalls { get; private set; }
        public Task? Delay { get; set; }
        public bool ThrowDestructiveFailure { get; set; }

        public Task<HistoryRewriteToolStatus> GetToolStatusAsync(
            Repository repository, CancellationToken cancellationToken = default) =>
            Task.FromResult(new HistoryRewriteToolStatus(Available, "1"));

        public Task<PathRemovalAnalysis> AnalyzePathRemovalAsync(
            Repository repository, string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PathRemovalAnalysis(path, Count, [], [], []));

        public async Task<PathRemovalResult> RemovePathFromHistoryAsync(
            Repository repository, string path, CancellationToken cancellationToken = default)
        {
            ExecuteCalls++;
            if (ThrowDestructiveFailure)
                throw new RepositoryHistoryRewriteException(HistoryRewriteFailureKind.RewriteFailed,
                    "rewrite failed", "backup", destructivePhaseStarted: true);
            if (Delay is not null) await Delay;
            return new PathRemovalResult(path, "backup", 4, "new-head");
        }
    }
}
