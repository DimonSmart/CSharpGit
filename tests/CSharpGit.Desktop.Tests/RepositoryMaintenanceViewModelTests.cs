using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.Threading;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Desktop.Tests;

public sealed class RepositoryMaintenanceViewModelTests
{
    private static readonly Repository RepositoryA = new("/repo", "/repo", "/repo/.git", false);
    private static readonly Repository RepositoryB = new("/other", "/other", "/other/.git", false);
    private static readonly RepositoryStorageStatistics Statistics = new(3, 10, 1, 200, 600);

    [Fact]
    public async Task PreparationLoadsStatisticsAndDefaultsToReady()
    {
        var service = new Service();
        var context = new Context();
        using var vm = Create(service, context);
        Assert.True(await vm.PrepareAsync(RepositoryA));
        Assert.Equal(RepositoryMaintenancePhase.Ready, vm.Phase);
        Assert.Equal(Statistics, vm.Before);
        Assert.Equal(1, service.ReadCount);
    }

    [Fact]
    public async Task PreparationRejectsMissingRepository()
    {
        var context = new Context { CurrentMaintenanceRepository = null };
        using var vm = Create(new Service(), context);
        Assert.False(vm.CanStart);
        Assert.False(await vm.PrepareAsync(RepositoryA));
        Assert.Null(vm.Before);
    }

    [Fact]
    public async Task GcUsesTypedOptionsAndTargetedRefresh()
    {
        var service = new Service();
        var context = new Context { ShowMaintenanceReflog = true };
        var worktreeRefreshCount = 0;
        using var vm = Create(service, context, () => { worktreeRefreshCount++; return Task.CompletedTask; });
        Assert.True(await vm.PrepareAsync(RepositoryA));
        var options = new RepositoryGcOptions(true, true, true);
        var result = await vm.ExecuteAsync(RepositoryA, options);
        Assert.Equal(RepositoryMaintenanceResultKind.Completed, result.Kind);
        Assert.Equal(options, service.Options);
        Assert.Same(RepositoryA, context.ExecutedRepository);
        Assert.True(context.IncludeHistory);
        Assert.Equal(1, context.RefreshCount);
        Assert.Equal(1, worktreeRefreshCount);
        Assert.Equal(2, service.ReadCount);
        Assert.False(vm.IsRunning);
    }

    [Fact]
    public async Task SuccessfulGcRetainsSuccessWhenPostStatisticsFails()
    {
        var service = new Service { FailSecondRead = true };
        using var vm = Create(service, new Context());
        Assert.True(await vm.PrepareAsync(RepositoryA));
        var result = await vm.ExecuteAsync(RepositoryA, new RepositoryGcOptions());
        Assert.Equal(RepositoryMaintenanceResultKind.Completed, result.Kind);
        Assert.NotNull(result.AfterStatisticsFailure);
        Assert.Null(result.GcFailure);
    }

    [Fact]
    public async Task SuccessfulGcRetainsSuccessWhenRefreshFails()
    {
        var context = new Context { FailRefresh = true };
        using var vm = Create(new Service(), context);
        Assert.True(await vm.PrepareAsync(RepositoryA));
        var result = await vm.ExecuteAsync(RepositoryA, new RepositoryGcOptions());
        Assert.Equal(RepositoryMaintenanceResultKind.Completed, result.Kind);
        Assert.NotNull(result.RefreshFailure);
    }

    [Fact]
    public async Task GarbageCollectionFailureRemainsDistinctFromRefreshFailure()
    {
        var service = new Service { FailGarbageCollection = true };
        var context = new Context();
        using var vm = Create(service, context);
        Assert.True(await vm.PrepareAsync(RepositoryA));
        var result = await vm.ExecuteAsync(RepositoryA, new RepositoryGcOptions());
        Assert.Equal(RepositoryMaintenanceResultKind.GcFailed, result.Kind);
        Assert.NotNull(result.GcFailure);
        Assert.Equal(0, context.RefreshCount);
    }

    [Fact]
    public async Task ConcurrentMutationRejectsGcWithoutStarting()
    {
        var service = new Service();
        var context = new Context { GateBusy = true };
        using var vm = Create(service, context);
        Assert.True(await vm.PrepareAsync(RepositoryA));
        var result = await vm.ExecuteAsync(RepositoryA, new RepositoryGcOptions());
        Assert.Equal(RepositoryMaintenanceResultKind.NotStarted, result.Kind);
        Assert.Equal(RepositoryMaintenanceNotStartedReason.GateBusy, result.NotStartedReason);
        Assert.Equal(0, service.RunCount);
    }

    [Fact]
    public async Task SwitchingRepositoryDuringReadDiscardsResult()
    {
        var pending = new TaskCompletionSource<RepositoryStorageStatistics>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new Service { PendingRead = pending.Task };
        var context = new Context();
        using var vm = Create(service, context);
        var preparation = vm.PrepareAsync(RepositoryA);
        context.CurrentMaintenanceRepository = RepositoryB;
        vm.Invalidate();
        pending.SetResult(Statistics);
        Assert.False(await preparation);
        Assert.Null(vm.Before);
    }

    [Fact]
    public async Task ChangingRepositoryAfterPreparationCannotStartGc()
    {
        var service = new Service();
        var context = new Context();
        using var vm = Create(service, context);
        Assert.True(await vm.PrepareAsync(RepositoryA));
        context.CurrentMaintenanceRepository = RepositoryB;
        var result = await vm.ExecuteAsync(RepositoryA, new RepositoryGcOptions());
        Assert.Equal(RepositoryMaintenanceResultKind.NotStarted, result.Kind);
        Assert.Equal(0, service.RunCount);
    }

    [Fact]
    public async Task RunningStateIncludesWorktreeRefreshAndRejectsRepeat()
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new Context();
        using var vm = Create(new Service(), context, () => pending.Task);
        Assert.True(await vm.PrepareAsync(RepositoryA));
        var running = vm.ExecuteAsync(RepositoryA, new RepositoryGcOptions());
        Assert.True(vm.IsRunning);
        var duplicate = await vm.ExecuteAsync(RepositoryA, new RepositoryGcOptions());
        Assert.Equal(RepositoryMaintenanceResultKind.NotStarted, duplicate.Kind);
        Assert.True(vm.IsRunning);
        pending.SetResult();
        Assert.Equal(RepositoryMaintenanceResultKind.Completed, (await running).Kind);
        Assert.False(vm.IsRunning);
    }

    private static RepositoryMaintenanceViewModel Create(
        Service service, Context context, Func<Task>? refresh = null)
    {
        var vm = new RepositoryMaintenanceViewModel(service, new DirectDispatcher());
        vm.Attach(context, refresh ?? (() => Task.CompletedTask));
        return vm;
    }

    private sealed class DirectDispatcher : IUiDispatcher
    {
        public bool HasThreadAccess => true;
        public bool TryEnqueue(Action action) { action(); return true; }
    }

    private sealed class Service : IRepositoryMaintenanceService
    {
        public int ReadCount { get; private set; }
        public int RunCount { get; private set; }
        public RepositoryGcOptions? Options { get; private set; }
        public bool FailSecondRead { get; init; }
        public bool FailGarbageCollection { get; init; }
        public Task<RepositoryStorageStatistics>? PendingRead { get; init; }

        public Task<RepositoryStorageStatistics> GetStorageStatisticsAsync(
            Repository repository, CancellationToken cancellationToken = default)
        {
            ReadCount++;
            if (FailSecondRead && ReadCount > 1)
                throw new IOException("after statistics failed");
            return PendingRead ?? Task.FromResult(Statistics);
        }

        public Task GarbageCollectAsync(Repository repository, RepositoryGcOptions options,
            CancellationToken cancellationToken = default)
        {
            RunCount++;
            Options = options;
            if (FailGarbageCollection) throw new IOException("gc failed");
            return Task.CompletedTask;
        }
    }

    private sealed class Context : IRepositoryMaintenanceContext
    {
        public event EventHandler? MaintenanceAvailabilityChanged;
        public Repository? CurrentMaintenanceRepository { get; set; } = RepositoryA;
        public bool CanStartRepositoryMaintenance => CurrentMaintenanceRepository is not null && !IsBusy;
        public bool IsBusy { get; set; }
        public bool ShowMaintenanceReflog { get; set; }
        public bool GateBusy { get; set; }
        public bool FailRefresh { get; set; }
        public int RefreshCount { get; private set; }
        public bool IncludeHistory { get; private set; }
        public Repository? ExecutedRepository { get; private set; }
        public bool IsCurrentMaintenanceRepository(Repository repository) =>
            ReferenceEquals(repository, CurrentMaintenanceRepository);

        public Task<RepositoryStorageStatistics?> ReadMaintenanceStatisticsAsync(
            IRepositoryMaintenanceService service, Repository repository) =>
            ReadCore(service, repository);

        private static async Task<RepositoryStorageStatistics?> ReadCore(
            IRepositoryMaintenanceService service, Repository repository) =>
            await service.GetStorageStatisticsAsync(repository);

        public async Task<RepositoryMaintenanceMutationResult> RunRepositoryMaintenanceMutationAsync(
            Repository expectedRepository, Func<Task> mutation, bool includeHistory, Func<Task>? additionalRefresh)
        {
            if (GateBusy)
                return new(false, null, null, RepositoryMaintenanceNotStartedReason.GateBusy);
            if (!IsCurrentMaintenanceRepository(expectedRepository))
                return new(false, null, null, RepositoryMaintenanceNotStartedReason.StaleContext);
            ExecutedRepository = expectedRepository;
            IncludeHistory = includeHistory;
            Exception? failure = null;
            try { await mutation(); } catch (Exception exception) { failure = exception; }
            Exception? refreshFailure = null;
            if (failure is null)
            {
                try
                {
                    RefreshCount++;
                    if (FailRefresh) throw new IOException("refresh failed");
                }
                catch (Exception exception) { refreshFailure = exception; }
                if (additionalRefresh is not null)
                {
                    try { await additionalRefresh(); } catch (Exception exception) { refreshFailure ??= exception; }
                }
            }
            return new(true, failure, refreshFailure);
        }
    }
}
