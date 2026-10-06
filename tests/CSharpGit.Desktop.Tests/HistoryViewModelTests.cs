using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.Threading;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Desktop.Tests;

public sealed class HistoryViewModelTests
{
    [Fact]
    public async Task InitialRefreshBuildsAllReferencesQueryAndSelectsFirstRow()
    {
        var fixture = new Fixture();
        fixture.Service.Pages.Enqueue(Page(true, "a", "b"));

        await fixture.ViewModel.RefreshAsync();

        Assert.Equal(new[] { "a", "b" }, Hashes(fixture.ViewModel.Rows));
        Assert.Equal("a", fixture.ViewModel.SelectedRow?.Commit.Hash);
        Assert.True(fixture.ViewModel.HasMore);
        var query = Assert.Single(fixture.Service.Queries).Query;
        Assert.Equal(HistoryScope.AllReferences, query.Scope);
        Assert.Null(query.Reference);
        Assert.Equal(fixture.Context.CurrentBranchName, query.HeadReference);
        Assert.Equal(fixture.Context.CurrentHeadCommit, query.HeadCommit);
        Assert.Equal(fixture.Context.LocalBranches.Count, query.RepositoryReferences?.LocalBranches.Count);
    }

    [Fact]
    public async Task CurrentBranchModeUsesOneReloadAndDisablesReflog()
    {
        var fixture = new Fixture(showReflog: true);
        fixture.Service.Pages.Enqueue(Page(false, "branch"));

        await fixture.ViewModel.SetHistoryDisplayModeAsync(HistoryDisplayMode.CurrentBranch);

        var query = Assert.Single(fixture.Service.Queries).Query;
        Assert.Equal(HistoryScope.CurrentBranch, query.Scope);
        Assert.False(query.IncludeReflog);
        Assert.False(fixture.ViewModel.ShowReflog);
        Assert.False(fixture.Settings.ShowReflog);
        Assert.Equal(1, fixture.Settings.ShowReflogWrites);
    }

    [Fact]
    public async Task FilterIsSharedByReferenceHistory()
    {
        var fixture = new Fixture();
        fixture.ViewModel.FilterText = "needle";
        fixture.Service.Pages.Enqueue(Page(false, "ref"));

        await fixture.ViewModel.ShowReferenceAsync("feature/test", "Branch: feature/test");

        var query = Assert.Single(fixture.Service.Queries).Query;
        Assert.Equal("needle", query.Filter);
        Assert.Equal("feature/test", query.Reference);
        Assert.Equal(HistoryScope.CurrentBranch, query.Scope);
        Assert.True(fixture.ViewModel.IsReferenceScoped);
        Assert.Equal("Branch: feature/test", fixture.ViewModel.ActiveReferenceLabel);
    }

    [Fact]
    public async Task ReferenceAndAllHistoryReuseTheSameRowsCollection()
    {
        var fixture = new Fixture();
        var rows = fixture.ViewModel.Rows;
        fixture.Service.Pages.Enqueue(Page(false, "ref"));
        fixture.Service.Pages.Enqueue(Page(false, "all"));

        await fixture.ViewModel.ShowReferenceAsync("feature/test", "Branch: feature/test");
        await fixture.ViewModel.ShowAllAsync();

        Assert.Same(rows, fixture.ViewModel.Rows);
        Assert.Equal(new[] { "all" }, Hashes(rows));
        Assert.False(fixture.ViewModel.IsReferenceScoped);
        Assert.Null(fixture.ViewModel.ActiveReference);
    }

    [Fact]
    public async Task ShowReferenceDisablesAndPersistsReflogBeforeLoading()
    {
        var fixture = new Fixture(showReflog: true);
        fixture.Service.Pages.Enqueue(Page(false, "ref"));

        await fixture.ViewModel.ShowReferenceAsync("origin/main", "Remote: origin/main");

        Assert.False(fixture.ViewModel.ShowReflog);
        Assert.False(fixture.Settings.ShowReflog);
        Assert.Equal(1, fixture.Settings.ShowReflogWrites);
        Assert.False(Assert.Single(fixture.Service.Queries).Query.IncludeReflog);
    }

    [Fact]
    public async Task LoadMoreUsesCurrentQuerySkipAndAppends()
    {
        var fixture = new Fixture();
        fixture.Service.Pages.Enqueue(Page(true, "a", "b"));
        fixture.Service.Pages.Enqueue(Page(false, "c"));

        await fixture.ViewModel.RefreshAsync();
        await fixture.ViewModel.LoadMoreAsync();

        Assert.Equal(new[] { 0, 2 }, fixture.Service.Queries.Select(call => call.Query.Skip));
        Assert.Equal(new[] { "a", "b", "c" }, Hashes(fixture.ViewModel.Rows));
        Assert.False(fixture.ViewModel.HasMore);

        await fixture.ViewModel.LoadMoreAsync();
        Assert.Equal(2, fixture.Service.Queries.Count);
    }

    [Fact]
    public async Task RefreshRestoresSelectionByCommitHash()
    {
        var fixture = new Fixture();
        fixture.Service.Pages.Enqueue(Page(false, "a", "b"));
        fixture.Service.Pages.Enqueue(Page(false, "x", "b", "c"));

        await fixture.ViewModel.RefreshAsync();
        fixture.ViewModel.SelectedRow = fixture.ViewModel.Rows[1];
        var oldObject = fixture.ViewModel.SelectedRow;

        await fixture.ViewModel.RefreshAsync();

        Assert.Equal("b", fixture.ViewModel.SelectedRow?.Commit.Hash);
        Assert.NotSame(oldObject, fixture.ViewModel.SelectedRow);
        Assert.Contains(fixture.ViewModel.SelectedRow, fixture.ViewModel.Rows);
    }

    [Fact]
    public async Task RefreshFallsBackToFirstRowWhenSelectedHashIsAbsent()
    {
        var fixture = new Fixture();
        fixture.Service.Pages.Enqueue(Page(false, "a", "b"));
        fixture.Service.Pages.Enqueue(Page(false, "x", "y"));

        await fixture.ViewModel.RefreshAsync();
        fixture.ViewModel.SelectedRow = fixture.ViewModel.Rows[1];

        await fixture.ViewModel.RefreshAsync();

        Assert.Equal("x", fixture.ViewModel.SelectedRow?.Commit.Hash);
    }

    [Fact]
    public async Task NavigateToCommitResetsScopedModeScopeAndFilter()
    {
        var fixture = new Fixture();
        fixture.Service.Pages.Enqueue(Page(false, "ref"));
        await fixture.ViewModel.ShowReferenceAsync("feature/test", "Branch: feature/test");
        fixture.ViewModel.FilterText = "filter";
        fixture.Service.ThroughHandler = (_, query, hash, _, _) =>
        {
            Assert.Equal("target", hash);
            Assert.Equal(HistoryScope.AllReferences, query.Scope);
            Assert.Null(query.Filter);
            Assert.Null(query.Reference);
            return Task.FromResult(Page(false, "head", "target", "tail"));
        };

        var target = await fixture.ViewModel.NavigateToCommitAsync("target");

        Assert.Equal("target", target?.Commit.Hash);
        Assert.Same(target, fixture.ViewModel.SelectedRow);
        Assert.False(fixture.ViewModel.IsReferenceScoped);
        Assert.Equal(fixture.ViewModel.Scopes[0], fixture.ViewModel.SelectedScope);
        Assert.Equal(string.Empty, fixture.ViewModel.FilterText);
    }

    [Fact]
    public async Task NavigateToUnknownCommitReportsError()
    {
        var fixture = new Fixture();
        fixture.Service.ThroughHandler = (_, _, _, _, _) =>
            Task.FromResult(Page(false, "other"));

        var target = await fixture.ViewModel.NavigateToCommitAsync("missing");

        Assert.Null(target);
        Assert.Contains("Could not navigate to commit", fixture.Context.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaterNormalRequestWinsWhenEarlierCompletesLast()
    {
        var fixture = new Fixture();
        var first = NewCompletion();
        var second = NewCompletion();
        var calls = 0;
        fixture.Service.ReadHandler = (_, _, _) =>
            ++calls == 1 ? first.Task : second.Task;

        var loadA = fixture.ViewModel.RefreshAsync();
        await WaitForAsync(() => calls == 1);
        var loadB = fixture.ViewModel.RefreshAsync();
        await WaitForAsync(() => calls == 2);

        second.SetResult(Page(false, "b"));
        await loadB;
        first.SetResult(Page(false, "a"));
        await loadA;

        Assert.Equal(new[] { "b" }, Hashes(fixture.ViewModel.Rows));
    }

    [Fact]
    public async Task LaterReferenceRequestWinsWhenEarlierCompletesLast()
    {
        var fixture = new Fixture();
        var first = NewCompletion();
        var second = NewCompletion();
        var calls = 0;
        fixture.Service.ReadHandler = (_, _, _) =>
            ++calls == 1 ? first.Task : second.Task;

        var loadA = fixture.ViewModel.ShowReferenceAsync("branch-a", "Branch: branch-a");
        await WaitForAsync(() => calls == 1);
        var loadB = fixture.ViewModel.ShowReferenceAsync("branch-b", "Branch: branch-b");
        await WaitForAsync(() => calls == 2);

        second.SetResult(Page(false, "b"));
        await loadB;
        first.SetResult(Page(false, "a"));
        await loadA;

        Assert.Equal("branch-b", fixture.ViewModel.ActiveReference);
        Assert.Equal("Branch: branch-b", fixture.ViewModel.ActiveReferenceLabel);
        Assert.Equal(new[] { "b" }, Hashes(fixture.ViewModel.Rows));
    }

    [Fact]
    public async Task RepositorySwitchRejectsOldCompletion()
    {
        var fixture = new Fixture();
        var oldRepository = fixture.Context.Repository!;
        var newRepository = new Repository("/next", "/next", "/next/.git", false);
        var first = NewCompletion();
        var second = NewCompletion();
        fixture.Service.ReadHandler = (repository, _, _) =>
            ReferenceEquals(repository, oldRepository) ? first.Task : second.Task;

        var oldLoad = fixture.ViewModel.RefreshAsync();
        await WaitForAsync(() => fixture.Service.Queries.Count == 1);

        fixture.Context.Repository = newRepository;
        fixture.ViewModel.OnRepositoryChanged(newRepository);
        var newLoad = fixture.ViewModel.RefreshAsync();
        await WaitForAsync(() => fixture.Service.Queries.Count == 2);

        second.SetResult(Page(false, "new"));
        await newLoad;
        first.SetResult(Page(false, "old"));
        await oldLoad;

        Assert.Equal(new[] { "new" }, Hashes(fixture.ViewModel.Rows));
        Assert.Equal("new", fixture.ViewModel.SelectedRow?.Commit.Hash);
    }

    [Fact]
    public async Task FilterChangeCancelsVisibleLoadingStateWithoutPublishingOldResult()
    {
        var fixture = new Fixture();
        var pending = NewCompletion();
        fixture.Service.ReadHandler = (_, _, _) => pending.Task;

        var load = fixture.ViewModel.RefreshAsync();
        await WaitForAsync(() => fixture.ViewModel.IsLoading);

        fixture.ViewModel.FilterText = "new filter";

        Assert.False(fixture.ViewModel.IsLoading);
        pending.SetResult(Page(false, "stale"));
        await load;
        Assert.Empty(fixture.ViewModel.Rows);
    }

    [Fact]
    public async Task ExternalShowReflogSettingIsReflectedAndReloaded()
    {
        var fixture = new Fixture();
        fixture.Service.Pages.Enqueue(Page(false, "normal"));
        fixture.Settings.PublishShowReflog(true);

        await WaitForAsync(() => fixture.Service.Queries.Count == 1);

        Assert.True(fixture.ViewModel.ShowReflog);
        Assert.True(fixture.Service.Queries[0].Query.IncludeReflog);
        Assert.Equal(HistoryScope.AllReferences, fixture.Service.Queries[0].Query.Scope);
    }

    [Fact]
    public async Task ExternalReflogEnableWhileScopedIsRejectedByScopedRule()
    {
        var fixture = new Fixture();
        fixture.Service.Pages.Enqueue(Page(false, "ref"));
        await fixture.ViewModel.ShowReferenceAsync("feature/test", "Branch: feature/test");

        fixture.Settings.PublishShowReflog(true);
        await WaitForAsync(() => fixture.Settings.ShowReflogWrites == 1);

        Assert.False(fixture.ViewModel.ShowReflog);
        Assert.False(fixture.Settings.ShowReflog);
        Assert.Single(fixture.Service.Queries);
    }

    private static TaskCompletionSource<HistoryPage> NewCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static HistoryPage Page(bool hasMore, params string[] hashes) =>
        new(hashes.Select(Row).ToArray(), hasMore);

    private static HistoryRow Row(string hash) =>
        new(
            new CommitHistoryItem(
                hash,
                [],
                $"subject {hash}",
                $"message {hash}",
                "Author",
                DateTimeOffset.UnixEpoch,
                []),
            new CommitTopology(0, []));

    private static string[] Hashes(IEnumerable<HistoryRow> rows) =>
        rows.Select(row => row.Commit.Hash).ToArray();

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        for (var attempt = 0; attempt < 200 && !predicate(); attempt++)
            await Task.Delay(5);

        Assert.True(predicate(), "Timed out waiting for the expected async history state.");
    }

    private sealed class Fixture
    {
        public Fixture(bool showReflog = false)
        {
            Service = new FakeHistoryService();
            Settings = new FakeSettingsService(showReflog);
            Context = new FakeContext
            {
                Repository = new Repository("/repo", "/repo", "/repo/.git", false),
                HeadExists = true,
                CurrentBranchName = "main",
                CurrentHeadCommit = "head",
                LocalBranches = [new GitBranch("main", "head", IsCurrent: true)],
                RemoteBranches = [new GitBranch("origin/main", "head")],
                Remotes = [new GitRemote("origin", "https://example/fetch", "https://example/push")],
                Tags = [new GitTag("v1", "head")]
            };
            ViewModel = new HistoryViewModel(Service, Settings, new ImmediateDispatcher());
            ViewModel.Attach(Context);
            ViewModel.OnRepositoryChanged(Context.Repository);
        }

        public FakeHistoryService Service { get; }
        public FakeSettingsService Settings { get; }
        public FakeContext Context { get; }
        public HistoryViewModel ViewModel { get; }
    }

    private sealed class FakeContext : IHistoryRepositoryContext
    {
        public Repository? Repository { get; set; }
        public bool? HeadExists { get; set; }
        public string? CurrentBranchName { get; set; }
        public string? CurrentHeadCommit { get; set; }
        public bool IsDetachedHead { get; set; }
        public IReadOnlyList<GitBranch> LocalBranches { get; set; } = [];
        public IReadOnlyList<GitBranch> RemoteBranches { get; set; } = [];
        public IReadOnlyList<GitRemote> Remotes { get; set; } = [];
        public IReadOnlyList<GitTag> Tags { get; set; } = [];
        public bool ShouldAutoSelectHistoryRow { get; set; } = true;
        public int BusyDepth { get; private set; }
        public string? ErrorMessage { get; private set; }

        public void EnterHistoryBusy() => BusyDepth++;
        public void ExitHistoryBusy() => BusyDepth--;
        public void ReportHistoryError(string message) => ErrorMessage = message;
    }

    private sealed class FakeHistoryService : IHistoryService
    {
        public Queue<HistoryPage> Pages { get; } = new();
        public List<HistoryCall> Queries { get; } = [];
        public Func<Repository, HistoryQuery, CancellationToken, Task<HistoryPage>>? ReadHandler { get; set; }
        public Func<Repository, HistoryQuery, string, int, CancellationToken, Task<HistoryPage>>? ThroughHandler { get; set; }

        public Task<HistoryPage> ReadHistoryAsync(
            Repository repository,
            HistoryQuery query,
            CancellationToken cancellationToken = default)
        {
            Queries.Add(new HistoryCall(repository, query));
            return ReadHandler?.Invoke(repository, query, cancellationToken)
                ?? Task.FromResult(Pages.Count == 0 ? Page(false) : Pages.Dequeue());
        }

        public Task<HistoryPage> ReadHistoryThroughCommitAsync(
            Repository repository,
            HistoryScope scope,
            string targetHash,
            int trailingCount = 100,
            CancellationToken cancellationToken = default) =>
            ReadHistoryThroughCommitAsync(
                repository,
                new HistoryQuery(scope, null, 0),
                targetHash,
                trailingCount,
                cancellationToken);

        public Task<HistoryPage> ReadHistoryThroughCommitAsync(
            Repository repository,
            HistoryQuery query,
            string targetHash,
            int trailingCount = 100,
            CancellationToken cancellationToken = default)
        {
            Queries.Add(new HistoryCall(repository, query));
            return ThroughHandler?.Invoke(repository, query, targetHash, trailingCount, cancellationToken)
                ?? Task.FromResult(Pages.Count == 0 ? Page(false) : Pages.Dequeue());
        }

        public Task<CommitDetails> ReadCommitAsync(
            Repository repository,
            string hash,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CommitDetails(Row(hash).Commit, []));

        public Task<IReadOnlyList<ChangedFile>> ReadChangedFilesAsync(
            Repository repository,
            string commitHash,
            string? parentHash,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ChangedFile>>([]);

        public Task<FileDiff> ReadDiffAsync(
            Repository repository,
            string hash,
            string path,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new FileDiff(path, false, []));

        public Task<FileDiff> ReadDiffAsync(
            Repository repository,
            string commitHash,
            string? parentHash,
            ChangedFile file,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new FileDiff(file.Path, file.IsBinary, []));

        public Task<FileDiff> ReadDiffAsync(
            Repository repository,
            string commitHash,
            string? parentHash,
            ChangedFile file,
            DiffLoadMode mode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new FileDiff(file.Path, file.IsBinary, []));
    }

    private sealed record HistoryCall(Repository Repository, HistoryQuery Query);

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public bool HasThreadAccess => true;

        public bool TryEnqueue(Action action)
        {
            action();
            return true;
        }
    }

    private sealed class FakeSettingsService(bool showReflog) : IAppSettingsService
    {
        public ApplicationThemeMode ThemeMode => default;
        public CommitTimeDisplayMode CommitTimeDisplayMode => default;
        public bool LoggingEnabled => false;
        public ApplicationLogLevel LogLevel => default;
        public GitConsoleAutoOpenMode GitConsoleAutoOpenMode => default;
        public bool ShowReflog { get; private set; } = showReflog;
        public bool AutoSetupRemoteOnPush => false;
        public bool ShowAuthorAvatars => false;
        public bool OnlineAvatarLookupEnabled => false;
        public bool HistoryPerformanceDiagnosticsEnabled => false;
        public HistoryRenderingMode HistoryRenderingMode => default;
        public string DefaultRepositoriesDirectory => string.Empty;
        public IReadOnlyList<RecentRepositorySettings> RecentRepositories => [];
        public int ShowReflogWrites { get; private set; }

        public event EventHandler? Changed;

        public Task SetThemeModeAsync(ApplicationThemeMode mode, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetCommitTimeDisplayModeAsync(CommitTimeDisplayMode mode, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetLoggingSettingsAsync(bool enabled, ApplicationLogLevel level, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetGitConsoleAutoOpenModeAsync(GitConsoleAutoOpenMode mode, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SetShowReflogAsync(bool value, CancellationToken cancellationToken = default)
        {
            ShowReflogWrites++;
            if (ShowReflog == value) return Task.CompletedTask;
            ShowReflog = value;
            Changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public void PublishShowReflog(bool value)
        {
            ShowReflog = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public Task SetAutoSetupRemoteOnPushAsync(bool value, CancellationToken cancellationToken = default) => Task.CompletedTask;
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
