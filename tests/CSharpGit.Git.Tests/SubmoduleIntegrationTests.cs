using System.Diagnostics;
using CSharpGit.Application;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git.Tests;

[CollectionDefinition("SubmoduleIntegration", DisableParallelization = true)]
public sealed class SubmoduleIntegrationCollection;

[Collection("SubmoduleIntegration")]
public sealed class SubmoduleIntegrationTests : IDisposable
{
    private const string ModulePath = "Shared Library α";
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"csharpgit-submodule-{Guid.NewGuid():N}");
    private readonly string? _previousProtocol;

    public SubmoduleIntegrationTests()
    {
        // Test-process-only permission for local remotes. Production Git settings are untouched.
        _previousProtocol = Environment.GetEnvironmentVariable("GIT_ALLOW_PROTOCOL");
        Environment.SetEnvironmentVariable("GIT_ALLOW_PROTOCOL", "file");
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task RepositoryWithoutSubmodulesReturnsEmptyList()
    {
        var repository = Init("ordinary");
        Assert.Empty(await Service().ReadAsync(await OpenAsync(repository)));
    }

    [Fact]
    public async Task DiscoveryIncludesCleanAndUninitializedGitlinksWithUrlAndRecordedCommit()
    {
        var (parent, child) = CreatePair();
        var clone = Clone(parent, "not-initialized");
        var information = Assert.Single(await Service().ReadAsync(await OpenAsync(clone)));

        Assert.Equal(ModulePath, information.Path);
        Assert.False(information.IsInitialized);
        Assert.Null(information.CurrentCommit);
        Assert.Equal(Head(child), information.RecordedCommit);
        Assert.Equal(child, information.Url);
        Assert.False(information.HasLocalChanges);
    }

    [Fact]
    public async Task InitializeRetrievesRecordedCommitAndAllowsDetachedHead()
    {
        var (parent, child) = CreatePair();
        var clone = Clone(parent, "initializing");
        var repository = await OpenAsync(clone);
        await Service().InitializeAsync(repository, ModulePath);

        var info = Assert.Single(await Service().ReadAsync(repository));
        Assert.True(info.IsInitialized);
        Assert.Equal(Head(child), info.CurrentCommit);
        Assert.Null(info.Branch);
        Assert.True(info.CanOpen);
        Assert.True(File.Exists(Path.Combine(clone, ModulePath, "lib.txt")));
        Assert.EndsWith(ModulePath, (await OpenAsync(Path.Combine(clone, ModulePath))).RepositoryRoot, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DifferentSubmoduleCommitCanBeStagedAndUnstagedWithoutTouchingChild()
    {
        var (parent, _) = CreatePair();
        var clone = Clone(parent, "stage");
        var repository = await OpenAsync(clone);
        var service = Service();
        await service.InitializeAsync(repository, ModulePath);
        var childPath = Path.Combine(clone, ModulePath);
        var extra = AppendChildCommit(childPath, "new commit");
        var status = await GitTestServices.CreateRepositoryStateService().ReadAsync(repository);
        var change = Assert.Single(status.Changes, candidate => candidate.Path == ModulePath);

        Assert.True(change.IsSubmodule);
        Assert.True(change.SubmoduleCommitChanged);
        Assert.True(change.CanStageGitlink);
        var workingTree = GitTestServices.CreateWorkingTreeService();
        await workingTree.StageFileAsync(repository, change);
        Assert.Equal(extra, RunGit(clone, "rev-parse", ":" + ModulePath).Trim());
        Assert.Contains(ModulePath, RunGit(clone, "diff", "--cached", "--name-only"));

        var staged = await GitTestServices.CreateRepositoryStateService().ReadAsync(repository);
        await workingTree.UnstageFileAsync(repository, Assert.Single(staged.Changes, item => item.Path == ModulePath));
        Assert.Equal(Head(parent), Head(clone));
        Assert.Equal(extra, Head(childPath));
        Assert.Equal(Head(Path.Combine(parent, ModulePath)), RunGit(clone, "rev-parse", ":" + ModulePath).Trim());
    }

    [Fact]
    public async Task DirtyOnlySubmoduleCannotBeStagedOrDiscardedFromParent()
    {
        var (parent, _) = CreatePair();
        var clone = Clone(parent, "dirty");
        var repository = await OpenAsync(clone);
        await Service().InitializeAsync(repository, ModulePath);
        var child = Path.Combine(clone, ModulePath);
        var tracked = Path.Combine(child, "lib.txt");
        await File.AppendAllTextAsync(tracked, "uncommitted\n");
        var untracked = Path.Combine(child, "untracked.tmp");
        await File.WriteAllTextAsync(untracked, "private\n");
        var status = await GitTestServices.CreateRepositoryStateService().ReadAsync(repository);
        var change = Assert.Single(status.Changes, candidate => candidate.Path == ModulePath);

        Assert.True(change.IsSubmodule);
        Assert.False(change.SubmoduleCommitChanged);
        Assert.True(change.SubmoduleModifiedContent);
        Assert.True(change.SubmoduleUntrackedContent);
        Assert.False(change.CanStageGitlink);
        Assert.False(WorkingTreeDiscard.IsEligible(change));
        Assert.Null(WorkingTreeDiscard.CreateSelected([change]));
        Assert.Null(WorkingTreeDiscard.CreateAll([change]));
        var worktree = GitTestServices.CreateWorkingTreeService();
        await Assert.ThrowsAsync<InvalidOperationException>(() => worktree.StageFileAsync(repository, change));
        await Assert.ThrowsAsync<InvalidOperationException>(() => worktree.DiscardFileAsync(repository, change));
        await Assert.ThrowsAsync<InvalidOperationException>(() => worktree.DiscardTrackedFilesAsync(repository, [change]));
        Assert.Equal("private\n", await File.ReadAllTextAsync(untracked));
        Assert.Contains("uncommitted", await File.ReadAllTextAsync(tracked));
    }

    [Fact]
    public async Task UpdateRefusesDirtyContentAndKeepsItIntact()
    {
        var (parent, _) = CreatePair();
        var clone = Clone(parent, "refuse-dirty");
        var repository = await OpenAsync(clone);
        var service = Service();
        await service.InitializeAsync(repository, ModulePath);
        var child = Path.Combine(clone, ModulePath);
        var extra = AppendChildCommit(child, "later");
        var file = Path.Combine(child, "lib.txt");
        await File.AppendAllTextAsync(file, "keep-me\n");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UpdateAsync(repository, ModulePath));
        Assert.Contains("commit or stash", error.Message);
        Assert.Equal(extra, Head(child));
        Assert.Contains("keep-me", await File.ReadAllTextAsync(file));
    }

    [Fact]
    public async Task UpdateReturnsCleanCheckoutToRecordedParentCommit()
    {
        var (parent, source) = CreatePair();
        var clone = Clone(parent, "update");
        var repository = await OpenAsync(clone);
        var service = Service();
        await service.InitializeAsync(repository, ModulePath);
        var child = Path.Combine(clone, ModulePath);
        AppendChildCommit(child, "later");

        await service.UpdateAsync(repository, ModulePath);

        var info = Assert.Single(await service.ReadAsync(repository));
        Assert.Equal(Head(source), info.CurrentCommit);
        Assert.False(info.HasCommitChange);
        Assert.Equal("original\n", await File.ReadAllTextAsync(Path.Combine(child, "lib.txt")));
    }

    [Fact]
    public async Task ParentDiscardAllLeavesSubmoduleDirtyFilesUntouched()
    {
        var (parent, _) = CreatePair();
        var clone = Clone(parent, "discard-all");
        var repository = await OpenAsync(clone);
        await Service().InitializeAsync(repository, ModulePath);
        var child = Path.Combine(clone, ModulePath);
        var privateFile = Path.Combine(child, "private.txt");
        await File.WriteAllTextAsync(privateFile, "retained");
        await File.WriteAllTextAsync(Path.Combine(clone, "ordinary.tmp"), "delete-me");
        var status = await GitTestServices.CreateRepositoryStateService().ReadAsync(repository);
        var request = WorkingTreeDiscard.CreateAll(status.Changes);
        Assert.NotNull(request);
        Assert.DoesNotContain(request.Changes, c => c.Path == ModulePath);

        await WorkingTreeDiscard.ExecuteAsync(repository, request,
            (batch, token) => GitTestServices.CreateWorkingTreeService().DiscardTrackedFilesAsync(repository, batch, token),
            (change, token) => GitTestServices.CreateWorkingTreeService().DiscardFileAsync(repository, change, token));
        Assert.True(File.Exists(privateFile));
        Assert.Equal("retained", await File.ReadAllTextAsync(privateFile));
    }

    [Fact]
    public async Task RefreshReadsExternalSubmoduleCommitAndInternalEdits()
    {
        var (parent, _) = CreatePair();
        var clone = Clone(parent, "external");
        var repository = await OpenAsync(clone);
        var modules = Service();
        await modules.InitializeAsync(repository, ModulePath);
        var before = Assert.Single(await modules.ReadAsync(repository));
        Assert.False(before.HasCommitChange);
        var child = Path.Combine(clone, ModulePath);
        AppendChildCommit(child, "changed externally");
        await File.AppendAllTextAsync(Path.Combine(child, "lib.txt"), "external edit\n");

        var after = Assert.Single(await modules.ReadAsync(repository));
        Assert.True(after.HasCommitChange);
        Assert.True(after.ModifiedContent);
        var state = await GitTestServices.CreateRepositoryStateService().ReadAsync(repository);
        Assert.Contains(state.Changes, c => c.IsSubmodule && c.SubmoduleCommitChanged && c.SubmoduleModifiedContent);
    }

    [Fact]
    public async Task RecursiveCloneInitializesSubmoduleOnlyWhenOptedIn()
    {
        var (parent, child) = CreatePair();
        var service = GitTestServices.CreateRepositoryCloneService();
        var ordinary = Path.Combine(_root, "ordinary-clone");
        var recursive = Path.Combine(_root, "recursive-clone");

        await service.CloneAsync(parent, ordinary);
        await service.CloneWithSubmodulesAsync(parent, recursive, true);

        Assert.False(Assert.Single(await Service().ReadAsync(await OpenAsync(ordinary))).IsInitialized);
        var initialized = Assert.Single(await Service().ReadAsync(await OpenAsync(recursive)));
        Assert.True(initialized.IsInitialized);
        Assert.Equal(Head(child), initialized.CurrentCommit);
    }

    [Fact]
    public async Task InvalidRemoteProducesGitDiagnosticWithoutDeletingClone()
    {
        var (parent, _) = CreatePair();
        var clone = Clone(parent, "missing-remote");
        var missing = Path.Combine(_root, "remote-that-does-not-exist");
        RunGit(clone, "config", "-f", ".gitmodules", $"submodule.{ModulePath}.url", missing);
        var repository = await OpenAsync(clone);
        var service = Service();
        await Assert.ThrowsAnyAsync<Exception>(() => service.InitializeAsync(repository, ModulePath));
        Assert.True(Directory.Exists(clone));
        Assert.False(Assert.Single(await service.ReadAsync(repository)).IsInitialized);
    }

    [Fact]
    public async Task HistoricalSnapshotKeepsRecordedGitlinkHashAfterExternalCheckoutChange()
    {
        var (parent, source) = CreatePair();
        var clone = Clone(parent, "historical");
        var repository = await OpenAsync(clone);
        await Service().InitializeAsync(repository, ModulePath);
        var initial = Head(source);
        var changed = AppendChildCommit(Path.Combine(clone, ModulePath), "new");

        var snapshot = await GitTestServices.CreateRepositorySnapshotService().ReadTreeAsync(repository, Head(clone));
        var gitlink = Assert.Single(snapshot, entry => entry.Kind == RepositorySnapshotEntryKind.Submodule);

        Assert.Equal(initial, gitlink.ObjectId);
        Assert.NotEqual(changed, gitlink.ObjectId);
        Assert.Equal(changed, Assert.Single(await Service().ReadAsync(repository)).CurrentCommit);
    }

    [Fact]
    public async Task RecursiveUpdateInitializesNestedSubmodules()
    {
        var nested = Init("nested");
        var source = Init("container");
        RunGit(source, "-c", "protocol.file.allow=always", "submodule", "add", "--", nested, "nested component");
        RunGit(source, "commit", "-am", "Add nested");
        var parent = Init("nested-parent");
        RunGit(parent, "-c", "protocol.file.allow=always", "submodule", "add", "--", source, ModulePath);
        RunGit(parent, "commit", "-am", "Add outer");
        var clone = Clone(parent, "nested-clone");
        var repository = await OpenAsync(clone);

        await Service().UpdateAllAsync(repository);

        Assert.True(File.Exists(Path.Combine(clone, ModulePath, "nested component", "lib.txt")));
        Assert.True(Assert.Single(await Service().ReadAsync(repository)).IsInitialized);
    }

    private GitSubmoduleService Service() =>
        new(new GitRepositoryCommandRunner(GitTestServices.CreateExecutor()));

    private Task<Repository> OpenAsync(string path) => GitTestServices.CreateRepositoryService().OpenAsync(path);

    private (string Parent, string Source) CreatePair()
    {
        var source = Init("source");
        var parent = Init("parent");
        RunGit(parent, "-c", "protocol.file.allow=always", "submodule", "add", "--", source, ModulePath);
        RunGit(parent, "commit", "-am", "Add submodule");
        return (parent, source);
    }

    private string Init(string name)
    {
        var directory = Path.Combine(_root, name);
        Directory.CreateDirectory(directory);
        RunGit(directory, "init", "-b", "main");
        RunGit(directory, "config", "user.email", "submodule-tests@example.invalid");
        RunGit(directory, "config", "user.name", "Submodule Integration Tests");
        File.WriteAllText(Path.Combine(directory, "lib.txt"), "original\n");
        RunGit(directory, "add", "lib.txt");
        RunGit(directory, "commit", "-m", "Initial");
        return directory;
    }

    private string Clone(string parent, string name)
    {
        var directory = Path.Combine(_root, name);
        RunGit(_root, "clone", "--", parent, directory);
        return directory;
    }

    private static string AppendChildCommit(string child, string description)
    {
        RunGit(child, "config", "user.email", "submodule-tests@example.invalid");
        RunGit(child, "config", "user.name", "Submodule Integration Tests");
        File.AppendAllText(Path.Combine(child, "lib.txt"), description + "\n");
        RunGit(child, "add", "lib.txt");
        RunGit(child, "commit", "-m", description);
        return Head(child);
    }

    private static string Head(string path) => RunGit(path, "rev-parse", "HEAD").Trim();

    private static string RunGit(string directory, params string[] args)
    {
        using var process = new Process();
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        info.Environment["GIT_ALLOW_PROTOCOL"] = "file";
        foreach (var arg in args) info.ArgumentList.Add(arg);
        process.StartInfo = info;
        if (!process.Start()) throw new InvalidOperationException("Git did not start.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Git {string.Join(" ", args)} failed: {stderr}");
        return stdout;
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("GIT_ALLOW_PROTOCOL", _previousProtocol);
        TestDirectory.Delete(_root);
    }
}
