using CSharpGit.Application;
using CSharpGit.Domain;

namespace CSharpGit.Git.Tests;

public sealed class GitFileAwareHistoryServiceDiffTests
{
    [Fact]
    public async Task ModifiedFileDiffDoesNotRepeatRenameOrCopyDetection()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var setupExecutor = await InitializeRepositoryAsync(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "note.txt"), "one\n");
            await CommitAllAsync(setupExecutor, directory, "initial");
            var parent = await ResolveHeadAsync(setupExecutor, directory);

            await File.AppendAllTextAsync(Path.Combine(directory, "note.txt"), "two\n");
            await CommitAllAsync(setupExecutor, directory, "modified");
            var commit = await ResolveHeadAsync(setupExecutor, directory);

            var activity = new GitCommandActivityHistory();
            var service = CreateService(activity);
            var repository = CreateRepository(directory);
            var file = Assert.Single(await service.ReadChangedFilesAsync(repository, commit, parent));
            Assert.Equal("M", file.Status);

            var diff = await service.ReadDiffAsync(repository, commit, parent, file);

            var command = activity.GetLatest(GitCommandFilter.AllCommands);
            Assert.NotNull(command);
            Assert.Contains("git diff --no-ext-diff", command.DisplayCommand, StringComparison.Ordinal);
            Assert.DoesNotContain("--find-renames", command.DisplayCommand, StringComparison.Ordinal);
            Assert.DoesNotContain("--find-copies", command.DisplayCommand, StringComparison.Ordinal);
            Assert.Contains(diff.Lines, line => line.Kind == DiffLineKind.Added && line.Text == "+two");
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task RenameDiffKeepsRenameDetectionAndMetadata()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var setupExecutor = await InitializeRepositoryAsync(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "old.txt"), "same content\n");
            await CommitAllAsync(setupExecutor, directory, "initial");
            var parent = await ResolveHeadAsync(setupExecutor, directory);

            await setupExecutor.ExecuteAsync(directory, "Setup", CancellationToken.None, "mv", "old.txt", "new.txt");
            await CommitAllAsync(setupExecutor, directory, "rename");
            var commit = await ResolveHeadAsync(setupExecutor, directory);

            var activity = new GitCommandActivityHistory();
            var service = CreateService(activity);
            var repository = CreateRepository(directory);
            var file = Assert.Single(await service.ReadChangedFilesAsync(repository, commit, parent));
            Assert.Equal("R", file.Status);
            Assert.Equal("old.txt", file.OriginalPath);
            Assert.Equal("new.txt", file.Path);

            var diff = await service.ReadDiffAsync(repository, commit, parent, file);

            var command = activity.GetLatest(GitCommandFilter.AllCommands);
            Assert.NotNull(command);
            Assert.Contains("--find-renames", command.DisplayCommand, StringComparison.Ordinal);
            Assert.Contains("--find-copies", command.DisplayCommand, StringComparison.Ordinal);
            Assert.Contains(diff.Lines, line => line.Text == "rename from old.txt");
            Assert.Contains(diff.Lines, line => line.Text == "rename to new.txt");
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static GitFileAwareHistoryService CreateService(GitCommandActivityHistory activity)
    {
        var executor = new GitCommandExecutor(new GitCliOptions(), activity);
        return new GitFileAwareHistoryService(
            new GitReferenceHistoryService(executor),
            executor);
    }

    private static Repository CreateRepository(string directory) =>
        new(directory, directory, Path.Combine(directory, ".git"), false);

    private static async Task<GitCommandExecutor> InitializeRepositoryAsync(string directory)
    {
        var executor = new GitCommandExecutor(new GitCliOptions(), new GitCommandActivityHistory());
        await executor.ExecuteAsync(directory, "Setup", CancellationToken.None, "init");
        await executor.ExecuteAsync(directory, "Setup", CancellationToken.None, "config", "user.email", "tests@csharpgit.local");
        await executor.ExecuteAsync(directory, "Setup", CancellationToken.None, "config", "user.name", "CSharpGit Tests");
        return executor;
    }

    private static async Task CommitAllAsync(GitCommandExecutor executor, string directory, string message)
    {
        await executor.ExecuteAsync(directory, "Setup", CancellationToken.None, "add", "--all");
        await executor.ExecuteAsync(directory, "Setup", CancellationToken.None, "commit", "-m", message);
    }

    private static Task<string> ResolveHeadAsync(GitCommandExecutor executor, string directory) =>
        executor.ExecuteAsync(directory, "Setup", CancellationToken.None, "rev-parse", "HEAD");

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"csharpgit-diff-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        try { Directory.Delete(directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
