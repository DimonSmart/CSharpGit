using CSharpGit.Application.Abstractions;
using CSharpGit.Application.Exceptions;
using CSharpGit.Domain;
using CSharpGit.Presentation.Threading;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Desktop.Tests;

public sealed class FileActionsViewModelTests
{
    private static readonly Repository RepoA = new("/repo", "/repo", "/repo/.git", false);
    private static readonly Repository RepoB = new("/other", "/other", "/other/.git", false);
    private static readonly DiffFileVersionPair NormalPair = new(
        new(DiffFileVersionLocation.GitSnapshot, "old.txt", "HEAD"),
        new(DiffFileVersionLocation.WorkingCopy, "new.txt"),
        "new.txt", "M");

    [Theory]
    [InlineData(FileActionKind.Original, "original.snapshot")]
    [InlineData(FileActionKind.Changed, "/files/new.txt")]
    public async Task OpensCorrectPairSide(FileActionKind action, string expectedPath)
    {
        using var fixture = new Fixture();
        var request = new CommitFileActionRequest(RepoA, "abc", "new.txt");
        var result = await fixture.Vm.ExecuteAsync(request, action);
        Assert.Equal(FileActionOutcomeKind.Completed, result.Kind);
        Assert.Equal(expectedPath, fixture.Shell.OpenedPath);
        Assert.Equal("abc", fixture.Versions.LastCommitHash);
    }

    [Fact]
    public async Task ExternalDiffReceivesExactlyResolvedPair()
    {
        using var fixture = new Fixture();
        var request = new CommitFileActionRequest(RepoA, "abc", "new.txt");
        var result = await fixture.Vm.ExecuteAsync(request, FileActionKind.ExternalDiff);
        Assert.Equal(FileActionOutcomeKind.Completed, result.Kind);
        Assert.Same(NormalPair, fixture.Tools.DiffPair);
    }

    [Fact]
    public async Task MissingExternalToolHasTypedOutcome()
    {
        using var fixture = new Fixture();
        fixture.Tools.NotConfigured = true;
        var result = await fixture.Vm.ExecuteAsync(
            new CommitFileActionRequest(RepoA, "abc", "new.txt"), FileActionKind.ExternalDiff);
        Assert.Equal(FileActionOutcomeKind.NotConfigured, result.Kind);
    }

    [Fact]
    public async Task MissingOriginalIsUnavailable()
    {
        using var fixture = new Fixture();
        fixture.Versions.CommitPair = new(
            new(DiffFileVersionLocation.Unavailable, "added.txt", EntryKind: GitEntryKind.Missing),
            NormalPair.Changed, "added.txt", "A");
        var result = await fixture.Vm.ExecuteAsync(
            new CommitFileActionRequest(RepoA, "abc", "added.txt"), FileActionKind.Original);
        Assert.Equal(FileActionOutcomeKind.Unavailable, result.Kind);
        Assert.Null(fixture.Shell.OpenedPath);
    }

    [Fact]
    public async Task DeletedFilePrefersOriginal()
    {
        using var fixture = new Fixture();
        fixture.Versions.CommitPair = new(
            NormalPair.Original,
            new(DiffFileVersionLocation.Unavailable, "old.txt", EntryKind: GitEntryKind.Missing),
            "old.txt", "D");
        var result = await fixture.Vm.ExecuteAsync(
            new CommitFileActionRequest(RepoA, "abc", "old.txt"), FileActionKind.Preferred);
        Assert.Equal(FileActionOutcomeKind.Completed, result.Kind);
        Assert.Equal("original.snapshot", fixture.Shell.OpenedPath);
    }

    [Fact]
    public async Task RevealValidatesWorkingCopyPathAgain()
    {
        using var fixture = new Fixture();
        var request = new CommitFileActionRequest(RepoA, "abc", "new.txt");
        var prepared = await fixture.Vm.PrepareAsync(request);
        Assert.NotNull(prepared);
        fixture.Paths.Missing = true;
        var result = await fixture.Vm.ExecuteAsync(request, FileActionKind.Reveal, prepared);
        Assert.Equal(FileActionOutcomeKind.Failed, result.Kind);
        Assert.Null(fixture.Shell.RevealedPath);
    }

    [Fact]
    public async Task SwitchingRepositoryBeforeEffectPreventsLaunch()
    {
        using var fixture = new Fixture();
        var request = new CommitFileActionRequest(RepoA, "abc", "new.txt");
        var prepared = await fixture.Vm.PrepareAsync(request);
        fixture.Context.Current = RepoB;
        var result = await fixture.Vm.ExecuteAsync(request, FileActionKind.ExternalDiff, prepared);
        Assert.Equal(FileActionOutcomeKind.Stale, result.Kind);
        Assert.Null(fixture.Tools.DiffPair);
    }

    [Fact]
    public async Task ChangingSelectionDiscardsLateResultWithoutRedirect()
    {
        using var fixture = new Fixture();
        var delayed = new TaskCompletionSource<DiffFileVersionPair>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Versions.DelayedPath = "a.txt";
        fixture.Versions.DelayedPair = delayed.Task;
        var requestA = new CommitFileActionRequest(RepoA, "abc", "a.txt");
        var requestB = new CommitFileActionRequest(RepoA, "abc", "b.txt");
        var first = fixture.Vm.SelectCommitAsync(requestA);
        await fixture.Vm.SelectCommitAsync(requestB);
        Assert.Equal(requestB, fixture.Vm.Commit.Request);
        delayed.SetResult(NormalPair);
        await first;
        Assert.Equal(requestB, fixture.Vm.Commit.Request);
        Assert.Equal(FileActionLoadPhase.Ready, fixture.Vm.Commit.Phase);
    }

    [Fact]
    public async Task ContextMenuPreparedActionKeepsOriginalFile()
    {
        using var fixture = new Fixture();
        var requestA = new CommitFileActionRequest(RepoA, "abc", "a.txt");
        var preparedA = await fixture.Vm.PrepareAsync(requestA);
        await fixture.Vm.SelectCommitAsync(new CommitFileActionRequest(RepoA, "abc", "b.txt"));
        var result = await fixture.Vm.ExecuteAsync(requestA, FileActionKind.ExternalDiff, preparedA);
        Assert.Equal(FileActionOutcomeKind.Completed, result.Kind);
        Assert.Same(preparedA!.Pair, fixture.Tools.DiffPair);
    }

    [Fact]
    public async Task WorkingTreeModesResolveIndependently()
    {
        using var fixture = new Fixture();
        var change = new WorkingTreeChange("new.txt", 'M', 'M');
        var staged = new WorkingTreeFileActionRequest(RepoA, change, WorkingTreeDiffKind.Staged);
        var unstaged = new WorkingTreeFileActionRequest(RepoA, change, WorkingTreeDiffKind.Unstaged);
        await fixture.Vm.SelectWorkingTreeAsync(staged);
        Assert.Equal(WorkingTreeDiffKind.Staged, fixture.Versions.LastKind);
        await fixture.Vm.SelectWorkingTreeAsync(unstaged);
        Assert.Equal(WorkingTreeDiffKind.Unstaged, fixture.Versions.LastKind);
        Assert.Equal(unstaged, fixture.Vm.WorkingTree.Request);
    }

    [Fact]
    public async Task UnsupportedEntryCannotRunExternalDiff()
    {
        using var fixture = new Fixture();
        fixture.Versions.CommitPair = new(
            new(DiffFileVersionLocation.GitSnapshot, "link", EntryKind: GitEntryKind.SymbolicLink),
            NormalPair.Changed, "link", "M");
        var result = await fixture.Vm.ExecuteAsync(
            new CommitFileActionRequest(RepoA, "abc", "link"), FileActionKind.ExternalDiff);
        Assert.Equal(FileActionOutcomeKind.Unavailable, result.Kind);
        Assert.Null(fixture.Tools.DiffPair);
    }

    [Fact]
    public async Task DisposePreventsLateSelectionPublication()
    {
        var fixture = new Fixture();
        var delayed = new TaskCompletionSource<DiffFileVersionPair>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Versions.DelayedPath = "a.txt";
        fixture.Versions.DelayedPair = delayed.Task;
        var task = fixture.Vm.SelectCommitAsync(new CommitFileActionRequest(RepoA, "abc", "a.txt"));
        fixture.Vm.Dispose();
        delayed.SetResult(NormalPair);
        await task;
        Assert.Null(fixture.Vm.Commit.Prepared);
        fixture.Dispose();
    }

    private sealed class Fixture : IDisposable
    {
        public Versions Versions { get; } = new();
        public Paths Paths { get; } = new();
        public Shell Shell { get; } = new();
        public Tools Tools { get; } = new();
        public Context Context { get; } = new();
        public FileActionsViewModel Vm { get; }

        public Fixture()
        {
            Vm = new FileActionsViewModel(Versions, Paths, Shell, Tools, new Dispatcher());
            Vm.Attach(Context);
        }

        public void Dispose() { Vm.Dispose(); Context.Dispose(); }
    }

    private sealed class Context : IFileActionsContext, IDisposable
    {
        private readonly RepositorySessionLifetime _lifetime = new(1);
        public Repository? Current { get; set; } = RepoA;
        public Repository? FileActionsRepository => Current;
        public bool IsCurrentFileActionsRepository(Repository repository) =>
            ReferenceEquals(repository, Current);
        public FileActionsReadLease? AcquireFileActionsRead(Repository repository) =>
            IsCurrentFileActionsRepository(repository) && _lifetime.TryAcquire(out var ct)
                ? new(_lifetime, ct) : null;
        public void Dispose() => _lifetime.End();
    }

    private sealed class Dispatcher : IUiDispatcher
    {
        public bool HasThreadAccess => true;
        public bool TryEnqueue(Action action) { action(); return true; }
    }

    private sealed class Versions : IRepositoryFileVersionService
    {
        public DiffFileVersionPair CommitPair { get; set; } = NormalPair;
        public string? LastCommitHash { get; private set; }
        public WorkingTreeDiffKind? LastKind { get; private set; }
        public string? DelayedPath { get; set; }
        public Task<DiffFileVersionPair>? DelayedPair { get; set; }

        public Task<DiffFileVersionPair> ResolveCommitAsync(Repository repository, string commitHash,
            string selectedPath, CancellationToken cancellationToken = default)
        {
            LastCommitHash = commitHash;
            return selectedPath == DelayedPath && DelayedPair is not null
                ? DelayedPair : Task.FromResult(CommitPair);
        }

        public Task<DiffFileVersionPair> ResolveWorkingTreeAsync(Repository repository, WorkingTreeChange change,
            WorkingTreeDiffKind kind, CancellationToken cancellationToken = default)
        {
            LastKind = kind;
            return Task.FromResult(CommitPair);
        }

        public Task<MaterializedFileVersion> MaterializeAsync(Repository repository, DiffFileVersion version,
            DiffFileSide side, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MaterializedFileVersion("original.snapshot"));
    }

    private sealed class Paths : IRepositoryPathService
    {
        public bool Missing { get; set; }
        public string ResolveExistingWorkingTreeFile(Repository repository, string gitPath,
            bool allowFinalLink = false) =>
            Missing ? throw new FileNotFoundException() : "/files/" + gitPath;
    }

    private sealed class Shell : IDesktopShellService
    {
        public string OpenFolderDescription => "Open";
        public string RevealDescription => "Reveal";
        public string? OpenedPath { get; private set; }
        public string? RevealedPath { get; private set; }
        public Task OpenFileAsync(string path, CancellationToken cancellationToken = default)
        {
            OpenedPath = path; return Task.CompletedTask;
        }
        public Task OpenFolderAsync(string path, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task RevealFileAsync(string path, CancellationToken cancellationToken = default)
        {
            RevealedPath = path; return Task.CompletedTask;
        }
    }

    private sealed class Tools : IExternalGitToolService
    {
        public DiffFileVersionPair? DiffPair { get; private set; }
        public bool NotConfigured { get; set; }
        public Task RunExternalDiffAsync(Repository repository, DiffFileVersionPair pair,
            CancellationToken cancellationToken = default)
        {
            if (NotConfigured) throw new GitToolNotConfiguredException("Diff tool");
            DiffPair = pair;
            return Task.CompletedTask;
        }
        public Task OpenEditorAsync(Repository? repository, string filePath,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RunMergeToolForFileAsync(Repository repository, ConflictFile conflict,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RunMergeToolWorkflowAsync(Repository repository,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task OpenConflictInEditorAsync(Repository repository, ConflictFile conflict,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task TestAsync(Repository? repository, GitToolKind kind,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
