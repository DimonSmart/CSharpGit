using System.Diagnostics;
using CSharpGit.Domain;
using CSharpGit.Git;

namespace CSharpGit.Git.Tests;

public sealed class TagServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"csharpgit-tags-{Guid.NewGuid():N}");

    [Fact]
    public async Task ReadsLightweightAndAnnotatedTagsWithPeeledMetadata()
    {
        var repositoryPath = CreateRepository();
        var first = Commit(repositoryPath, "first.txt", "one", "First");
        RunGit(repositoryPath, "tag", "light/β", first);
        RunGit(repositoryPath, "tag", "-a", "v1.0.0", first, "-m", "Release one\n\nDetails");

        var service = new GitTagService();
        var repository = await new GitCliRepositoryService().OpenAsync(repositoryPath);
        var tags = await service.ReadTagsAsync(repository);

        var lightweight = Assert.Single(tags, tag => tag.Name == "light/β");
        Assert.Equal(GitTagKind.Lightweight, lightweight.Kind);
        Assert.Equal(first, lightweight.TargetCommit);
        Assert.Null(lightweight.TagObjectId);
        Assert.Null(lightweight.TaggedAt);
        Assert.Null(lightweight.Message);

        var annotated = Assert.Single(tags, tag => tag.Name == "v1.0.0");
        Assert.Equal(GitTagKind.Annotated, annotated.Kind);
        Assert.Equal(first, annotated.TargetCommit);
        Assert.NotEqual(first, annotated.TagObjectId);
        Assert.Equal("CSharpGit Tests", annotated.TaggerName);
        Assert.Equal("tests@example.invalid", annotated.TaggerEmail);
        Assert.NotNull(annotated.TaggedAt);
        Assert.Contains("Release one", annotated.Message);
        Assert.Contains("Details", annotated.Message);

        var badges = new GitReferences([], [], [], tags).ByCommit[first];
        Assert.Contains("tag: light/β", badges);
        Assert.Contains("tag: v1.0.0", badges);
    }

    [Fact]
    public async Task UsesGitVersionOrderingByDefault()
    {
        var path = CreateRepository();
        Commit(path, "a.txt", "a", "A");
        foreach (var tag in new[] { "v1.2.0", "v1.9.0", "v1.10.0", "v1.12.0", "v2.0.0" }) RunGit(path, "tag", tag);

        var tags = await ReadTagsAsync(path);

        Assert.Equal(new[] { "v2.0.0", "v1.12.0", "v1.10.0", "v1.9.0", "v1.2.0" }, tags.Select(tag => tag.Name));
    }

    [Fact]
    public async Task RespectsVersionSortSuffixAndExplicitTagSort()
    {
        var path = CreateRepository();
        Commit(path, "a.txt", "a", "A");
        foreach (var tag in new[] { "v1.0", "v1.0-rc1", "v1.0-rc2", "v1.0.1" }) RunGit(path, "tag", tag);
        RunGit(path, "config", "versionsort.suffix", "-rc");

        var service = new GitTagService();
        var repository = await new GitCliRepositoryService().OpenAsync(path);
        var suffixSorted = await service.ReadTagsAsync(repository);
        Assert.Equal(new[] { "v1.0.1", "v1.0", "v1.0-rc2", "v1.0-rc1" }, suffixSorted.Select(tag => tag.Name));

        RunGit(path, "config", "tag.sort", "version:refname");
        var configured = await service.ReadTagsAsync(repository);
        Assert.Equal(new[] { "v1.0-rc1", "v1.0-rc2", "v1.0", "v1.0.1" }, configured.Select(tag => tag.Name));
    }

    [Fact]
    public async Task CreatesTagsAtHeadAndHistoricalCommitAndRejectsInvalidOrDuplicateNames()
    {
        var path = CreateRepository();
        var first = Commit(path, "first.txt", "one", "First");
        var second = Commit(path, "second.txt", "two", "Second");
        var service = new GitTagService();
        var repository = await new GitCliRepositoryService().OpenAsync(path);

        await service.CreateTagAsync(repository, new CreateTagRequest("light/history", first, GitTagKind.Lightweight));
        await service.CreateTagAsync(repository, new CreateTagRequest("v2.0.0", "HEAD", GitTagKind.Annotated, "Release two"));

        var tags = await service.ReadTagsAsync(repository);
        Assert.Equal(first, Assert.Single(tags, tag => tag.Name == "light/history").TargetCommit);
        var annotated = Assert.Single(tags, tag => tag.Name == "v2.0.0");
        Assert.Equal(second, annotated.TargetCommit);
        Assert.Equal(GitTagKind.Annotated, annotated.Kind);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateTagAsync(repository, new CreateTagRequest("v2.0.0", first, GitTagKind.Lightweight)));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateTagAsync(repository, new CreateTagRequest("bad..tag", first, GitTagKind.Lightweight)));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateTagAsync(repository, new CreateTagRequest("annotated-empty", first, GitTagKind.Annotated, "   ")));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateTagAsync(repository, new CreateTagRequest("missing-target", "deadbeef", GitTagKind.Lightweight)));
    }

    [Fact]
    public async Task DeletesLocalTagsWithoutChangingCommitIndexOrWorkingTree()
    {
        var path = CreateRepository();
        var head = Commit(path, "tracked.txt", "base", "Base");
        File.AppendAllText(Path.Combine(path, "tracked.txt"), "\nworking");
        RunGit(path, "add", "tracked.txt");
        File.AppendAllText(Path.Combine(path, "tracked.txt"), "\nunstaged");
        RunGit(path, "tag", "light");
        RunGit(path, "tag", "-a", "annotated", "-m", "Annotated");
        var before = Git(path, "status", "--porcelain=v1");

        var service = new GitTagService();
        var repository = await new GitCliRepositoryService().OpenAsync(path);
        await service.DeleteTagAsync(repository, "light");
        await service.DeleteTagAsync(repository, "annotated");

        Assert.Equal(head, Git(path, "rev-parse", "HEAD"));
        Assert.Equal(before, Git(path, "status", "--porcelain=v1"));
        Assert.DoesNotContain("light", Git(path, "tag", "--list").Split('\n'));
        Assert.DoesNotContain("annotated", Git(path, "tag", "--list").Split('\n'));
    }

    [Fact]
    public async Task PushesOneTagReportsAlreadyUpToDateAndForceUpdatesOnlyConfirmedConflict()
    {
        var (path, bare) = CreateRepositoryWithRemote();
        var first = Commit(path, "first.txt", "one", "First");
        var service = new GitTagService();
        var repository = await new GitCliRepositoryService().OpenAsync(path);
        await service.CreateTagAsync(repository, new CreateTagRequest("release", first, GitTagKind.Lightweight));

        var pushed = await service.PushTagAsync(repository, "origin", "release");
        Assert.Equal(PushTagResultKind.Pushed, pushed.Kind);
        var already = await service.PushTagAsync(repository, "origin", "release");
        Assert.Equal(PushTagResultKind.AlreadyUpToDate, already.Kind);

        var second = Commit(path, "second.txt", "two", "Second");
        RunGit(path, "tag", "--force", "release", second);
        var conflict = await service.PushTagAsync(repository, "origin", "release");
        Assert.Equal(PushTagResultKind.Conflict, conflict.Kind);
        var snapshot = Assert.IsType<RemoteTagConflictSnapshot>(conflict.Conflict);
        Assert.Equal(first, snapshot.CurrentRemoteTarget);
        Assert.Equal(second, snapshot.NewLocalTarget);

        await service.ForceUpdateRemoteTagAsync(repository, snapshot);
        Assert.Equal(second, GitBare(bare, "rev-parse", "refs/tags/release^{commit}"));
    }

    [Fact]
    public async Task RefusesForceUpdateWhenRemoteMovedAfterConfirmation()
    {
        var (path, bare) = CreateRepositoryWithRemote();
        var first = Commit(path, "first.txt", "one", "First");
        var second = Commit(path, "second.txt", "two", "Second");
        var third = Commit(path, "third.txt", "three", "Third");
        RunGit(path, "tag", "release", first);
        RunGit(path, "push", "origin", "refs/tags/release:refs/tags/release");
        RunGit(path, "push", "origin", "refs/heads/main:refs/heads/main");
        RunGit(path, "tag", "--force", "release", second);

        var service = new GitTagService();
        var repository = await new GitCliRepositoryService().OpenAsync(path);
        var conflict = await service.PushTagAsync(repository, "origin", "release");
        var snapshot = Assert.IsType<RemoteTagConflictSnapshot>(conflict.Conflict);

        RunGitBare(bare, "update-ref", "refs/tags/release", third);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ForceUpdateRemoteTagAsync(repository, snapshot));
        Assert.Contains("changed after confirmation", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(third, GitBare(bare, "rev-parse", "refs/tags/release^{commit}"));
    }

    [Fact]
    public async Task PushesAllDeletesRemoteWhileKeepingLocalAndFetchesTags()
    {
        var (path, bare) = CreateRepositoryWithRemote();
        var first = Commit(path, "first.txt", "one", "First");
        RunGit(path, "tag", "one", first);
        RunGit(path, "tag", "two", first);
        var service = new GitTagService();
        var repository = await new GitCliRepositoryService().OpenAsync(path);

        await service.PushAllTagsAsync(repository, "origin");
        Assert.Equal(first, GitBare(bare, "rev-parse", "refs/tags/one^{commit}"));
        Assert.Equal(first, GitBare(bare, "rev-parse", "refs/tags/two^{commit}"));

        await service.DeleteRemoteTagAsync(repository, "origin", "one");
        Assert.Equal(first, Git(path, "rev-parse", "refs/tags/one^{commit}"));
        Assert.False(TryGitBare(bare, out _, "show-ref", "--verify", "refs/tags/one"));

        RunGitBare(bare, "update-ref", "refs/tags/remote-only", first);
        await service.FetchTagsAsync(repository, "origin");
        Assert.Equal(first, Git(path, "rev-parse", "refs/tags/remote-only^{commit}"));
    }

    [Fact]
    public async Task FetchTagCollisionFailsAndPreservesLocalTag()
    {
        var (path, bare) = CreateRepositoryWithRemote();
        var first = Commit(path, "first.txt", "one", "First");
        var second = Commit(path, "second.txt", "two", "Second");
        RunGit(path, "tag", "collision", first);
        RunGit(path, "push", "origin", "refs/heads/main:refs/heads/main");
        RunGitBare(bare, "update-ref", "refs/tags/collision", second);
        var service = new GitTagService();
        var repository = await new GitCliRepositoryService().OpenAsync(path);

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => service.FetchTagsAsync(repository, "origin"));

        Assert.Contains("tag", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(first, Git(path, "rev-parse", "refs/tags/collision^{commit}"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private async Task<IReadOnlyList<GitTag>> ReadTagsAsync(string path)
    {
        var repository = await new GitCliRepositoryService().OpenAsync(path);
        return await new GitTagService().ReadTagsAsync(repository);
    }

    private string CreateRepository()
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        RunGit(path, "init", "-b", "main");
        RunGit(path, "config", "user.name", "CSharpGit Tests");
        RunGit(path, "config", "user.email", "tests@example.invalid");
        return path;
    }

    private (string Repository, string Bare) CreateRepositoryWithRemote()
    {
        var path = CreateRepository();
        var bare = Path.Combine(_root, $"remote-{Guid.NewGuid():N}.git");
        Directory.CreateDirectory(bare);
        RunGit(bare, "init", "--bare");
        RunGit(path, "remote", "add", "origin", bare);
        return (path, bare);
    }

    private static string Commit(string path, string file, string content, string message)
    {
        File.WriteAllText(Path.Combine(path, file), content);
        RunGit(path, "add", file);
        RunGit(path, "commit", "-m", message);
        return Git(path, "rev-parse", "HEAD");
    }

    private static string Git(string workingDirectory, params string[] arguments)
    {
        if (!TryGit(workingDirectory, out var result, arguments)) throw new InvalidOperationException(result);
        return result.Trim();
    }

    private static string GitBare(string bareDirectory, params string[] arguments)
    {
        if (!TryGitBare(bareDirectory, out var result, arguments)) throw new InvalidOperationException(result);
        return result.Trim();
    }

    private static void RunGit(string workingDirectory, params string[] arguments) => _ = Git(workingDirectory, arguments);

    private static void RunGitBare(string bareDirectory, params string[] arguments) => _ = GitBare(bareDirectory, arguments);

    private static bool TryGitBare(string bareDirectory, out string result, params string[] arguments) =>
        TryGit(Path.GetDirectoryName(bareDirectory)!, out result, ["--git-dir", bareDirectory, .. arguments]);

    private static bool TryGit(string workingDirectory, out string result, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Git did not start.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        result = process.ExitCode == 0 ? stdout : $"Git failed ({process.ExitCode}): {stderr}{stdout}";
        return process.ExitCode == 0;
    }
}
