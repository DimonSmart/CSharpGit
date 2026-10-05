using System.Diagnostics;
using CSharpGit.Domain;

namespace CSharpGit.Git.Tests;

public sealed class ReflogHistoryTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"csharpgit-reflog-history-{Guid.NewGuid():N}");

    [Fact]
    public async Task ReflogModeRecoversUnreachableCommitAndReclassifiesAfterBranchCreation()
    {
        InitializeRepository();
        Commit("A");
        Commit("B");
        Commit("C");
        var lostHash = RunGit("rev-parse", "HEAD");
        RunGit("reset", "--hard", "HEAD~1");

        var repository = await GitTestServices.CreateRepositoryService().OpenAsync(_temporaryDirectory);
        var service = GitTestServices.CreateReferenceHistoryService();

        var normal = await service.ReadHistoryAsync(
            repository,
            new HistoryQuery(HistoryScope.AllReferences, null, 0, 20));
        Assert.DoesNotContain(normal.Rows, row => row.Commit.Hash == lostHash);

        var state = await GitTestServices.CreateRepositoryStateService().ReadAsync(repository);
        var expanded = await service.ReadHistoryAsync(
            repository,
            new HistoryQuery(
                HistoryScope.AllReferences,
                null,
                0,
                20,
                IncludeReflog: true,
                RepositoryReferences: state.Refs,
                HeadReference: state.HeadReference,
                HeadCommit: state.HeadCommit,
                IsDetachedHead: state.IsDetached,
                ReflogSessionId: 1));
        var recovered = Assert.Single(expanded.Rows, row => row.Commit.Hash == lostHash);
        Assert.True(recovered.IsReflogOnly);
        Assert.DoesNotContain("reflog", recovered.Commit.References);
        Assert.NotNull(recovered.Reflog);
        Assert.StartsWith("main@{", recovered.Reflog!.Selector, StringComparison.Ordinal);
        Assert.Equal("commit", recovered.Reflog.EventKind);
        Assert.Contains("commit: C", recovered.Reflog.Subject, StringComparison.Ordinal);
        Assert.StartsWith("◌ main@{", recovered.ReflogGhostDisplay, StringComparison.Ordinal);
        Assert.Contains(expanded.Rows, row => row.Commit.Subject == "B");
        Assert.Equal(expanded.Rows.Count, expanded.Rows.Select(row => row.Commit.Hash).Distinct().Count());

        RunGit("branch", "recovered", lostHash);

        var refreshed = await service.ReadHistoryAsync(
            repository,
            new HistoryQuery(HistoryScope.AllReferences, null, 0, 20, IncludeReflog: true));
        var normalAgain = Assert.Single(refreshed.Rows, row => row.Commit.Hash == lostHash);
        Assert.False(normalAgain.IsReflogOnly);
    }

    [Fact]
    public async Task ReflogModeUnionsNormalReferencesWithReflogRoots()
    {
        InitializeRepository();
        Commit("main commit");
        var parentHash = RunGit("rev-parse", "HEAD");
        var treeHash = RunGit("rev-parse", "HEAD^{tree}");
        var tagOnlyHash = RunGit("commit-tree", treeHash, "-p", parentHash, "-m", "tag-only commit");
        RunGit("tag", "union-only", tagOnlyHash);

        var reflogHashes = RunGit("rev-list", "--reflog")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.DoesNotContain(tagOnlyHash, reflogHashes);

        var repository = await GitTestServices.CreateRepositoryService().OpenAsync(_temporaryDirectory);
        var service = GitTestServices.CreateReferenceHistoryService();

        var normal = await service.ReadHistoryAsync(
            repository,
            new HistoryQuery(HistoryScope.AllReferences, null, 0, 20));
        Assert.Contains(normal.Rows, row => row.Commit.Hash == tagOnlyHash);

        var expanded = await service.ReadHistoryAsync(
            repository,
            new HistoryQuery(HistoryScope.AllReferences, null, 0, 20, IncludeReflog: true));
        var tagOnly = Assert.Single(expanded.Rows, row => row.Commit.Hash == tagOnlyHash);
        Assert.False(tagOnly.IsReflogOnly);
    }

    [Fact]
    public async Task ReflogModeParticipatesInFilteringButNeverExtendsCurrentBranchScope()
    {
        InitializeRepository();
        Commit("base");
        Commit("needle from reflog");
        var lostHash = RunGit("rev-parse", "HEAD");
        RunGit("reset", "--hard", "HEAD~1");

        var repository = await GitTestServices.CreateRepositoryService().OpenAsync(_temporaryDirectory);
        var service = GitTestServices.CreateReferenceHistoryService();

        var filtered = await service.ReadHistoryAsync(
            repository,
            new HistoryQuery(HistoryScope.AllReferences, "needle", 0, 20, IncludeReflog: true));
        var recovered = Assert.Single(filtered.Rows);
        Assert.Equal(lostHash, recovered.Commit.Hash);
        Assert.True(recovered.IsReflogOnly);

        var currentBranch = await service.ReadHistoryAsync(
            repository,
            new HistoryQuery(HistoryScope.CurrentBranch, null, 0, 20, IncludeReflog: true));
        Assert.DoesNotContain(currentBranch.Rows, row => row.Commit.Hash == lostHash);
        Assert.All(currentBranch.Rows, row => Assert.False(row.IsReflogOnly));
    }

    [Fact]
    public async Task ReferenceDecorationsPreserveSemanticIdentityWhenDisplayNamesCollide()
    {
        InitializeRepository();
        Commit("base");
        var hash = RunGit("rev-parse", "HEAD");
        RunGit("branch", "origin/main", hash);
        RunGit("update-ref", "refs/remotes/origin/main", hash);

        var repository = await GitTestServices.CreateRepositoryService().OpenAsync(_temporaryDirectory);
        var state = await GitTestServices.CreateRepositoryStateService().ReadAsync(repository);
        var service = GitTestServices.CreateReferenceHistoryService();

        var history = await service.ReadHistoryAsync(
            repository,
            new HistoryQuery(
                HistoryScope.AllReferences,
                null,
                0,
                20,
                RepositoryReferences: state.Refs,
                HeadReference: state.HeadReference,
                HeadCommit: state.HeadCommit,
                IsDetachedHead: state.IsDetached));

        var row = Assert.Single(history.Rows, candidate => candidate.Commit.Hash == hash);
        Assert.Contains(
            row.ReferenceDetails,
            reference => reference.DisplayName == "main"
                         && reference.Kind == HistoryReferenceKind.CurrentLocalBranch);

        var colliding = row.ReferenceDetails
            .Where(reference => reference.DisplayName == "origin/main")
            .ToList();
        Assert.Equal(2, colliding.Count);
        Assert.Contains(colliding, reference => reference.Kind == HistoryReferenceKind.LocalBranch);
        Assert.Contains(colliding, reference => reference.Kind == HistoryReferenceKind.RemoteTrackingBranch);
    }

    [Fact]
    public async Task DetachedHeadGetsDedicatedSemanticMarkerWithoutUpstreamCounters()
    {
        InitializeRepository();
        Commit("base");
        var hash = RunGit("rev-parse", "HEAD");
        RunGit("checkout", "--detach", hash);

        var repository = await GitTestServices.CreateRepositoryService().OpenAsync(_temporaryDirectory);
        var state = await GitTestServices.CreateRepositoryStateService().ReadAsync(repository);
        var service = GitTestServices.CreateReferenceHistoryService();

        var history = await service.ReadHistoryAsync(
            repository,
            new HistoryQuery(
                HistoryScope.AllReferences,
                null,
                0,
                20,
                RepositoryReferences: state.Refs,
                HeadReference: state.HeadReference,
                HeadCommit: state.HeadCommit,
                IsDetachedHead: state.IsDetached));

        var row = Assert.Single(history.Rows, candidate => candidate.Commit.Hash == hash);
        var detached = Assert.Single(
            row.ReferenceDetails,
            reference => reference.Kind == HistoryReferenceKind.DetachedHead);
        Assert.Equal("HEAD", detached.DisplayName);
        Assert.Null(detached.Upstream);
        Assert.Equal(0, detached.Ahead);
        Assert.Equal(0, detached.Behind);
    }

    private void InitializeRepository()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        RunGit("init", "-b", "main");
        RunGit("config", "user.email", "tests@example.invalid");
        RunGit("config", "user.name", "CSharpGit Tests");
    }

    private void Commit(string subject)
    {
        File.AppendAllText(Path.Combine(_temporaryDirectory, "history.txt"), subject + "\n");
        RunGit("add", "history.txt");
        RunGit("commit", "-m", subject);
    }

    private string RunGit(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = _temporaryDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException(error);
        return output.Trim();
    }

    public void Dispose() => TestDirectory.Delete(_temporaryDirectory);
}
