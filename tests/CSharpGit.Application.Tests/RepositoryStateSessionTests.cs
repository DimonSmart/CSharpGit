using CSharpGit.Application;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Application.Tests;

public sealed class RepositoryStateSessionTests
{
    [Fact]
    public async Task PollingPublishesExternalGitStateChangesWithinReasonableTime()
    {
        var initial = CreateState(headCommit: "commit-1");
        var service = new FakeStateService(initial);
        await using var session = await new RepositoryStateSessionFactory(service).CreateAsync(initial.Repository);
        var changed = new TaskCompletionSource<RepositoryState>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.StateChanged += (_, state) => changed.TrySetResult(state);

        service.CurrentState = CreateState(headCommit: "external-commit");
        var observed = await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("external-commit", observed.HeadCommit);
        Assert.Equal("external-commit", session.Current.HeadCommit);
    }

    [Fact]
    public async Task PollingContinuesAfterTransientReadFailure()
    {
        var initial = CreateState(headCommit: "commit-1");
        var service = new FakeStateService(initial);
        await using var session = await new RepositoryStateSessionFactory(service).CreateAsync(initial.Repository);
        var changed = new TaskCompletionSource<RepositoryState>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.StateChanged += (_, state) => changed.TrySetResult(state);

        service.FailuresRemaining = 1;
        service.CurrentState = CreateState(headCommit: "commit-2");

        var observed = await changed.Task.WaitAsync(TimeSpan.FromSeconds(7));

        Assert.Equal("commit-2", observed.HeadCommit);
        Assert.True(service.ReadCount >= 3);
    }

    [Fact]
    public async Task RefreshesStateEvenWhenMutationFails()
    {
        var initial = CreateState(headCommit: "commit-1");
        var service = new FakeStateService(initial);
        await using var session = await new RepositoryStateSessionFactory(service).CreateAsync(initial.Repository);
        service.CurrentState = CreateState(headCommit: "commit-2");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.RunMutationAsync(_ => throw new InvalidOperationException("failure")));

        Assert.Equal(2, service.ReadCount);
        Assert.Equal("commit-2", session.Current.HeadCommit);
    }

    [Fact]
    public async Task ExternalRemoteRefChangePublishesStateChanged()
    {
        var initial = CreateState(references: Refs(remoteBranches: [new("origin/main", "A")]));
        var next = CreateState(references: Refs(remoteBranches: [new("origin/main", "B")]));

        await AssertPublishesStateChanged(initial, next);
    }

    [Fact]
    public async Task ExternalLocalBranchChangePublishesStateChanged()
    {
        var initial = CreateState(references: Refs(localBranches: [new("main", "A", true)]));
        var next = CreateState(references: Refs(localBranches: [new("main", "B", true)]));

        await AssertPublishesStateChanged(initial, next);
    }

    [Fact]
    public async Task ExternalTagChangePublishesStateChanged()
    {
        var initial = CreateState(references: Refs(tags: [new("v1", "A")]));
        var next = CreateState(references: Refs(tags: [new("v1", "B")]));

        await AssertPublishesStateChanged(initial, next);
    }

    [Theory]
    [InlineData("FetchUrl")]
    [InlineData("PushUrl")]
    public async Task ExternalRemoteConfigurationChangePublishesStateChanged(string field)
    {
        var initialRemote = new GitRemote("origin", "fetch-A", "push-A");
        var nextRemote = field switch
        {
            "FetchUrl" => initialRemote with { FetchUrl = "fetch-B" },
            "PushUrl" => initialRemote with { PushUrl = "push-B" },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        var initial = CreateState(references: Refs(remotes: [initialRemote]));
        var next = CreateState(references: Refs(remotes: [nextRemote]));

        await AssertPublishesStateChanged(initial, next);
    }

    [Theory]
    [InlineData("LocalBranch", false)]
    [InlineData("LocalBranch", true)]
    [InlineData("RemoteBranch", false)]
    [InlineData("RemoteBranch", true)]
    [InlineData("Tag", false)]
    [InlineData("Tag", true)]
    [InlineData("Remote", false)]
    [InlineData("Remote", true)]
    public async Task RefMembershipChangePublishesStateChanged(string refKind, bool removal)
    {
        var empty = GitReferences.Empty;
        var populated = refKind switch
        {
            "LocalBranch" => Refs(localBranches: [new GitBranch("topic", "A")]),
            "RemoteBranch" => Refs(remoteBranches: [new GitBranch("origin/topic", "A")]),
            "Tag" => Refs(tags: [new GitTag("v1", "A")]),
            "Remote" => Refs(remotes: [new GitRemote("origin", "fetch", "push")]),
            _ => throw new ArgumentOutOfRangeException(nameof(refKind))
        };
        var initial = CreateState(references: removal ? populated : empty);
        var next = CreateState(references: removal ? empty : populated);

        await AssertPublishesStateChanged(initial, next);
    }

    [Fact]
    public async Task ExternalStashChangePublishesStateChanged()
    {
        var initial = CreateState(stashes: []);
        var next = CreateState(stashes: [new GitStash("stash@{0}", "S", "work")]);

        await AssertPublishesStateChanged(initial, next);
    }

    [Fact]
    public async Task ConflictResolutionChangePublishesStateChanged()
    {
        var unresolved = Conflict("foo.cs", isResolved: false);
        var resolved = unresolved with { IsResolved = true };
        var initial = CreateState(
            operation: RepositoryOperation.Rebase,
            operationDetails: Operation(RepositoryOperation.Rebase, [unresolved]));
        var next = CreateState(
            operation: RepositoryOperation.Rebase,
            operationDetails: Operation(RepositoryOperation.Rebase, [resolved]));

        await AssertPublishesStateChanged(initial, next);
    }

    [Fact]
    public async Task CurrentOperationKindChangePublishesStateChangedWhenTopLevelOperationIsUnchanged()
    {
        var initial = CreateState(
            operation: RepositoryOperation.Rebase,
            operationDetails: Operation(RepositoryOperation.Rebase, []));
        var next = CreateState(
            operation: RepositoryOperation.Rebase,
            operationDetails: Operation(RepositoryOperation.Merge, []));

        await AssertPublishesStateChanged(initial, next);
    }

    [Theory]
    [InlineData("CanContinue")]
    [InlineData("CanAbort")]
    [InlineData("CanSkip")]
    public async Task OperationCapabilitiesChangePublishesStateChanged(string field)
    {
        var initialOperation = Operation(RepositoryOperation.Rebase, []);
        var nextOperation = field switch
        {
            "CanContinue" => initialOperation with { CanContinue = true },
            "CanAbort" => initialOperation with { CanAbort = true },
            "CanSkip" => initialOperation with { CanSkip = true },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        var initial = CreateState(operation: RepositoryOperation.Rebase, operationDetails: initialOperation);
        var next = CreateState(operation: RepositoryOperation.Rebase, operationDetails: nextOperation);

        await AssertPublishesStateChanged(initial, next);
    }

    [Theory]
    [InlineData("IsCurrent")]
    [InlineData("Upstream")]
    [InlineData("Ahead")]
    [InlineData("Behind")]
    public async Task LocalBranchMetadataChangePublishesStateChanged(string field)
    {
        var initialBranch = new GitBranch("main", "A", true, "origin/main");
        var nextBranch = field switch
        {
            "IsCurrent" => initialBranch with { IsCurrent = false },
            "Upstream" => initialBranch with { Upstream = "backup/main" },
            "Ahead" => initialBranch with { Ahead = 1 },
            "Behind" => initialBranch with { Behind = 1 },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        var initial = CreateState(references: Refs(localBranches: [initialBranch]));
        var next = CreateState(references: Refs(localBranches: [nextBranch]));

        await AssertPublishesStateChanged(initial, next);
    }

    [Theory]
    [InlineData("IsCurrent")]
    [InlineData("Upstream")]
    [InlineData("Ahead")]
    [InlineData("Behind")]
    public async Task RemoteBranchMetadataChangePublishesStateChanged(string field)
    {
        var initialBranch = new GitBranch("origin/main", "A");
        var nextBranch = field switch
        {
            "IsCurrent" => initialBranch with { IsCurrent = true },
            "Upstream" => initialBranch with { Upstream = "other/main" },
            "Ahead" => initialBranch with { Ahead = 1 },
            "Behind" => initialBranch with { Behind = 1 },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        var initial = CreateState(references: Refs(remoteBranches: [initialBranch]));
        var next = CreateState(references: Refs(remoteBranches: [nextBranch]));

        await AssertPublishesStateChanged(initial, next);
    }

    [Fact]
    public async Task ReadTimestampOnlyChangeDoesNotPublishStateChanged()
    {
        var initial = CreateState(readAtUtc: DateTimeOffset.UnixEpoch);
        var next = initial with { ReadAtUtc = DateTimeOffset.UnixEpoch.AddSeconds(1) };

        await AssertDoesNotPublishStateChanged(initial, next);
    }

    [Fact]
    public async Task EquivalentCollectionsWithDifferentInstancesDoNotPublishStateChanged()
    {
        var initialRepository = CreateRepository();
        var equivalentRepository = CreateRepository();
        var initial = new RepositoryState(
            initialRepository,
            "main",
            "A",
            false,
            RepositoryOperation.Rebase,
            [new WorkingTreeChange("file.txt", 'M', ' ')],
            new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" },
            new Dictionary<string, string> { ["y"] = "2", ["x"] = "1" },
            DateTimeOffset.UnixEpoch,
            new GitReferences(
                [new GitBranch("main", "A", true, "origin/main", 1, 2)],
                [new GitBranch("origin/main", "B")],
                [new GitRemote("origin", "fetch", "push")],
                [new GitTag("v1", "A")]),
            [new GitStash("stash@{0}", "S", "work")],
            Operation(RepositoryOperation.Rebase, [Conflict("file.txt", false)]));
        var next = new RepositoryState(
            equivalentRepository,
            string.Concat("ma", "in"),
            new string('A', 1),
            false,
            RepositoryOperation.Rebase,
            [new WorkingTreeChange(string.Concat("file", ".txt"), 'M', ' ')],
            new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" },
            new SortedDictionary<string, string> { ["x"] = "1", ["y"] = "2" },
            DateTimeOffset.UnixEpoch.AddMinutes(1),
            new GitReferences(
                [new GitBranch("main", "A", true, "origin/main", 1, 2)],
                [new GitBranch("origin/main", "B")],
                [new GitRemote("origin", "fetch", "push")],
                [new GitTag("v1", "A")]),
            [new GitStash("stash@{0}", "S", "work")],
            Operation(RepositoryOperation.Rebase, [Conflict("file.txt", false)]));

        await AssertDoesNotPublishStateChanged(initial, next);
    }

    [Fact]
    public async Task EquivalentRefsInDifferentOrderDoNotPublishStateChanged()
    {
        var initial = CreateState(references: new GitReferences(
            [new("main", "A", true), new("feature", "B")],
            [new("origin/main", "A"), new("origin/feature", "B")],
            [new("origin", "f1", "p1"), new("backup", "f2", "p2")],
            [new("v1", "A"), new("v2", "B")]));
        var next = CreateState(references: new GitReferences(
            [new("feature", "B"), new("main", "A", true)],
            [new("origin/feature", "B"), new("origin/main", "A")],
            [new("backup", "f2", "p2"), new("origin", "f1", "p1")],
            [new("v2", "B"), new("v1", "A")]));

        await AssertDoesNotPublishStateChanged(initial, next);
    }

    [Fact]
    public async Task EquivalentConflictsInDifferentOrderDoNotPublishStateChanged()
    {
        var first = Conflict("a.cs", false);
        var second = Conflict("b.cs", true);
        var initial = CreateState(
            operation: RepositoryOperation.Merge,
            operationDetails: Operation(RepositoryOperation.Merge, [first, second], canContinue: true, canAbort: true));
        var next = CreateState(
            operation: RepositoryOperation.Merge,
            operationDetails: Operation(RepositoryOperation.Merge, [second, first], canContinue: true, canAbort: true));

        await AssertDoesNotPublishStateChanged(initial, next);
    }

    [Fact]
    public async Task EquivalentStashesInDifferentOrderDoNotPublishStateChanged()
    {
        var first = new GitStash("stash@{0}", "A", "first");
        var second = new GitStash("stash@{1}", "B", "second");
        var initial = CreateState(stashes: [first, second]);
        var next = CreateState(stashes: [second, first]);

        await AssertDoesNotPublishStateChanged(initial, next);
    }

    [Fact]
    public async Task NullAndExplicitEmptyOptionalStateAreEquivalent()
    {
        var repository = CreateRepository();
        var initial = new RepositoryState(
            repository, "main", "A", false, RepositoryOperation.None,
            [], new Dictionary<string, string>(), new Dictionary<string, string>(), DateTimeOffset.UnixEpoch);
        var next = new RepositoryState(
            repository, "main", "A", false, RepositoryOperation.None,
            [], new Dictionary<string, string>(), new Dictionary<string, string>(), DateTimeOffset.UnixEpoch.AddSeconds(1),
            GitReferences.Empty, [], RepositoryOperationState.None);

        await AssertDoesNotPublishStateChanged(initial, next);
    }

    private static async Task AssertPublishesStateChanged(RepositoryState initial, RepositoryState next)
    {
        var service = new FakeStateService(initial);
        await using var session = await new RepositoryStateSessionFactory(service).CreateAsync(initial.Repository);
        var published = 0;
        RepositoryState? observed = null;
        session.StateChanged += (_, state) =>
        {
            published++;
            observed = state;
        };

        service.CurrentState = next;
        await session.RefreshAsync();

        Assert.Equal(1, published);
        Assert.Same(next, observed);
        Assert.Same(next, session.Current);
    }

    private static async Task AssertDoesNotPublishStateChanged(RepositoryState initial, RepositoryState next)
    {
        var service = new FakeStateService(initial);
        await using var session = await new RepositoryStateSessionFactory(service).CreateAsync(initial.Repository);
        var published = 0;
        session.StateChanged += (_, _) => published++;

        service.CurrentState = next;
        await session.RefreshAsync();

        Assert.Equal(0, published);
        Assert.Same(next, session.Current);
    }

    private static RepositoryState CreateState(
        string headCommit = "A",
        DateTimeOffset? readAtUtc = null,
        RepositoryOperation operation = RepositoryOperation.None,
        GitReferences? references = null,
        IReadOnlyList<GitStash>? stashes = null,
        RepositoryOperationState? operationDetails = null) =>
        new(
            CreateRepository(),
            "main",
            headCommit,
            false,
            operation,
            [],
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            readAtUtc ?? DateTimeOffset.UnixEpoch,
            references,
            stashes,
            operationDetails);

    private static Repository CreateRepository() => new("work", "root", "git", false);

    private static GitReferences Refs(
        IReadOnlyList<GitBranch>? localBranches = null,
        IReadOnlyList<GitBranch>? remoteBranches = null,
        IReadOnlyList<GitRemote>? remotes = null,
        IReadOnlyList<GitTag>? tags = null) =>
        new(localBranches ?? [], remoteBranches ?? [], remotes ?? [], tags ?? []);

    private static RepositoryOperationState Operation(
        RepositoryOperation kind,
        IReadOnlyList<ConflictFile> conflicts,
        bool canContinue = false,
        bool canAbort = false,
        bool canSkip = false) =>
        new(kind, conflicts, canContinue, canAbort, canSkip);

    private static ConflictFile Conflict(string path, bool isResolved) =>
        new(
            path,
            ConflictKind.Textual,
            isResolved,
            CanOpenManually: true,
            CanChooseCurrentLocal: true,
            CanChooseIncomingRemote: true,
            CanKeepDeletion: false,
            CanStage: isResolved,
            CanRunMergeTool: !isResolved,
            CurrentLocalLabel: "Current",
            IncomingRemoteLabel: "Incoming");

    private sealed class FakeStateService : IRepositoryStateService
    {
        private readonly object _sync = new();
        private RepositoryState _currentState;
        private int _readCount;
        private int _failuresRemaining;

        public FakeStateService(RepositoryState currentState)
        {
            _currentState = currentState;
        }

        public RepositoryState CurrentState
        {
            get { lock (_sync) return _currentState; }
            set { lock (_sync) _currentState = value; }
        }

        public int ReadCount
        {
            get { lock (_sync) return _readCount; }
        }

        public int FailuresRemaining
        {
            get { lock (_sync) return _failuresRemaining; }
            set { lock (_sync) _failuresRemaining = value; }
        }

        public Task<RepositoryState> ReadAsync(Repository ignored, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                _readCount++;
                if (_failuresRemaining > 0)
                {
                    _failuresRemaining--;
                    throw new IOException("transient read failure");
                }

                return Task.FromResult(_currentState);
            }
        }
    }
}
