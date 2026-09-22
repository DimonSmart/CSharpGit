using System.Diagnostics;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git.Tests;

public sealed class RepositoryMaintenanceServiceTests : IDisposable
{
    private readonly string _temporaryDirectory =
        Path.Combine(Path.GetTempPath(), $"csharpgit-gc-{Guid.NewGuid():N}");

    [Fact]
    public void ParsesCountObjectsOutputWithoutSyntheticObjectCount()
    {
        const string output = """
            size-pack: 200
            alternate: C:/some/object/database
            count: 10
            unknown-field: 123
            packs: 2
            in-pack: 90
            size: 100
            prune-packable: 3
            garbage: 0
            size-garbage: 0
            """;

        var statistics = GitRepositoryMaintenanceService.ParseStorageStatistics(output);

        Assert.Equal(10, statistics.LooseObjectCount);
        Assert.Equal(90, statistics.PackedObjectCount);
        Assert.Equal(2, statistics.PackCount);
        Assert.Equal(100 * 1024L, statistics.LooseObjectsSizeBytes);
        Assert.Equal(200 * 1024L, statistics.PackedObjectsSizeBytes);
        Assert.Equal(300 * 1024L, statistics.ObjectStorageSizeBytes);
        Assert.Null(typeof(RepositoryStorageStatistics).GetProperty("ObjectCount"));
    }

    [Fact]
    public void ParserIgnoresOrderWhitespaceAndUnknownFields()
    {
        const string output = """
              packs: 4
            size-pack: 9
            future-field: anything
            in-pack: 7
              size: 5
            count: 3
            alternate: D:/objects:with:colons
            """;

        var statistics = GitRepositoryMaintenanceService.ParseStorageStatistics(output);

        Assert.Equal(3, statistics.LooseObjectCount);
        Assert.Equal(7, statistics.PackedObjectCount);
        Assert.Equal(4, statistics.PackCount);
        Assert.Equal(5 * 1024L, statistics.LooseObjectsSizeBytes);
        Assert.Equal(9 * 1024L, statistics.PackedObjectsSizeBytes);
    }

    [Theory]
    [InlineData("""
        count: nope
        size: 1
        in-pack: 1
        packs: 1
        size-pack: 1
        """)]
    [InlineData("""
        count: -1
        size: 1
        in-pack: 1
        packs: 1
        size-pack: 1
        """)]
    [InlineData("""
        count: 1
        size: 1
        in-pack: 1
        packs: 1
        """)]
    [InlineData("""
        count: 1
        size: 9007199254740992
        in-pack: 1
        packs: 1
        size-pack: 1
        """)]
    public void ParserRejectsMalformedMissingNegativeAndOverflowValues(string output)
    {
        Assert.Throws<InvalidOperationException>(
            () => GitRepositoryMaintenanceService.ParseStorageStatistics(output));
    }

    [Fact]
    public void GcArgumentsAreTypedAndDeterministic()
    {
        Assert.Equal(
            ["gc"],
            GitRepositoryMaintenanceService.BuildGarbageCollectArguments(new RepositoryGcOptions()));
        Assert.Equal(
            ["gc", "--aggressive"],
            GitRepositoryMaintenanceService.BuildGarbageCollectArguments(
                new RepositoryGcOptions(Aggressive: true)));
        Assert.Equal(
            ["gc", "--prune=now"],
            GitRepositoryMaintenanceService.BuildGarbageCollectArguments(
                new RepositoryGcOptions(PruneNow: true)));
        Assert.Equal(
            ["gc", "--keep-largest-pack"],
            GitRepositoryMaintenanceService.BuildGarbageCollectArguments(
                new RepositoryGcOptions(KeepLargestPack: true)));
        Assert.Equal(
            ["gc", "--aggressive", "--prune=now", "--keep-largest-pack"],
            GitRepositoryMaintenanceService.BuildGarbageCollectArguments(
                new RepositoryGcOptions(
                    Aggressive: true,
                    PruneNow: true,
                    KeepLargestPack: true)));
    }

    [Fact]
    public async Task StatisticsAreInternalAndGcIsUserCommand()
    {
        var repository = await CreateRepositoryAsync(Path.Combine(_temporaryDirectory, "commands"));
        var sink = new RecordingActivitySink();
        var executor = new GitCommandExecutor(new GitCliOptions(), sink);
        var service = new GitRepositoryMaintenanceService(executor);

        _ = await service.GetStorageStatisticsAsync(repository);
        await service.GarbageCollectAsync(repository, new RepositoryGcOptions());

        Assert.Contains(
            sink.Commands,
            command => command.Kind == GitCommandKind.Internal
                       && command.Arguments.SequenceEqual(["count-objects", "-v"]));
        Assert.Contains(
            sink.Commands,
            command => command.Kind == GitCommandKind.User
                       && command.Arguments.SequenceEqual(["gc"]));
    }

    [Fact]
    public async Task OrdinaryGcPreservesHeadAndTrackedWorkingTree()
    {
        var path = Path.Combine(_temporaryDirectory, "ordinary");
        var repository = await CreateRepositoryAsync(path);
        WriteFile(path, "data.txt", "v1\n");
        RunGit(path, "add", ".");
        RunGit(path, "commit", "-m", "add data");
        WriteFile(path, "data.txt", "v2\n");
        RunGit(path, "add", ".");
        RunGit(path, "commit", "-m", "update data");

        var service = GitTestServices.CreateRepositoryMaintenanceService();
        var before = await service.GetStorageStatisticsAsync(repository);
        var head = RunGitOutput(path, "rev-parse", "HEAD");
        var trackedContent = File.ReadAllText(Path.Combine(path, "data.txt"));

        await service.GarbageCollectAsync(repository, new RepositoryGcOptions());
        var after = await service.GetStorageStatisticsAsync(repository);

        Assert.Equal(head, RunGitOutput(path, "rev-parse", "HEAD"));
        Assert.Equal(trackedContent, File.ReadAllText(Path.Combine(path, "data.txt")));
        Assert.Empty(RunGitOutput(path, "status", "--porcelain=v1"));
        Assert.True(before.ObjectStorageSizeBytes >= 0);
        Assert.True(after.ObjectStorageSizeBytes >= 0);
    }

    [Fact]
    public async Task GcWorksFromLinkedWorktree()
    {
        var primaryPath = Path.Combine(_temporaryDirectory, "primary");
        var linkedPath = Path.Combine(_temporaryDirectory, "linked");
        _ = await CreateRepositoryAsync(primaryPath);
        WriteFile(primaryPath, "data.txt", "worktree\n");
        RunGit(primaryPath, "add", ".");
        RunGit(primaryPath, "commit", "-m", "base");
        RunGit(primaryPath, "worktree", "add", "-b", "linked", linkedPath);

        var repository = await GitTestServices.CreateRepositoryService().OpenAsync(linkedPath);
        Assert.True(repository.IsWorktree);

        var service = GitTestServices.CreateRepositoryMaintenanceService();
        _ = await service.GetStorageStatisticsAsync(repository);
        await service.GarbageCollectAsync(repository, new RepositoryGcOptions());
        _ = await service.GetStorageStatisticsAsync(repository);

        Assert.Empty(RunGitOutput(linkedPath, "status", "--porcelain=v1"));
        Assert.False(string.IsNullOrWhiteSpace(RunGitOutput(linkedPath, "rev-parse", "HEAD")));
    }

    private async Task<Repository> CreateRepositoryAsync(string path)
    {
        Directory.CreateDirectory(path);
        RunGit(path, "init", "-b", "main");
        RunGit(path, "config", "user.email", "tests@example.invalid");
        RunGit(path, "config", "user.name", "CSharpGit Tests");
        return await GitTestServices.CreateRepositoryService().OpenAsync(path);
    }

    private static void WriteFile(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string RunGitOutput(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(
            process.ExitCode == 0,
            $"git {string.Join(' ', arguments)} failed: {output}\n{error}");
        return output.Trim();
    }

    private static void RunGit(string workingDirectory, params string[] arguments) =>
        _ = RunGitOutput(workingDirectory, arguments);

    public void Dispose() => TestDirectory.Delete(_temporaryDirectory);

    private sealed record RecordedCommand(
        IReadOnlyList<string> Arguments,
        GitCommandKind Kind);

    private sealed class RecordingActivitySink : IGitCommandActivitySink
    {
        public List<RecordedCommand> Commands { get; } = [];

        public Guid Started(
            string executable,
            string workingDirectory,
            IReadOnlyList<string> arguments,
            GitCommandKind commandKind)
        {
            Commands.Add(new RecordedCommand([.. arguments], commandKind));
            return Guid.NewGuid();
        }

        public void OutputReceived(Guid id, GitOutputStream stream, string chunk)
        {
        }

        public void Completed(Guid id, int exitCode)
        {
        }

        public void Cancelled(Guid id, int? exitCode)
        {
        }
    }
}
