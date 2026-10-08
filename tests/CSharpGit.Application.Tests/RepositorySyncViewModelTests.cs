using System.ComponentModel;
using CSharpGit.Application.Abstractions;
using CSharpGit.Application.Exceptions;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Application.Tests;

public sealed class RepositorySyncViewModelTests
{
    [Fact]
    public void RemoteStatePreservesSelectionByNameAndClearsOnRepositoryClose()
    {
        var repository = Repository("repo");
        var context = Context(repository, Branch("main", "origin/main"));
        var viewModel = CreateViewModel(new FakeSyncService(), new FakeSettingsService(), context);

        viewModel.ApplyRepositoryState([Remote("origin"), Remote("backup")]);
        viewModel.SelectedRemote = viewModel.Remotes.Single(remote => remote.Name == "backup");
        var replacement = new GitRemote("backup", "https://example.test/backup-v2", "https://example.test/backup-v2");

        viewModel.ApplyRepositoryState([Remote("origin"), replacement]);

        Assert.Same(replacement, viewModel.SelectedRemote);

        viewModel.ClearRepositoryState();

        Assert.Empty(viewModel.Remotes);
        Assert.Null(viewModel.SelectedRemote);
    }

    [Fact]
    public async Task PullUsesCommittedDefaultAndOneOffDoesNotPersistIt()
    {
        var repo = Repository("pull-preferences");
        var fake = new FakeSyncService();
        var settings = new FakeSettingsService
        {
            DefaultPullStrategy = PullStrategy.Rebase,
            ForcePullAutoStash = true
        };
        using var vm = CreateViewModel(fake, settings, Context(repo, Branch("main", "origin/main")));

        var initial = await vm.PullAsync(repo);
        Assert.Equal(PullExecutionKind.Completed, initial.Kind);
        Assert.Equal(new PullOptions(PullStrategy.Rebase, true), fake.LastPullOptions);

        var oneOff = await vm.PullAsync(repo, PullStrategy.FastForwardOnly);
        Assert.Equal(PullExecutionKind.Completed, oneOff.Kind);
        Assert.Equal(new PullOptions(PullStrategy.FastForwardOnly, true), fake.LastPullOptions);
        Assert.Equal(PullStrategy.Rebase, settings.DefaultPullStrategy);

        await vm.SetDefaultPullStrategyAsync(PullStrategy.Merge);
        await vm.SetPullAutoStashAsync(false);
        Assert.Equal(PullStrategy.Merge, vm.DefaultPullStrategy);
        Assert.False(vm.ForcePullAutoStash);
        await vm.PullAsync(repo);
        Assert.Equal(new PullOptions(PullStrategy.Merge, false), fake.LastPullOptions);
    }

    [Fact]
    public async Task PullOutcomeAndUnavailableStateAreNotReportedAsSuccess()
    {
        var repo = Repository("pull-conflict");
        var fake = new FakeSyncService
        {
            NextPullResult = new(PullOutcome.NeedsAttention, PullCompletionKind.Unknown,
                RepositoryOperation.Rebase, true, "Resolve conflicts.")
        };
        var ctx = Context(repo, Branch("main", "origin/main"));
        using var vm = CreateViewModel(fake, new FakeSettingsService(), ctx);

        var paused = await vm.PullAsync(repo);
        Assert.Equal(PullExecutionKind.NeedsAttention, paused.Kind);
        Assert.Equal(RepositoryOperation.Rebase, paused.GitResult?.ActiveOperation);

        ctx.HasUnmergedPaths = true;
        Assert.False(vm.CanPull);
        ctx.HasUnmergedPaths = false;
        ctx.CurrentOperation = RepositoryOperation.Rebase;
        Assert.False(vm.CanPull);
        var unavailable = await vm.PullAsync(repo);
        Assert.Equal(PullExecutionKind.Unavailable, unavailable.Kind);
        Assert.Single(fake.Calls);
    }

    [Fact]
    public async Task PullRejectsStaleRepositoryAndMissingUpstream()
    {
        var repo = Repository("pull-current");
        var other = Repository("pull-other");
        var fake = new FakeSyncService();
        var ctx = Context(repo, Branch("main", null));
        using var vm = CreateViewModel(fake, new FakeSettingsService(), ctx);
        Assert.False(vm.CanPull);
        Assert.Equal(PullExecutionKind.Unavailable, (await vm.PullAsync(repo)).Kind);
        ctx.Repository = other;
        Assert.Equal(PullExecutionKind.Unavailable, (await vm.PullAsync(repo)).Kind);
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task FetchFetchAllAndPullUseSingleMutationLifecycle()
    {
        var repository = Repository("repo");
        var sync = new FakeSyncService();
        var context = Context(repository, Branch("main", "origin/main"));
        var viewModel = CreateViewModel(sync, new FakeSettingsService(), context);

        await viewModel.FetchAsync(repository, "origin");
        await viewModel.FetchAllAsync(repository);
        await viewModel.PullAsync(repository);

        Assert.Equal(["fetch:origin", "fetch-all", "pull"], sync.Calls);
        Assert.Equal(3, context.MutationLifecycleCalls);
    }

    [Fact]
    public async Task TrackedPushUsesDefaultPushOptions()
    {
        var repository = Repository("repo");
        var sync = new FakeSyncService();
        var context = Context(repository, Branch("main", "origin/main"));
        var viewModel = CreateViewModel(sync, new FakeSettingsService { AutoSetupRemoteOnPush = true }, context);

        var result = await viewModel.PushAsync(repository);

        Assert.Equal(PushExecutionKind.Completed, result.Kind);
        Assert.Equal(1, sync.PushCalls);
        Assert.Null(sync.LastPushOptions);
        Assert.Equal(1, context.MutationLifecycleCalls);
    }

    [Fact]
    public async Task UntrackedPushUsesAutoSetupWhenEnabled()
    {
        var repository = Repository("repo");
        var sync = new FakeSyncService();
        var context = Context(repository, Branch("main", null));
        var viewModel = CreateViewModel(sync, new FakeSettingsService { AutoSetupRemoteOnPush = true }, context);

        var result = await viewModel.PushAsync(repository);

        Assert.Equal(PushExecutionKind.Completed, result.Kind);
        Assert.True(sync.LastPushOptions?.AutoSetupRemote == true);
        Assert.Equal(1, context.MutationLifecycleCalls);
    }

    [Fact]
    public async Task UntrackedPushRequiresExplicitTargetWhenAutoSetupIsDisabled()
    {
        var repository = Repository("repo");
        var sync = new FakeSyncService();
        var context = Context(repository, Branch("main", null));
        var viewModel = CreateViewModel(sync, new FakeSettingsService { AutoSetupRemoteOnPush = false }, context);

        var result = await viewModel.PushAsync(repository);

        Assert.Equal(PushExecutionKind.PublishTargetRequired, result.Kind);
        Assert.Equal(0, sync.PushCalls);
        Assert.Equal(0, context.MutationLifecycleCalls);
    }

    [Fact]
    public async Task AutoSetupPushMapsDestinationAndNonFastForwardToExplicitTarget()
    {
        var repository = Repository("repo");
        var sync = new FakeSyncService
        {
            PushException = new PushRejectedException(
                PushResultKind.PushDestinationUnavailable,
                "no destination")
        };
        var context = Context(repository, Branch("main", null));
        var viewModel = CreateViewModel(sync, new FakeSettingsService { AutoSetupRemoteOnPush = true }, context);

        var destination = await viewModel.PushAsync(repository);

        Assert.Equal(PushExecutionKind.PublishTargetRequired, destination.Kind);

        sync.PushException = new PushRejectedException(
            PushResultKind.NonFastForwardRejected,
            "rejected");
        var nonFastForward = await viewModel.PushAsync(repository);

        Assert.Equal(PushExecutionKind.PublishTargetRequired, nonFastForward.Kind);
    }

    [Fact]
    public async Task TrackedNonFastForwardRemainsDistinct()
    {
        var repository = Repository("repo");
        var sync = new FakeSyncService
        {
            PushException = new PushRejectedException(
                PushResultKind.NonFastForwardRejected,
                "rejected")
        };
        var context = Context(repository, Branch("main", "origin/main"));
        var viewModel = CreateViewModel(sync, new FakeSettingsService(), context);

        var result = await viewModel.PushAsync(repository);

        Assert.Equal(PushExecutionKind.NonFastForwardRejected, result.Kind);
    }

    [Fact]
    public async Task PublishPreparationAndExecutionBelongToRepositorySync()
    {
        var repository = Repository("repo");
        var sync = new FakeSyncService
        {
            PublishPreparation = new PublishBranchPreparation("main", "origin")
        };
        var context = Context(repository, Branch("main", null));
        var viewModel = CreateViewModel(sync, new FakeSettingsService(), context);

        var preparation = await viewModel.PreparePublishBranchAsync(repository);
        var execution = await viewModel.PublishBranchAsync(
            repository,
            "origin",
            "feature/main",
            setUpstream: true);

        Assert.True(preparation.Succeeded);
        Assert.Equal("main", preparation.Preparation?.LocalBranch);
        Assert.Equal("origin", preparation.Preparation?.SuggestedRemote);
        Assert.Equal(PushExecutionKind.Completed, execution.Kind);
        Assert.Equal(["prepare-publish", "publish:origin:feature/main:True"], sync.Calls);
        Assert.Equal(1, context.MutationLifecycleCalls);
    }

    [Fact]
    public async Task ForcePreparationClassifiesMissingUpstreamAndExplicitTarget()
    {
        var repository = Repository("repo");
        var sync = new FakeSyncService
        {
            ForcePreparationException = new ForcePushWithLeasePreparationException(
                ForcePushPreparationFailure.MissingUpstream,
                "missing upstream")
        };
        var context = Context(repository, Branch("main", null));
        var viewModel = CreateViewModel(sync, new FakeSettingsService(), context);

        var missing = await viewModel.PrepareForcePushWithLeaseAsync(repository);

        Assert.Equal(ForcePushPreparationKind.ExplicitTargetRequired, missing.Kind);

        var snapshot = Snapshot();
        sync.ForcePreparationException = null;
        sync.ForceSnapshot = snapshot;
        var explicitTarget = await viewModel.PrepareForcePushWithLeaseAsync(
            repository,
            "origin",
            "main");

        Assert.Equal(ForcePushPreparationKind.Ready, explicitTarget.Kind);
        Assert.Same(snapshot, explicitTarget.Snapshot);
        Assert.Equal("prepare-force:origin:main", sync.Calls[^1]);
    }

    [Fact]
    public async Task ForceExecutionUsesExactPreparedSnapshotAndClassifiesLeaseRejection()
    {
        var repository = Repository("repo");
        var snapshot = Snapshot();
        var sync = new FakeSyncService { ForceSnapshot = snapshot };
        var context = Context(repository, Branch("main", "origin/main"));
        var viewModel = CreateViewModel(sync, new FakeSettingsService(), context);

        var preparation = await viewModel.PrepareForcePushWithLeaseAsync(repository);
        Assert.Same(snapshot, preparation.Snapshot);

        var completed = await viewModel.ForcePushWithLeaseAsync(repository, preparation.Snapshot!);

        Assert.Equal(ForcePushExecutionKind.Completed, completed.Kind);
        Assert.Same(snapshot, sync.LastForceSnapshot);

        sync.ForceExecutionException = new PushRejectedException(
            PushResultKind.LeaseRejected,
            "lease changed");
        var rejected = await viewModel.ForcePushWithLeaseAsync(repository, snapshot);

        Assert.Equal(ForcePushExecutionKind.LeaseRejected, rejected.Kind);
    }

    [Fact]
    public async Task ForceCancellationIsPresentationFriendly()
    {
        var repository = Repository("repo");
        var sync = new FakeSyncService
        {
            ForceExecutionException = new ForcePushWithLeaseCancelledException("cancelled")
        };
        var context = Context(repository, Branch("main", "origin/main"));
        var viewModel = CreateViewModel(sync, new FakeSettingsService(), context);

        var result = await viewModel.ForcePushWithLeaseAsync(repository, Snapshot());

        Assert.Equal(ForcePushExecutionKind.Cancelled, result.Kind);
        Assert.Equal("cancelled", result.Message);
    }

    [Fact]
    public async Task RepositorySwitchBlocksPreparedForceSnapshotExecution()
    {
        var first = Repository("first");
        var second = Repository("second");
        var snapshot = Snapshot();
        var sync = new FakeSyncService { ForceSnapshot = snapshot };
        var context = Context(first, Branch("main", "origin/main"));
        var viewModel = CreateViewModel(sync, new FakeSettingsService(), context);

        var preparation = await viewModel.PrepareForcePushWithLeaseAsync(first);
        Assert.Same(snapshot, preparation.Snapshot);

        context.Repository = second;
        var result = await viewModel.ForcePushWithLeaseAsync(first, snapshot);

        Assert.Equal(ForcePushExecutionKind.StaleRepository, result.Kind);
        Assert.Equal(0, sync.ForceExecutionCalls);
    }

    private static RepositorySyncViewModel CreateViewModel(
        FakeSyncService sync,
        FakeSettingsService settings,
        FakeSyncContext context)
    {
        var viewModel = new RepositorySyncViewModel(sync, settings);
        viewModel.Attach(context);
        return viewModel;
    }

    private static FakeSyncContext Context(Repository repository, GitBranch branch) =>
        new()
        {
            Repository = repository,
            CurrentLocalBranch = branch,
            CanRunSyncMutation = true
        };

    private static Repository Repository(string name) =>
        new($"/work/{name}", $"/work/{name}", $"/work/{name}/.git", false);

    private static GitRemote Remote(string name) =>
        new(name, $"https://example.test/{name}", $"https://example.test/{name}");

    private static GitBranch Branch(string name, string? upstream) =>
        new(name, "local", true, upstream);

    private static ForcePushWithLeaseSnapshot Snapshot() =>
        new(
            "main",
            "local-commit",
            "origin",
            "origin",
            "main",
            "remote-commit");

    private sealed class FakeSyncContext : IRepositorySyncContext
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

        public bool IsBusy { get; set; }
        public bool CanRunSyncMutation { get; set; } = true;
        public RepositoryOperation CurrentOperation { get; set; }
        public bool HasUnmergedPaths { get; set; }
        public GitBranch? CurrentLocalBranch { get; set; }
        public int MutationLifecycleCalls { get; private set; }

        public async Task<bool> RunSyncMutationAsync(
            Repository expectedRepository,
            Func<Task> mutation,
            string? errorContext = null)
        {
            if (!ReferenceEquals(Repository, expectedRepository))
                return false;

            MutationLifecycleCalls++;
            try
            {
                await mutation();
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

    private sealed class FakeSyncService : IRepositorySyncService
    {
        public List<string> Calls { get; } = [];
        public Exception? PushException { get; set; }
        public Exception? PublishException { get; set; }
        public Exception? ForcePreparationException { get; set; }
        public Exception? ForceExecutionException { get; set; }
        public PushOptions? LastPushOptions { get; private set; }
        public PullOptions? LastPullOptions { get; private set; }
        public PullResult NextPullResult { get; set; } = new(PullOutcome.Completed, PullCompletionKind.UpToDate, RepositoryOperation.None, false, "Up to date.");
        public int PushCalls { get; private set; }
        public int ForceExecutionCalls { get; private set; }
        public ForcePushWithLeaseSnapshot? LastForceSnapshot { get; private set; }
        public PublishBranchPreparation PublishPreparation { get; set; } = new("main", "origin");
        public ForcePushWithLeaseSnapshot ForceSnapshot { get; set; } = Snapshot();

        public Task FetchAsync(
            Repository repository,
            string remote,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"fetch:{remote}");
            return Task.CompletedTask;
        }

        public Task FetchAllAsync(
            Repository repository,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("fetch-all");
            return Task.CompletedTask;
        }

        public Task DeleteRemoteBranchAsync(
            Repository repository,
            string remote,
            string branch,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<PullResult> PullAsync(
            Repository repository,
            PullOptions options,
            CancellationToken cancellationToken = default)
        {
            LastPullOptions = options;
            Calls.Add("pull");
            return Task.FromResult(NextPullResult);
        }

        public Task PushAsync(
            Repository repository,
            PushOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            PushCalls++;
            LastPushOptions = options;
            Calls.Add($"push:{options?.AutoSetupRemote == true}");
            return PushException is null
                ? Task.CompletedTask
                : Task.FromException(PushException);
        }

        public Task<PublishBranchPreparation> PreparePublishBranchAsync(
            Repository repository,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("prepare-publish");
            return Task.FromResult(PublishPreparation);
        }

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
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"prepare-force:{remote ?? "<auto>"}:{remoteBranch ?? "<auto>"}");
            return ForcePreparationException is null
                ? Task.FromResult(ForceSnapshot)
                : Task.FromException<ForcePushWithLeaseSnapshot>(ForcePreparationException);
        }

        public Task ForcePushWithLeaseAsync(
            Repository repository,
            ForcePushWithLeaseSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            ForceExecutionCalls++;
            LastForceSnapshot = snapshot;
            Calls.Add("force");
            return ForceExecutionException is null
                ? Task.CompletedTask
                : Task.FromException(ForceExecutionException);
        }
    }

    private sealed class FakeSettingsService : IAppSettingsService
    {
        public ApplicationThemeMode ThemeMode => ApplicationThemeMode.System;
        public CommitTimeDisplayMode CommitTimeDisplayMode => CommitTimeDisplayMode.Smart;
        public bool LoggingEnabled => false;
        public ApplicationLogLevel LogLevel => ApplicationLogLevel.Information;
        public GitConsoleAutoOpenMode GitConsoleAutoOpenMode => GitConsoleAutoOpenMode.OnErrors;
        public bool ShowReflog => false;
        public bool AutoSetupRemoteOnPush { get; set; }
        public PullStrategy DefaultPullStrategy { get; set; } = PullStrategy.GitConfiguration;
        public bool ForcePullAutoStash { get; set; }
        public bool ShowAuthorAvatars => true;
        public bool OnlineAvatarLookupEnabled => true;
        public bool HistoryPerformanceDiagnosticsEnabled => false;
        public HistoryRenderingMode HistoryRenderingMode => HistoryRenderingMode.Full;
        public string DefaultRepositoriesDirectory => "/work";
        public IReadOnlyList<RecentRepositorySettings> RecentRepositories => [];

        public event EventHandler? Changed;
        public void PublishChange() => Changed?.Invoke(this, EventArgs.Empty);

        public Task SetThemeModeAsync(ApplicationThemeMode mode, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetCommitTimeDisplayModeAsync(CommitTimeDisplayMode mode, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetLoggingSettingsAsync(bool enabled, ApplicationLogLevel level, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetGitConsoleAutoOpenModeAsync(GitConsoleAutoOpenMode mode, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetShowReflogAsync(bool value, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetAutoSetupRemoteOnPushAsync(bool value, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetDefaultPullStrategyAsync(PullStrategy strategy, CancellationToken cancellationToken = default)
        {
            DefaultPullStrategy = strategy;
            PublishChange();
            return Task.CompletedTask;
        }
        public Task SetForcePullAutoStashAsync(bool value, CancellationToken cancellationToken = default)
        {
            ForcePullAutoStash = value;
            PublishChange();
            return Task.CompletedTask;
        }
        public Task SetShowAuthorAvatarsAsync(bool value, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetOnlineAvatarLookupEnabledAsync(bool value, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetHistoryPerformanceDiagnosticsEnabledAsync(bool value, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetHistoryRenderingModeAsync(HistoryRenderingMode mode, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetDefaultRepositoriesDirectoryAsync(string directory, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RecordRecentRepositoryAsync(string path, string displayName, string? lastBranchName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateRecentRepositoryBranchAsync(string path, string? lastBranchName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetRecentRepositoryPinnedAsync(string path, bool pinned, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MovePinnedRepositoryAsync(string path, int newIndex, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveRecentRepositoryAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
