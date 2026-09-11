using CSharpGit.Application;
using CSharpGit.Application.Abstractions;
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
            var setupRunner = await InitializeRepositoryAsync(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "note.txt"), "one\n");
            await CommitAllAsync(setupRunner, directory, "initial");
            var parent = await ResolveHeadAsync(setupRunner, directory);

            await File.AppendAllTextAsync(Path.Combine(directory, "note.txt"), "two\n");
            await CommitAllAsync(setupRunner, directory, "modified");
            var commit = await ResolveHeadAsync(setupRunner, directory);

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
            var setupRunner = await InitializeRepositoryAsync(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "old.txt"), "same content\n");
            await CommitAllAsync(setupRunner, directory, "initial");
            var parent = await ResolveHeadAsync(setupRunner, directory);

            await setupRunner.RunAsync(directory, "Setup", CancellationToken.None, "mv", "old.txt", "new.txt");
            await CommitAllAsync(setupRunner, directory, "rename");
            var commit = await ResolveHeadAsync(setupRunner, directory);

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
        var options = new GitCliOptions();
        return new GitFileAwareHistoryService(
            new GitReferenceHistoryService(options),
            new GitRepositoryFileVersionService(options),
            options,
            activity);
    }

    private static Repository CreateRepository(string directory) =>
        new(directory, directory, Path.Combine(directory, ".git"), false);

    private static async Task<GitProcessRunner> InitializeRepositoryAsync(string directory)
    {
        var runner = new GitProcessRunner("git", new GitCommandActivityHistory());
        await runner.RunAsync(directory, "Setup", CancellationToken.None, "init");
        await runner.RunAsync(directory, "Setup", CancellationToken.None, "config", "user.email", "tests@csharpgit.local");
        await runner.RunAsync(directory, "Setup", CancellationToken.None, "config", "user.name", "CSharpGit Tests");
        return runner;
    }

    private static async Task CommitAllAsync(GitProcessRunner runner, string directory, string message)
    {
        await runner.RunAsync(directory, "Setup", CancellationToken.None, "add", "--all");
        await runner.RunAsync(directory, "Setup", CancellationToken.None, "commit", "-m", message);
    }

    private static Task<string> ResolveHeadAsync(GitProcessRunner runner, string directory) =>
        runner.RunAsync(directory, "Setup", CancellationToken.None, "rev-parse", "HEAD");

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"csharpgit-diff-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
