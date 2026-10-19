using System.Windows.Input;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.Threading;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Desktop.Tests;

public sealed class HistoryViewModelTests
{
    [Fact]
    public async Task RefreshUsesNormalQueryAndRestoresSelectionByHash()
    {
        using var fixture = new Fixture();
        var first = Row("a");
        var selected = Row("b");
        fixture.Service.Enqueue(new HistoryPage([first, selected], HasMore: false));

        await fixture.ViewModel.RefreshAsync();
        fixture.ViewModel.SelectedRow = selected;

        var restored = Row("b");
        fixture.Service.Enqueue(new HistoryPage([Row("c"), restored], HasMore: false));
        await fixture.ViewModel.RefreshAsync();

        Assert.Equal(2, fixture.Service.Reads.Count);
        var query = fixture.Service.Reads[^1].Query;
        Assert.Equal(HistoryScope.AllReferences, query.Scope);
        Assert.Null(query.Reference);
        Assert.Same(restored, fixture.ViewModel.SelectedRow);
    }

    [Fact]
    public async Task PullNavigationSelectsHeadWithinCurrentBranchWithoutChangingScope()
    {
        using var fixture = new Fixture();
        fixture.Service.Enqueue(new HistoryPage([Row("new-head"), Row("old-head")], HasMore: false));
        fixture.ViewModel.SelectedScope = fixture.ViewModel.Scopes[1];
        // Changing scope already initiates the history read.
        Assert.Equal(2, fixture.ViewModel.Rows.Count);
        fixture.ViewModel.SelectedRow = fixture.ViewModel.Rows[1];
        var selectionVersion = fixture.ViewModel.SelectionIntentVersion;

        var target = await fixture.ViewModel.SelectCommitInCurrentHistoryAsync("new-head", selectionVersion);

        Assert.Same(fixture.ViewModel.Rows[0], target);
        Assert.Same(target, fixture.ViewModel.SelectedRow);
        Assert.Equal(HistoryScope.CurrentBranch, fixture.ViewModel.SelectedScope.Value);
        Assert.Empty(fixture.Service.ThroughReads);
    }

    [Fact]
    public async Task PullNavigationFindsHeadOutsideInitialAllReferencesPage()
    {
        using var fixture = new Fixture();
        fixture.Service.Enqueue(new HistoryPage([Row("other")], HasMore: true));
        await fixture.ViewModel.RefreshAsync();
        var version = fixture.ViewModel.SelectionIntentVersion;
        var head = Row("new-head");
        fixture.Service.EnqueueThrough(new HistoryPage([Row("other"), head], HasMore: true));

        var target = await fixture.ViewModel.SelectCommitInCurrentHistoryAsync("new-head", version);

        Assert.Same(head, target);
        Assert.Same(target, fixture.ViewModel.SelectedRow);
        Assert.Equal(HistoryScope.AllReferences, fixture.ViewModel.SelectedScope.Value);
        Assert.Equal("new-head", Assert.Single(fixture.Service.ThroughReads).Hash);
    }

    [Fact]
    public async Task PullNavigationDoesNotOverrideNewSelectionOrFilteredHistory()
    {
        using var fixture = new Fixture();
        fixture.Service.Enqueue(new HistoryPage([Row("new-head"), Row("old-head")], HasMore: false));
        await fixture.ViewModel.RefreshAsync();
        var version = fixture.ViewModel.SelectionIntentVersion;
        fixture.ViewModel.SelectedRow = fixture.ViewModel.Rows[1];

        Assert.Null(await fixture.ViewModel.SelectCommitInCurrentHistoryAsync("new-head", version));
        Assert.Same(fixture.ViewModel.Rows[1], fixture.ViewModel.SelectedRow);

        fixture.ViewModel.FilterText = "needle";
        var filteredVersion = fixture.ViewModel.SelectionIntentVersion;
        Assert.Null(await fixture.ViewModel.SelectCommitInCurrentHistoryAsync("new-head", filteredVersion));
        Assert.Empty(fixture.Service.ThroughReads);
    }

    [Fact]
    public async Task RefreshRestorationDoesNotCountAsUserSelectionIntent()
    {
        using var fixture = new Fixture();
        fixture.Service.Enqueue(new HistoryPage([Row("old"), Row("selected")], HasMore: false));
        await fixture.ViewModel.RefreshAsync();
        fixture.ViewModel.SelectedRow = fixture.ViewModel.Rows[1];
        var version = fixture.ViewModel.SelectionIntentVersion;

        fixture.Service.Enqueue(new HistoryPage([Row("new"), Row("selected")], HasMore: false));
        await fixture.ViewModel.RefreshAsync();

        Assert.Equal(version, fixture.ViewModel.SelectionIntentVersion);
        Assert.Equal("selected", fixture.ViewModel.SelectedRow?.Commit.Hash);
    }

    [Fact]
    public async Task PullNavigationDoesNotOverrideSelectionChangedDuringHistoryLookup()
    {
        using var fixture = new Fixture(controlled: true);
        var refresh = fixture.ViewModel.RefreshAsync();
        fixture.Service.CompleteRead(0, new HistoryPage([Row("other"), Row("user-target")], HasMore: true));
        await refresh;
        var version = fixture.ViewModel.SelectionIntentVersion;

        var navigation = fixture.ViewModel.SelectCommitInCurrentHistoryAsync("new-head", version);
        Assert.Single(fixture.Service.ThroughReads);
        fixture.ViewModel.SelectedRow = fixture.ViewModel.Rows[1];
        fixture.Service.CompleteThroughRead(0, new HistoryPage([Row("new-head")], HasMore: false));

        Assert.Null(await navigation);
        Assert.Equal(["other", "user-target"], fixture.ViewModel.Rows.Select(row => row.Commit.Hash));
        Assert.Equal("user-target", fixture.ViewModel.SelectedRow?.Commit.Hash);
    }

    [Fact]
    public async Task ReferenceScopeAndShowAllReuseOneRowsCollection()
    {
        using var fixture = new Fixture();
        var rows = fixture.ViewModel.Rows;

        fixture.Service.Enqueue(new HistoryPage([Row("branch")], HasMore: false));
        await fixture.ViewModel.ShowReferenceAsync("feature/test", "Branch: feature/test");

        Assert.Same(rows, fixture.ViewModel.Rows);
        Assert.True(fixture.ViewModel.IsReferenceScoped);
        Assert.Equal("feature/test", fixture.Service.Reads[^1].Query.Reference);
        Assert.Equal(HistoryScope.CurrentBranch, fixture.Service.Reads[^1].Query.Scope);

        fixture.Service.Enqueue(new HistoryPage([Row("all")], HasMore: false));
        var callsBefore = fixture.Service.Reads.Count;
        await fixture.ViewModel.ShowAllAsync();

        Assert.Same(rows, fixture.ViewModel.Rows);
        Assert.False(fixture.ViewModel.IsReferenceScoped);
        Assert.Equal(callsBefore + 1, fixture.Service.Reads.Count);
        Assert.Equal(HistoryScope.AllReferences, fixture.Service.Reads[^1].Query.Scope);
        Assert.Null(fixture.Service.Reads[^1].Query.Reference);
    }

    [Fact]
    public async Task LoadMoreAppendsAndUsesMaterializedCountAsSkip()
    {
        using var fixture = new Fixture();
        fixture.Service.Enqueue(new HistoryPage([Row("a"), Row("b")], HasMore: true));
        await fixture.ViewModel.RefreshAsync();

        fixture.Service.Enqueue(new HistoryPage([Row("c")], HasMore: false));
        await fixture.ViewModel.LoadMoreAsync();

        Assert.Equal(3, fixture.ViewModel.Rows.Count);
        Assert.Equal(["a", "b", "c"], fixture.ViewModel.Rows.Select(row => row.Commit.Hash));
        Assert.Equal(2, fixture.Service.Reads[^1].Query.Skip);
        Assert.False(fixture.ViewModel.HasMore);
    }

    [Fact]
    public async Task NavigationExitsReferenceScopeClearsFilterAndSelectsTarget()
    {
        using var fixture = new Fixture();
        fixture.Service.Enqueue(new HistoryPage([Row("branch")], HasMore: false));
        await fixture.ViewModel.ShowReferenceAsync("feature/test", "Branch: feature/test");
        fixture.ViewModel.FilterText = "needle";

        var target = Row("target");
        fixture.Service.EnqueueThrough(new HistoryPage([Row("a"), target, Row("b")], HasMore: true));
        var result = await fixture.ViewModel.NavigateToCommitAsync("target");

        Assert.Same(target, result);
        Assert.Same(target, fixture.ViewModel.SelectedRow);
        Assert.False(fixture.ViewModel.IsReferenceScoped);
        Assert.Equal(string.Empty, fixture.ViewModel.FilterText);
        var query = Assert.Single(fixture.Service.ThroughReads).Query;
        Assert.Equal(HistoryScope.AllReferences, query.Scope);
        Assert.Null(query.Filter);
        Assert.Null(query.Reference);
    }

    [Fact]
    public async Task LatestRefreshWinsWhenOlderRequestCompletesLate()
    {
        using var fixture = new Fixture(controlled: true);

        var oldLoad = fixture.ViewModel.RefreshAsync();
        var newLoad = fixture.ViewModel.RefreshAsync();

        fixture.Service.CompleteRead(1, new HistoryPage([Row("new")], HasMore: false));
        await newLoad;
        fixture.Service.CompleteRead(0, new HistoryPage([Row("old")], HasMore: false));
        await oldLoad;

        Assert.Equal(["new"], fixture.ViewModel.Rows.Select(row => row.Commit.Hash));
        Assert.Equal("new", fixture.ViewModel.SelectedRow?.Commit.Hash);
        Assert.True(fixture.Service.Reads[0].Token.IsCancellationRequested);
    }

    [Fact]
    public async Task RepositorySwitchClearsStateAndRejectsOldCompletion()
    {
        using var fixture = new Fixture(controlled: true);
        var oldLoad = fixture.ViewModel.RefreshAsync();

        var next = new Repository("/next", "/next", "/next/.git", false);
        fixture.Context.Repository = next;
        fixture.ViewModel.OnRepositoryChanged(next);

        fixture.Service.CompleteRead(0, new HistoryPage([Row("old")], HasMore: true));
        await oldLoad;

        Assert.Empty(fixture.ViewModel.Rows);
        Assert.Null(fixture.ViewModel.SelectedRow);
        Assert.False(fixture.ViewModel.HasMore);
        Assert.False(fixture.ViewModel.IsReferenceScoped);
    }

    [Fact]
    public async Task InvalidationDuringBusyCallbackDoesNotAccessDisposedTokenOrRetainLoading()
    {
        using var fixture = new Fixture(controlled: true);
        fixture.Context.OnEnterHistoryBusy = fixture.ViewModel.Invalidate;

        var load = fixture.ViewModel.RefreshAsync();
        var read = Assert.Single(fixture.Service.Reads);
        Assert.True(read.Token.IsCancellationRequested);
        fixture.Service.CompleteRead(0, new HistoryPage([Row("discarded")], HasMore: false));
        await load.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(fixture.ViewModel.IsLoading);
        Assert.Empty(fixture.ViewModel.Rows);
        Assert.Equal(0, fixture.Context.BusyCount);
    }

    [Fact]
    public async Task ReflogFlowsIntoNormalQueryAndReferenceScopeDisablesIt()
    {
        using var fixture = new Fixture(showReflog: true);
        fixture.Service.Enqueue(new HistoryPage([Row("all")], HasMore: false));
        await fixture.ViewModel.RefreshAsync();

        Assert.True(fixture.Service.Reads[^1].Query.IncludeReflog);

        fixture.Service.Enqueue(new HistoryPage([Row("branch")], HasMore: false));
        await fixture.ViewModel.ShowReferenceAsync("feature/test", "Branch: feature/test");

        Assert.False(fixture.ViewModel.ShowReflog);
        Assert.False(fixture.Settings.ShowReflog);
        Assert.True(fixture.Settings.SetShowReflogCalls >= 1);
        Assert.False(fixture.Service.Reads[^1].Query.IncludeReflog);
        Assert.Equal("feature/test", fixture.Service.Reads[^1].Query.Reference);
    }

    [Fact]
    public async Task SuppressedSelectionDoesNotReplaceExternalDetailsSelection()
    {
        using var fixture = new Fixture();
        fixture.Context.CanUpdateHistorySelection = false;
        fixture.Service.Enqueue(new HistoryPage([Row("a")], HasMore: false));

        await fixture.ViewModel.RefreshAsync();

        Assert.Null(fixture.ViewModel.SelectedRow);
    }

    private static HistoryRow Row(string hash) =>
        new(
            new CommitHistoryItem(
                hash,
                [],
                hash,
                hash,
                "author",
                DateTimeOffset.UnixEpoch,
                []),
            new CommitTopology(0, []));

    private sealed class Fixture : IDisposable
    {
        public Fixture(bool controlled = false, bool showReflog = false)
        {
            Repository = new Repository("/repo", "/repo", "/repo/.git", false);
            Service = new FakeHistoryService(controlled);
            Settings = new FakeSettings { ShowReflog = showReflog };
            Context = new FakeContext { Repository = Repository };
            ViewModel = new HistoryViewModel(Service, Settings, new ImmediateDispatcher());
            ViewModel.Attach(Context);
            ViewModel.OnRepositoryChanged(Repository);
        }

        public Repository Repository { get; }
        public FakeHistoryService Service { get; }
        public FakeSettings Settings { get; }
        public FakeContext Context { get; }
        public HistoryViewModel ViewModel { get; }

        public void Dispose() => ViewModel.Dispose();
    }

    private sealed class FakeContext : IHistoryRepositoryContext
    {
        public Repository? Repository { get; set; }
        public bool? HeadExists { get; set; } = true;
        public string? HeadReference { get; set; } = "main";
        public string? HeadCommit { get; set; } = "head";
        public bool IsDetachedHead { get; set; }
        public IReadOnlyList<GitBranch> LocalBranches { get; set; } = [];
        public IReadOnlyList<GitBranch> RemoteBranches { get; set; } = [];
        public IReadOnlyList<GitRemote> Remotes { get; set; } = [];
        public IReadOnlyList<GitTag> Tags { get; set; } = [];
        public bool CanUpdateHistorySelection { get; set; } = true;
        public int BusyCount { get; private set; }
        public Action? OnEnterHistoryBusy { get; set; }
        public string? Error { get; private set; }

        public void EnterHistoryBusy()
        {
            BusyCount++;
            OnEnterHistoryBusy?.Invoke();
        }
        public void ExitHistoryBusy() => BusyCount--;
        public void ReportHistoryError(string message) => Error = message;
    }

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public bool HasThreadAccess => true;

        public bool TryEnqueue(Action action)
        {
            action();
            return true;
        }
    }

    private sealed class FakeHistoryService(bool controlled) : IHistoryService
    {
        private readonly Queue<HistoryPage> _pages = new();
        private readonly Queue<HistoryPage> _throughPages = new();
        private readonly List<TaskCompletionSource<HistoryPage>> _controlledReads = [];
        private readonly List<TaskCompletionSource<HistoryPage>> _controlledThroughReads = [];

        public List<(Repository Repository, HistoryQuery Query, CancellationToken Token)> Reads { get; } = [];
        public List<(Repository Repository, HistoryQuery Query, string Hash, CancellationToken Token)> ThroughReads { get; } = [];

        public void Enqueue(HistoryPage page) => _pages.Enqueue(page);
        public void EnqueueThrough(HistoryPage page) => _throughPages.Enqueue(page);

        public void CompleteRead(int index, HistoryPage page) =>
            _controlledReads[index].TrySetResult(page);

        public void CompleteThroughRead(int index, HistoryPage page) =>
            _controlledThroughReads[index].TrySetResult(page);

        public Task<HistoryPage> ReadHistoryAsync(
            Repository repository,
            HistoryQuery query,
            CancellationToken cancellationToken = default)
        {
            Reads.Add((repository, query, cancellationToken));
            if (!controlled)
                return Task.FromResult(_pages.Dequeue());

            var source = new TaskCompletionSource<HistoryPage>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _controlledReads.Add(source);
            return source.Task;
        }

        public Task<HistoryPage> ReadHistoryThroughCommitAsync(
            Repository repository,
            HistoryScope scope,
            string targetHash,
            int trailingCount = 100,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<HistoryPage> ReadHistoryThroughCommitAsync(
            Repository repository,
            HistoryQuery query,
            string targetHash,
            int trailingCount = 100,
            CancellationToken cancellationToken = default)
        {
            ThroughReads.Add((repository, query, targetHash, cancellationToken));
            if (!controlled)
                return Task.FromResult(_throughPages.Dequeue());

            var source = new TaskCompletionSource<HistoryPage>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _controlledThroughReads.Add(source);
            return source.Task;
        }

        public Task<CommitDetails> ReadCommitAsync(
            Repository repository,
            string hash,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ChangedFile>> ReadChangedFilesAsync(
            Repository repository,
            string commitHash,
            string? parentHash,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FileDiff> ReadDiffAsync(
            Repository repository,
            string hash,
            string path,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FileDiff> ReadDiffAsync(
            Repository repository,
            string commitHash,
            string? parentHash,
            ChangedFile file,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FileDiff> ReadDiffAsync(
            Repository repository,
            string commitHash,
            string? parentHash,
            ChangedFile file,
            DiffLoadMode mode,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeSettings : IAppearanceSettings
    {
        public ApplicationThemeMode ThemeMode => ApplicationThemeMode.System;
        public CommitTimeDisplayMode CommitTimeDisplayMode => CommitTimeDisplayMode.Smart;
        public HistoryRenderingMode HistoryRenderingMode => HistoryRenderingMode.Full;
        public bool ShowReflog { get; set; }
        public bool ShowAuthorAvatars => false;
        public bool OnlineAvatarLookupEnabled => false;
        public int SetShowReflogCalls { get; private set; }

        public event EventHandler? Changed;

        public Task SetShowReflogAsync(bool value, CancellationToken cancellationToken = default)
        {
            SetShowReflogCalls++;
            ShowReflog = value;
            Changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task SetThemeModeAsync(ApplicationThemeMode mode, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetCommitTimeDisplayModeAsync(CommitTimeDisplayMode mode, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetHistoryRenderingModeAsync(HistoryRenderingMode mode, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetShowAuthorAvatarsAsync(bool value, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetOnlineAvatarLookupEnabledAsync(bool value, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
