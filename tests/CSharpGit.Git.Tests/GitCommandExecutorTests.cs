using System.Diagnostics;
using CSharpGit.Application;
using CSharpGit.Application.Abstractions;

namespace CSharpGit.Git.Tests;

public sealed class GitCommandExecutorTests
{
    [Fact]
    public async Task CheckedExecutionReturnsOutput()
    {
        var executor = CreateExecutor();

        var output = await executor.ExecuteAsync(
            Path.GetTempPath(),
            "Version",
            CancellationToken.None,
            "--version");

        Assert.Contains("git version", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckedExecutionThrowsForNonZeroExitCode()
    {
        var executor = CreateExecutor();

        await Assert.ThrowsAsync<GitCommandExecutionException>(() => executor.ExecuteAsync(
            Path.GetTempPath(),
            "Failure",
            CancellationToken.None,
            "definitely-not-a-csharpgit-command"));
    }

    [Fact]
    public async Task RawExecutionReturnsNonZeroExitCodeWithoutThrowing()
    {
        var executor = CreateExecutor();

        var result = await executor.ExecuteForResultAsync(
            Path.GetTempPath(),
            "RawFailure",
            GitCommandKind.Internal,
            CancellationToken.None,
            null,
            ["definitely-not-a-csharpgit-command"]);

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.StandardError));
    }

    [Fact]
    public async Task ExecutorRecordsSuccessFailureAndCommandKind()
    {
        var history = new GitCommandActivityHistory();
        var executor = CreateExecutor(history);

        await executor.ExecuteAsync(
            Path.GetTempPath(),
            "Version",
            GitCommandKind.Internal,
            CancellationToken.None,
            "--version");

        var success = history.GetLatest(GitCommandFilter.AllCommands);
        Assert.NotNull(success);
        Assert.Equal(GitCommandStatus.Succeeded, success.Status);
        Assert.Equal(GitCommandKind.Internal, success.CommandKind);
        Assert.Equal(0, success.ExitCode);

        await Assert.ThrowsAsync<GitCommandExecutionException>(() => executor.ExecuteAsync(
            Path.GetTempPath(),
            "Failure",
            GitCommandKind.User,
            CancellationToken.None,
            "definitely-not-a-csharpgit-command"));

        var failure = history.GetLatest(GitCommandFilter.AllCommands);
        Assert.NotNull(failure);
        Assert.Equal(GitCommandKind.User, failure.CommandKind);
        Assert.Equal(GitCommandStatus.Failed, failure.Status);
        Assert.NotEqual(0, failure.ExitCode);
    }

    [Fact]
    public async Task CancellationTerminatesProcessTreeDrainsPipesAndMarksActivityCancelled()
    {
        var executable = OperatingSystem.IsWindows()
            ? Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe"
            : "/bin/sh";
        var arguments = OperatingSystem.IsWindows()
            ? new[] { "/d", "/s", "/c", "ping -n 30 127.0.0.1 >nul" }
            : new[] { "-c", "sleep 30" };
        var started = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var history = new GitCommandActivityHistory();
        var executor = new GitCommandExecutor(
            new GitCliOptions { ExecutablePath = executable },
            history,
            processId => started.TrySetResult(processId));
        using var cancellation = new CancellationTokenSource();

        var task = executor.ExecuteAsync(
            Path.GetTempPath(),
            "Cancellation",
            GitCommandKind.User,
            cancellation.Token,
            arguments);
        var processId = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        var completion = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(8)));
        Assert.Same(task, completion);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);

        try
        {
            using var process = Process.GetProcessById(processId);
            Assert.True(process.HasExited, "Cancelled subprocess is still running.");
        }
        catch (ArgumentException)
        {
        }

        var activity = history.GetLatest(GitCommandFilter.AllCommands);
        Assert.NotNull(activity);
        Assert.Equal(GitCommandStatus.Cancelled, activity.Status);
        Assert.NotNull(activity.CompletedAt);
    }

    [Fact]
    public async Task CustomEnvironmentOverridesDefaultGitEditor()
    {
        var executor = CreateExecutor();
        const string expected = "csharpgit-custom-editor";

        var result = await executor.ExecuteForResultAsync(
            Path.GetTempPath(),
            "Editor",
            GitCommandKind.Internal,
            CancellationToken.None,
            new Dictionary<string, string?> { ["GIT_EDITOR"] = expected },
            ["var", "GIT_EDITOR"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, result.StandardOutput);
    }

    [Fact]
    public async Task MachineReadableOutputPreservesLeadingWhitespace()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var executor = CreateExecutor();
            await executor.ExecuteAsync(directory, "Setup", CancellationToken.None, "init");
            await executor.ExecuteAsync(directory, "Setup", CancellationToken.None, "config", "user.email", "tests@csharpgit.local");
            await executor.ExecuteAsync(directory, "Setup", CancellationToken.None, "config", "user.name", "CSharpGit Tests");
            await File.WriteAllTextAsync(Path.Combine(directory, "note.txt"), "one\n");
            await executor.ExecuteAsync(directory, "Setup", CancellationToken.None, "add", "note.txt");
            await executor.ExecuteAsync(directory, "Setup", CancellationToken.None, "commit", "-m", "initial");
            await File.AppendAllTextAsync(Path.Combine(directory, "note.txt"), "two\n");

            var output = await executor.ExecuteAsync(
                directory,
                "Status",
                CancellationToken.None,
                "status", "--porcelain=v1", "--untracked-files=all");

            Assert.StartsWith(" M note.txt", output, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task ExecuteToFileWritesBinaryOutputWithoutStringConversion()
    {
        var directory = CreateTemporaryDirectory();
        var destination = Path.Combine(directory, "blob.bin");
        try
        {
            var executor = CreateExecutor();
            await executor.ExecuteAsync(directory, "Setup", CancellationToken.None, "init");
            var source = Path.Combine(directory, "source.bin");
            var bytes = new byte[] { 0, 1, 2, 10, 13, 128, 200, 255 };
            await File.WriteAllBytesAsync(source, bytes);
            var blob = await executor.ExecuteAsync(
                directory,
                "Hash",
                CancellationToken.None,
                "hash-object", "-w", source);

            await executor.ExecuteToFileAsync(
                directory,
                "Blob",
                CancellationToken.None,
                destination,
                "cat-file", "blob", blob);

            Assert.Equal(bytes, await File.ReadAllBytesAsync(destination));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static GitCommandExecutor CreateExecutor(IGitCommandActivitySink? sink = null) =>
        new(new GitCliOptions(), sink);

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"csharpgit-executor-tests-{Guid.NewGuid():N}");
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
