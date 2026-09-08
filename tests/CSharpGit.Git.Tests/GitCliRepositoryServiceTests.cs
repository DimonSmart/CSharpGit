using System.Diagnostics;

namespace CSharpGit.Git.Tests;

public sealed class GitCliRepositoryServiceTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"csharpgit-{Guid.NewGuid():N}");

    [Fact]
    public async Task OpensOrdinaryRepository()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        RunGit(_temporaryDirectory, "init");

        var repository = await new GitCliRepositoryService().OpenAsync(_temporaryDirectory);

        Assert.False(repository.IsWorktree);
        Assert.Equal(Path.GetFullPath(_temporaryDirectory), repository.RepositoryRoot);
    }

    [Fact]
    public async Task OpensLinkedWorktree()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        RunGit(_temporaryDirectory, "init");
        RunGit(_temporaryDirectory, "config", "user.email", "tests@example.invalid");
        RunGit(_temporaryDirectory, "config", "user.name", "CSharpGit Tests");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "README.md"), "test");
        RunGit(_temporaryDirectory, "add", "README.md");
        RunGit(_temporaryDirectory, "commit", "-m", "initial");
        var worktreePath = Path.Combine(Path.GetDirectoryName(_temporaryDirectory)!, $"{Path.GetFileName(_temporaryDirectory)}-worktree");
        RunGit(_temporaryDirectory, "worktree", "add", "-b", "test-worktree", worktreePath);

        var repository = await new GitCliRepositoryService().OpenAsync(worktreePath);

        Assert.True(repository.IsWorktree);
        Assert.Equal(Path.GetFullPath(worktreePath), repository.RepositoryRoot);
    }

    [Fact]
    public async Task ReadsFreshStateAndRepositoryConfiguration()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        RunGit(_temporaryDirectory, "init");
        RunGit(_temporaryDirectory, "config", "user.email", "tests@example.invalid");
        RunGit(_temporaryDirectory, "config", "user.name", "CSharpGit Tests");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "tracked.txt"), "initial");
        RunGit(_temporaryDirectory, "add", "tracked.txt");
        RunGit(_temporaryDirectory, "commit", "-m", "initial");

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_temporaryDirectory);
        var initial = await service.ReadAsync(repository);

        Assert.Empty(initial.Changes);
        Assert.Equal("CSharpGit Tests", initial.LocalConfiguration["user.name"]);
        Assert.NotNull(initial.HeadCommit);

        File.AppendAllText(Path.Combine(_temporaryDirectory, "tracked.txt"), "changed");
        var refreshed = await service.ReadAsync(repository);

        var change = Assert.Single(refreshed.Changes);
        Assert.Equal("tracked.txt", change.Path);
        Assert.Equal('M', change.WorkingTreeStatus);
    }

    [Fact]
    public async Task ReportsMissingGitExecutableClearly()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var service = new GitCliRepositoryService(new GitCliOptions { ExecutablePath = Path.Combine(_temporaryDirectory, "missing-git") });

        var exception = await Assert.ThrowsAsync<CSharpGit.Application.Exceptions.RepositoryOpenException>(
            () => service.OpenAsync(_temporaryDirectory));

        Assert.Contains("not found", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PreservesUnicodePathsCommitMessagesAndGitOutput()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        RunGit(_temporaryDirectory, "init", "-b", "main");
        RunGit(_temporaryDirectory, "config", "user.email", "tests@example.invalid");
        RunGit(_temporaryDirectory, "config", "user.name", "Unicode Tester");
        const string path = "данные-日本語.txt";
        const string message = "Добавить 日本語 data ✓";
        File.WriteAllText(Path.Combine(_temporaryDirectory, path), "Unicode contents ✓\n");
        RunGit(_temporaryDirectory, "add", "--", path);
        RunGit(_temporaryDirectory, "commit", "-m", message);

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_temporaryDirectory);
        var history = await service.ReadHistoryAsync(repository, new CSharpGit.Domain.HistoryQuery(CSharpGit.Domain.HistoryScope.CurrentBranch, null, 0, 10));
        var commit = Assert.Single(history.Rows).Commit;
        var details = await service.ReadCommitAsync(repository, commit.Hash);

        Assert.Equal(message, commit.Subject);
        Assert.Contains(details.Files, file => file.Path == path);
    }

    [Fact]
    public async Task ReadsPagedBranchedAndMergedHistoryWithDetailsAndDiffs()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        RunGit(_temporaryDirectory, "init", "-b", "main");
        RunGit(_temporaryDirectory, "config", "user.email", "tests@example.invalid");
        RunGit(_temporaryDirectory, "config", "user.name", "CSharpGit Tests");
        CommitFile("shared.txt", "base\n", "base");
        RunGit(_temporaryDirectory, "checkout", "-b", "feature");
        CommitFile("feature.txt", "feature\n", "feature work");
        RunGit(_temporaryDirectory, "checkout", "main");
        CommitFile("main.txt", "main\n", "main work");
        RunGit(_temporaryDirectory, "merge", "--no-ff", "feature", "-m", "merge feature");
        File.WriteAllBytes(Path.Combine(_temporaryDirectory, "image.bin"), [0, 1, 2, 0, 255]);
        RunGit(_temporaryDirectory, "add", "image.bin");
        RunGit(_temporaryDirectory, "commit", "-m", "binary asset");
        RunGit(_temporaryDirectory, "checkout", "feature");
        CommitFile("side.txt", "side\n", "side only");
        RunGit(_temporaryDirectory, "checkout", "main");

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_temporaryDirectory);
        var first = await service.ReadHistoryAsync(repository, new CSharpGit.Domain.HistoryQuery(CSharpGit.Domain.HistoryScope.AllReferences, null, 0, 2));
        var second = await service.ReadHistoryAsync(repository, new CSharpGit.Domain.HistoryQuery(CSharpGit.Domain.HistoryScope.AllReferences, null, 2, 20));

        Assert.True(first.HasMore);
        Assert.Equal(2, first.Rows.Count);
        Assert.Contains(first.Rows.Concat(second.Rows), row => row.Commit.Parents.Count == 2 && row.Topology.Edges.Count == 2);
        Assert.Contains(first.Rows.Concat(second.Rows), row => row.Commit.References.Contains("feature"));

        var binaryCommit = first.Rows.Single(row => row.Commit.Subject == "binary asset").Commit;
        var details = await service.ReadCommitAsync(repository, binaryCommit.Hash);
        var binary = Assert.Single(details.Files, file => file.Path == "image.bin");
        Assert.True(binary.IsBinary);
        var diff = await service.ReadDiffAsync(repository, binaryCommit.Hash, binary.Path);
        Assert.True(diff.IsBinary);
        Assert.Empty(diff.Lines);

        var filtered = await service.ReadHistoryAsync(repository, new CSharpGit.Domain.HistoryQuery(CSharpGit.Domain.HistoryScope.CurrentBranch, "feature work", 0, 20));
        Assert.Single(filtered.Rows);
        var allSide = await service.ReadHistoryAsync(repository, new CSharpGit.Domain.HistoryQuery(CSharpGit.Domain.HistoryScope.AllReferences, "side only", 0, 20));
        var currentSide = await service.ReadHistoryAsync(repository, new CSharpGit.Domain.HistoryQuery(CSharpGit.Domain.HistoryScope.CurrentBranch, "side only", 0, 20));
        Assert.Single(allSide.Rows);
        Assert.Empty(currentSide.Rows);

        var textCommit = first.Rows.Concat(second.Rows).Single(row => row.Commit.Subject == "main work").Commit;
        var textDiff = await service.ReadDiffAsync(repository, textCommit.Hash, "main.txt");
        Assert.False(textDiff.IsBinary);
        Assert.Contains(textDiff.Lines, line => line.Kind == CSharpGit.Domain.DiffLineKind.Added);
    }

    [Fact]
    public async Task ManagesIndexCommitsAndWholeFileDiscard()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        RunGit(_temporaryDirectory, "init", "-b", "main");
        RunGit(_temporaryDirectory, "config", "user.email", "tests@example.invalid");
        RunGit(_temporaryDirectory, "config", "user.name", "CSharpGit Tests");
        CommitFile("tracked.txt", "base\n", "base");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "tracked.txt"), "staged\n");

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_temporaryDirectory);
        await service.StageFileAsync(repository, Assert.Single((await service.ReadAsync(repository)).Changes));
        File.AppendAllText(Path.Combine(_temporaryDirectory, "tracked.txt"), "unstaged\n");
        var both = Assert.Single((await service.ReadAsync(repository)).Changes);
        Assert.True(both.IsStaged);
        Assert.True(both.IsUnstaged);

        await service.CommitAsync(repository, "index only");
        Assert.Equal("staged\nunstaged\n", File.ReadAllText(Path.Combine(_temporaryDirectory, "tracked.txt")));
        Assert.Equal("staged", RunGitOutput(_temporaryDirectory, "show", "HEAD:tracked.txt").Trim());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CommitAsync(repository, "empty index"));
        await service.CommitAsync(repository, "intentional", intentionalEmpty: true);

        var unstaged = Assert.Single((await service.ReadAsync(repository)).Changes);
        await service.DiscardFileAsync(repository, unstaged);
        Assert.Equal("staged\n", File.ReadAllText(Path.Combine(_temporaryDirectory, "tracked.txt")));

        File.WriteAllText(Path.Combine(_temporaryDirectory, "new.txt"), "new");
        var untracked = Assert.Single((await service.ReadAsync(repository)).Changes, item => item.Path == "new.txt");
        Assert.Equal(CSharpGit.Domain.FileChangeKind.Untracked, untracked.Kind);
        await service.StageFileAsync(repository, untracked);
        await service.UnstageFileAsync(repository, Assert.Single((await service.ReadAsync(repository)).Changes, item => item.Path == "new.txt"));
        Assert.False(Assert.Single((await service.ReadAsync(repository)).Changes, item => item.Path == "new.txt").IsStaged);
    }

    [Fact]
    public async Task ReadsRefsAndRunsBranchAndRemoteWorkflowsWithoutGuessingUpstream()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        RunGit(_temporaryDirectory, "init", "-b", "main");
        RunGit(_temporaryDirectory, "config", "user.email", "tests@example.invalid");
        RunGit(_temporaryDirectory, "config", "user.name", "CSharpGit Tests");
        CommitFile("tracked.txt", "base\n", "base");
        RunGit(_temporaryDirectory, "tag", "v1");
        var bare = Path.Combine(_temporaryDirectory, ".remote.git");
        RunGit(_temporaryDirectory, "init", "--bare", bare);
        RunGit(_temporaryDirectory, "remote", "add", "origin", bare);

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_temporaryDirectory);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PushAsync(repository));
        await service.PushAsync(repository, "origin", "main", setUpstream: true);
        await service.FetchAllAsync(repository);

        var refs = (await service.ReadAsync(repository)).Refs;
        Assert.Contains(refs.LocalBranches, branch => branch.Name == "main" && branch.IsCurrent && branch.Upstream == "origin/main");
        Assert.Contains(refs.RemoteBranches, branch => branch.Name == "origin/main");
        Assert.Contains(refs.Remotes, remote => remote.Name == "origin" && remote.FetchUrl == bare);
        Assert.Contains(refs.Tags, tag => tag.Name == "v1");

        await service.CreateBranchAsync(repository, "topic");
        Assert.Equal("topic", (await service.ReadAsync(repository)).HeadReference);
        await service.SwitchBranchAsync(repository, "main");
        await service.DeleteBranchAsync(repository, "topic");
        await service.CheckoutAsync(repository, "v1");
        Assert.True((await service.ReadAsync(repository)).IsDetached);
        await service.SwitchBranchAsync(repository, "main");
        await service.PullAsync(repository);

        CommitFile("rejected.txt", "rejected\n", "rejected push");
        var hook = Path.Combine(bare, "hooks", "pre-receive");
        File.WriteAllText(hook, "#!/bin/sh\necho 'Authentication failed by test remote' >&2\nexit 1\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var authenticationFailure = await Assert.ThrowsAsync<CSharpGit.Application.Exceptions.RepositoryOpenException>(
            () => service.PushAsync(repository));
        Assert.Contains("Authentication failed", authenticationFailure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunsRealStashAndClassifiesGitMergeResults()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        RunGit(_temporaryDirectory, "init", "-b", "main");
        RunGit(_temporaryDirectory, "config", "user.email", "tests@example.invalid");
        RunGit(_temporaryDirectory, "config", "user.name", "CSharpGit Tests");
        CommitFile("tracked.txt", "base\n", "base");

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_temporaryDirectory);
        File.WriteAllText(Path.Combine(_temporaryDirectory, "tracked.txt"), "stashed\n");
        await service.CreateStashAsync(repository, "work in progress");
        var stash = Assert.Single((await service.ReadAsync(repository)).Stashes);
        Assert.Equal("stash@{0}", stash.Name);
        Assert.Contains("work in progress", stash.Message);
        await service.ApplyStashAsync(repository, stash.Name);
        Assert.Equal("stashed\n", File.ReadAllText(Path.Combine(_temporaryDirectory, "tracked.txt")));
        RunGit(_temporaryDirectory, "restore", "tracked.txt");
        await service.PopStashAsync(repository, stash.Name);
        Assert.Empty((await service.ReadAsync(repository)).Stashes);
        RunGit(_temporaryDirectory, "restore", "tracked.txt");

        RunGit(_temporaryDirectory, "switch", "-c", "feature");
        CommitFile("feature.txt", "feature\n", "feature");
        RunGit(_temporaryDirectory, "switch", "main");
        var fastForward = await service.MergeAsync(repository, "feature");
        Assert.Equal(CSharpGit.Domain.MergeResultKind.FastForward, fastForward.Kind);

        RunGit(_temporaryDirectory, "switch", "-c", "side");
        CommitFile("side.txt", "side\n", "side");
        RunGit(_temporaryDirectory, "switch", "main");
        CommitFile("main.txt", "main\n", "main");
        var mergeCommit = await service.MergeAsync(repository, "side");
        Assert.Equal(CSharpGit.Domain.MergeResultKind.MergeCommit, mergeCommit.Kind);
        Assert.Equal(2, RunGitOutput(_temporaryDirectory, "show", "-s", "--format=%P", "HEAD").Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);

        RunGit(_temporaryDirectory, "switch", "-c", "conflict");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "tracked.txt"), "conflict side\n");
        RunGit(_temporaryDirectory, "commit", "-am", "conflict side");
        RunGit(_temporaryDirectory, "switch", "main");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "tracked.txt"), "conflict main\n");
        RunGit(_temporaryDirectory, "commit", "-am", "conflict main");
        var conflict = await service.MergeAsync(repository, "conflict");
        Assert.Equal(CSharpGit.Domain.MergeResultKind.Conflicts, conflict.Kind);
        var conflictedState = await service.ReadAsync(repository);
        Assert.Equal(CSharpGit.Domain.RepositoryOperation.Merge, conflictedState.Operation);
        Assert.Contains(conflictedState.Changes, change => change.IsConflicted);
        Assert.True(conflictedState.CurrentOperation.CanContinue);
        Assert.True(conflictedState.CurrentOperation.CanAbort);
        Assert.False(conflictedState.CurrentOperation.CanSkip);
        var conflictFile = Assert.Single(conflictedState.CurrentOperation.Conflicts);
        Assert.Equal(CSharpGit.Domain.ConflictKind.Textual, conflictFile.Kind);
        Assert.Equal("Current/local", conflictFile.CurrentLocalLabel);
        Assert.True(conflictFile.CanChooseCurrentLocal);
        Assert.True(conflictFile.CanChooseIncomingRemote);

        await service.ChooseConflictSideAsync(repository, conflictFile, CSharpGit.Domain.ConflictResolutionSide.IncomingRemote);
        var chosen = Assert.Single((await service.ReadAsync(repository)).CurrentOperation.Conflicts);
        Assert.False(chosen.IsResolved);
        await service.StageResolvedConflictAsync(repository, chosen);
        var resolved = Assert.Single((await service.ReadAsync(repository)).CurrentOperation.Conflicts);
        Assert.True(resolved.IsResolved);
        Assert.False(resolved.CanStage);
        await service.AbortOperationAsync(repository);
        Assert.Equal(CSharpGit.Domain.RepositoryOperation.None, (await service.ReadAsync(repository)).Operation);
    }

    [Fact]
    public async Task RunsInteractiveRebasePlanAndStopsOnConflict()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        RunGit(_temporaryDirectory, "init", "-b", "main");
        RunGit(_temporaryDirectory, "config", "user.email", "tests@example.invalid");
        RunGit(_temporaryDirectory, "config", "user.name", "CSharpGit Tests");
        CommitFile("root.txt", "root\n", "root");
        var baseCommit = RunGitOutput(_temporaryDirectory, "rev-parse", "HEAD").Trim();
        CommitFile("a.txt", "a\n", "A");
        CommitFile("b.txt", "b\n", "B");
        CommitFile("c.txt", "c\n", "C");
        CommitFile("d.txt", "d\n", "D");
        CommitFile("e.txt", "e\n", "E");
        CommitFile("f.txt", "f\n", "F");

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_temporaryDirectory);
        var original = await service.ReadInteractiveRebasePlanAsync(repository, baseCommit);
        Assert.Equal(6, original.Items.Count);
        var bySubject = original.Items.ToDictionary(item => item.Subject);
        var plan = new CSharpGit.Domain.InteractiveRebasePlan(baseCommit,
        [
            bySubject["F"] with { Action = CSharpGit.Domain.RebaseAction.Pick },
            bySubject["A"] with { Action = CSharpGit.Domain.RebaseAction.Pick },
            bySubject["B"] with { Action = CSharpGit.Domain.RebaseAction.Squash },
            bySubject["C"] with { Action = CSharpGit.Domain.RebaseAction.Fixup },
            bySubject["D"] with { Action = CSharpGit.Domain.RebaseAction.Reword, NewMessage = "D edited" },
            bySubject["E"] with { Action = CSharpGit.Domain.RebaseAction.Drop }
        ]);

        var result = await service.StartInteractiveRebaseAsync(repository, plan);

        Assert.Equal(CSharpGit.Domain.RebaseResultKind.Completed, result.Kind);
        Assert.Equal(3, int.Parse(RunGitOutput(_temporaryDirectory, "rev-list", "--count", $"{baseCommit}..HEAD").Trim()));
        Assert.Contains("D edited", RunGitOutput(_temporaryDirectory, "log", "--format=%s", $"{baseCommit}..HEAD"));
        Assert.False(File.Exists(Path.Combine(_temporaryDirectory, "e.txt")));

        RunGit(_temporaryDirectory, "switch", "-c", "conflict-source", baseCommit);
        File.WriteAllText(Path.Combine(_temporaryDirectory, "root.txt"), "source\n");
        RunGit(_temporaryDirectory, "commit", "-am", "source conflict");
        var conflictPlan = await service.ReadInteractiveRebasePlanAsync(repository, baseCommit);
        RunGit(_temporaryDirectory, "switch", "-c", "conflict-target", baseCommit);
        File.WriteAllText(Path.Combine(_temporaryDirectory, "root.txt"), "target\n");
        RunGit(_temporaryDirectory, "commit", "-am", "target conflict");
        RunGit(_temporaryDirectory, "switch", "conflict-source");
        var ontoTarget = RunGitOutput(_temporaryDirectory, "rev-parse", "conflict-target").Trim();
        var conflicting = new CSharpGit.Domain.InteractiveRebasePlan(ontoTarget, conflictPlan.Items);

        var conflict = await service.StartInteractiveRebaseAsync(repository, conflicting);

        Assert.Equal(CSharpGit.Domain.RebaseResultKind.Conflicts, conflict.Kind);
        var rebaseState = await service.ReadAsync(repository);
        Assert.Equal(CSharpGit.Domain.RepositoryOperation.Rebase, rebaseState.Operation);
        Assert.True(rebaseState.CurrentOperation.CanSkip);
        var rebaseConflict = Assert.Single(rebaseState.CurrentOperation.Conflicts);
        Assert.Contains("replayed commit", rebaseConflict.CurrentLocalLabel);
        Assert.Contains("rebase base", rebaseConflict.IncomingRemoteLabel);
        var reopened = await new GitCliRepositoryService().OpenAsync(_temporaryDirectory);
        Assert.Equal(CSharpGit.Domain.RepositoryOperation.Rebase, (await new GitCliRepositoryService().ReadAsync(reopened)).Operation);
        await service.SkipOperationAsync(repository);
        Assert.Equal(CSharpGit.Domain.RepositoryOperation.None, (await service.ReadAsync(repository)).Operation);
    }

    [Fact]
    public async Task ConfiguresPresetAndCustomMergeToolsInRepositoryGitConfig()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        RunGit(_temporaryDirectory, "init", "-b", "main");
        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_temporaryDirectory);

        await service.ConfigureMergeToolAsync(repository, new CSharpGit.Domain.MergeToolConfiguration(
            "meld", CSharpGit.Domain.MergeToolConfigurationKind.Preset,
            CSharpGit.Domain.GitConfigurationScope.RepositoryLocal, "C:/Tools/meld.exe"));

        var preset = await service.ReadAsync(repository);
        Assert.Equal("meld", preset.LocalConfiguration["merge.tool"]);
        Assert.Equal("C:/Tools/meld.exe", preset.LocalConfiguration["mergetool.meld.path"]);
        Assert.Equal("false", preset.LocalConfiguration["mergetool.prompt"]);

        await service.ConfigureMergeToolAsync(repository, new CSharpGit.Domain.MergeToolConfiguration(
            "companytool", CSharpGit.Domain.MergeToolConfigurationKind.CustomExecutable,
            CSharpGit.Domain.GitConfigurationScope.RepositoryLocal, "C:/Program Files/Company/tool.exe",
            "--base \"$BASE\" --local \"$LOCAL\" --remote \"$REMOTE\" --output \"$MERGED\""));

        var custom = await service.ReadAsync(repository);
        Assert.Equal("companytool", custom.LocalConfiguration["merge.tool"]);
        var command = custom.LocalConfiguration["mergetool.companytool.cmd"];
        Assert.Contains("C:/Program Files/Company/tool.exe", command);
        Assert.All(new[] { "$BASE", "$LOCAL", "$REMOTE", "$MERGED" }, variable => Assert.Contains(variable, command));
        Assert.Equal("true", custom.LocalConfiguration["mergetool.companytool.trustexitcode"]);

        await Assert.ThrowsAsync<ArgumentException>(() => service.ConfigureMergeToolAsync(repository,
            new CSharpGit.Domain.MergeToolConfiguration("broken", CSharpGit.Domain.MergeToolConfigurationKind.CustomCommand,
                CSharpGit.Domain.GitConfigurationScope.RepositoryLocal, "tool $LOCAL $REMOTE")));
        Assert.Equal("companytool", (await service.ReadAsync(repository)).LocalConfiguration["merge.tool"]);
    }

    [Fact]
    public async Task RefreshesOrdinaryRepositoryAndLinkedWorktreeAfterExternalChanges()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        RunGit(_temporaryDirectory, "init", "-b", "main");
        ConfigureIdentity(_temporaryDirectory);
        CommitFile("tracked.txt", "base\n", "base");
        var worktreePath = $"{_temporaryDirectory}-worktree";
        RunGit(_temporaryDirectory, "worktree", "add", "-b", "linked", worktreePath);

        var service = new GitCliRepositoryService();
        var main = await service.OpenAsync(_temporaryDirectory);
        var linked = await service.OpenAsync(worktreePath);
        Assert.Empty((await service.ReadAsync(main)).Changes);
        Assert.Empty((await service.ReadAsync(linked)).Changes);

        File.AppendAllText(Path.Combine(_temporaryDirectory, "tracked.txt"), "external main\n");
        File.AppendAllText(Path.Combine(worktreePath, "tracked.txt"), "external linked\n");

        Assert.Equal('M', Assert.Single((await service.ReadAsync(main)).Changes).WorkingTreeStatus);
        Assert.Equal('M', Assert.Single((await service.ReadAsync(linked)).Changes).WorkingTreeStatus);
    }

    [Fact]
    public async Task CoversEveryWorkingTreeStateAndAmendsOnlyTheIndex()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        RunGit(_temporaryDirectory, "init", "-b", "main");
        ConfigureIdentity(_temporaryDirectory);
        CommitFile("modified.txt", "base\n", "base");
        CommitFile("deleted.txt", "delete me\n", "deleted base");
        CommitFile("renamed.txt", "rename me\n", "rename base");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "modified.txt"), "staged\n");
        RunGit(_temporaryDirectory, "add", "modified.txt");
        File.AppendAllText(Path.Combine(_temporaryDirectory, "modified.txt"), "unstaged\n");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "added.txt"), "added\n");
        RunGit(_temporaryDirectory, "add", "added.txt");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "untracked.txt"), "untracked\n");
        File.Delete(Path.Combine(_temporaryDirectory, "deleted.txt"));
        RunGit(_temporaryDirectory, "mv", "renamed.txt", "moved.txt");

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_temporaryDirectory);
        var changes = (await service.ReadAsync(repository)).Changes;
        Assert.Contains(changes, item => item.Kind == CSharpGit.Domain.FileChangeKind.Modified && item.IsStaged && item.IsUnstaged);
        Assert.Contains(changes, item => item.Kind == CSharpGit.Domain.FileChangeKind.Added && item.IsStaged);
        Assert.Contains(changes, item => item.Kind == CSharpGit.Domain.FileChangeKind.Untracked && item.IsUnstaged);
        Assert.Contains(changes, item => item.Kind == CSharpGit.Domain.FileChangeKind.Deleted && item.IsUnstaged);
        Assert.Contains(changes, item => item.Kind == CSharpGit.Domain.FileChangeKind.Renamed && item.IsStaged && item.OriginalPath == "renamed.txt");

        await service.CommitAsync(repository, "amended", amend: true);
        Assert.Equal("amended", RunGitOutput(_temporaryDirectory, "show", "-s", "--format=%s", "HEAD").Trim());
        Assert.Contains((await service.ReadAsync(repository)).Changes, item => item.Path == "modified.txt" && item.IsUnstaged && !item.IsStaged);
    }

    [Theory]
    [InlineData("add-add", CSharpGit.Domain.ConflictKind.AddAdd)]
    [InlineData("modify-delete", CSharpGit.Domain.ConflictKind.ModifyDelete)]
    [InlineData("delete-modify", CSharpGit.Domain.ConflictKind.DeleteModify)]
    [InlineData("binary", CSharpGit.Domain.ConflictKind.Binary)]
    public async Task ClassifiesDeclaredConflictTypesAndExposesOnlyValidActions(string scenario, CSharpGit.Domain.ConflictKind expected)
    {
        Directory.CreateDirectory(_temporaryDirectory);
        RunGit(_temporaryDirectory, "init", "-b", "main");
        ConfigureIdentity(_temporaryDirectory);
        if (scenario != "add-add")
        {
            if (scenario == "binary") File.WriteAllBytes(Path.Combine(_temporaryDirectory, "file.bin"), [0, 1, 0]);
            else File.WriteAllText(Path.Combine(_temporaryDirectory, "file.txt"), "base\n");
            RunGit(_temporaryDirectory, "add", ".");
            RunGit(_temporaryDirectory, "commit", "-m", "base");
        }
        else CommitFile("root.txt", "root\n", "base");

        RunGit(_temporaryDirectory, "switch", "-c", "incoming");
        ChangeConflictFile(scenario, incoming: true);
        RunGit(_temporaryDirectory, "add", "-A");
        RunGit(_temporaryDirectory, "commit", "-m", "incoming");
        RunGit(_temporaryDirectory, "switch", "main");
        ChangeConflictFile(scenario, incoming: false);
        RunGit(_temporaryDirectory, "add", "-A");
        RunGit(_temporaryDirectory, "commit", "-m", "current");

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_temporaryDirectory);
        Assert.Equal(CSharpGit.Domain.MergeResultKind.Conflicts, (await service.MergeAsync(repository, "incoming")).Kind);
        var conflict = Assert.Single((await service.ReadAsync(repository)).CurrentOperation.Conflicts);
        Assert.Equal(expected, conflict.Kind);
        Assert.Equal(expected is CSharpGit.Domain.ConflictKind.ModifyDelete or CSharpGit.Domain.ConflictKind.DeleteModify, conflict.CanKeepDeletion);
        Assert.Equal(expected != CSharpGit.Domain.ConflictKind.DeleteModify, conflict.CanChooseCurrentLocal);
        Assert.Equal(expected != CSharpGit.Domain.ConflictKind.ModifyDelete, conflict.CanChooseIncomingRemote);
        if (conflict.CanKeepDeletion)
        {
            await service.KeepConflictDeletionAsync(repository, conflict);
            Assert.True(Assert.Single((await service.ReadAsync(repository)).CurrentOperation.Conflicts).IsResolved);
        }
        await service.AbortOperationAsync(repository);
    }

    [Fact]
    public async Task RunsFileAndRepositoryMergetoolScopesAndReportsExternalToolFailure()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        RunGit(_temporaryDirectory, "init", "-b", "main");
        ConfigureIdentity(_temporaryDirectory);
        CommitFile("one.txt", "base\n", "base one");
        CommitFile("two.txt", "base\n", "base two");
        RunGit(_temporaryDirectory, "switch", "-c", "incoming");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "one.txt"), "incoming one\n");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "two.txt"), "incoming two\n");
        RunGit(_temporaryDirectory, "commit", "-am", "incoming");
        RunGit(_temporaryDirectory, "switch", "main");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "one.txt"), "current one\n");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "two.txt"), "current two\n");
        RunGit(_temporaryDirectory, "commit", "-am", "current");

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_temporaryDirectory);
        await service.ConfigureMergeToolAsync(repository, new CSharpGit.Domain.MergeToolConfiguration(
            "testtool", CSharpGit.Domain.MergeToolConfigurationKind.CustomCommand,
            CSharpGit.Domain.GitConfigurationScope.RepositoryLocal, ": \"$BASE\" \"$LOCAL\"; cp \"$REMOTE\" \"$MERGED\""));
        await service.MergeAsync(repository, "incoming");
        var conflicts = (await service.ReadAsync(repository)).CurrentOperation.Conflicts;
        await service.RunMergeToolForFileAsync(repository, conflicts[0]);
        Assert.Single((await service.ReadAsync(repository)).CurrentOperation.Conflicts, item => !item.IsResolved);
        await service.RunMergeToolWorkflowAsync(repository);
        Assert.All((await service.ReadAsync(repository)).CurrentOperation.Conflicts, item => Assert.True(item.IsResolved));
        await service.ContinueOperationAsync(repository);
        Assert.Equal(CSharpGit.Domain.RepositoryOperation.None, (await service.ReadAsync(repository)).Operation);

        RunGit(_temporaryDirectory, "switch", "-c", "failure", "HEAD~1");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "one.txt"), "failure\n");
        RunGit(_temporaryDirectory, "commit", "-am", "failure");
        RunGit(_temporaryDirectory, "switch", "main");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "one.txt"), "main again\n");
        RunGit(_temporaryDirectory, "commit", "-am", "main again");
        await service.MergeAsync(repository, "failure");
        await service.ConfigureMergeToolAsync(repository, new CSharpGit.Domain.MergeToolConfiguration(
            "broken", CSharpGit.Domain.MergeToolConfigurationKind.CustomCommand,
            CSharpGit.Domain.GitConfigurationScope.RepositoryLocal, ": \"$BASE\" \"$LOCAL\" \"$REMOTE\" \"$MERGED\"; exit 23"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunMergeToolWorkflowAsync(repository));
        Assert.Equal(CSharpGit.Domain.RepositoryOperation.Merge, (await service.ReadAsync(repository)).Operation);
        await service.AbortOperationAsync(repository);
    }

    private void CommitFile(string name, string contents, string message)
    {
        File.WriteAllText(Path.Combine(_temporaryDirectory, name), contents);
        RunGit(_temporaryDirectory, "add", name);
        RunGit(_temporaryDirectory, "commit", "-m", message);
    }

    private static void ConfigureIdentity(string directory)
    {
        RunGit(directory, "config", "user.email", "tests@example.invalid");
        RunGit(directory, "config", "user.name", "CSharpGit Tests");
    }

    private void ChangeConflictFile(string scenario, bool incoming)
    {
        var suffix = incoming ? "incoming" : "current";
        switch (scenario)
        {
            case "add-add": File.WriteAllText(Path.Combine(_temporaryDirectory, "file.txt"), $"{suffix}\n"); break;
            case "modify-delete" when incoming: File.Delete(Path.Combine(_temporaryDirectory, "file.txt")); break;
            case "modify-delete": File.WriteAllText(Path.Combine(_temporaryDirectory, "file.txt"), "current modification\n"); break;
            case "delete-modify" when incoming: File.WriteAllText(Path.Combine(_temporaryDirectory, "file.txt"), "incoming modification\n"); break;
            case "delete-modify": File.Delete(Path.Combine(_temporaryDirectory, "file.txt")); break;
            case "binary": File.WriteAllBytes(Path.Combine(_temporaryDirectory, "file.bin"), incoming ? [0, 2, 0] : [0, 3, 0]); break;
        }
    }

    public void Dispose()
    {
        var worktree = $"{_temporaryDirectory}-worktree";
        if (Directory.Exists(worktree))
        {
            DeleteDirectory(worktree);
        }

        if (Directory.Exists(_temporaryDirectory))
        {
            DeleteDirectory(_temporaryDirectory);
        }
    }

    private static void DeleteDirectory(string path)
    {
        const int attempts = 5;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(path, recursive: true);
                return;
            }
            catch (UnauthorizedAccessException) when (attempt < attempts)
            {
                Thread.Sleep(100 * attempt);
            }
            catch (IOException) when (attempt < attempts)
            {
                Thread.Sleep(100 * attempt);
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
            catch (IOException)
            {
                return;
            }
        }
    }

    private static void RunGit(string directory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git") { WorkingDirectory = directory };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    private static string RunGitOutput(string directory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        return output;
    }
}
