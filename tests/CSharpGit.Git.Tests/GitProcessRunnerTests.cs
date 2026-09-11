using System.Diagnostics;
using CSharpGit.Application;
using CSharpGit.Application.Abstractions;

namespace CSharpGit.Git.Tests;

public sealed class GitProcessRunnerTests
{
    [Fact]
    public async Task CancellationTerminatesLongRunningSubprocessAndCompletesPipeReaders()
    {
        var executable = OperatingSystem.IsWindows()
            ? Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe"
            : "/bin/sh";
        var arguments = OperatingSystem.IsWindows()
            ? new[] { "/d", "/s", "/c", "ping -n 30 127.0.0.1 >nul" }
            : new[] { "-c", "sleep 30" };
        var started = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();

        var task = GitProcessRunner.RunProcessAsync(
            executable,
            Path.GetTempPath(),
            "CancellationTest",
            cancellation.Token,
            arguments,
            processId => started.TrySetResult(processId));
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
            // Process id no longer exists, which is the expected outcome.
        }
    }

    [Fact]
    public async Task RunnerRecordsSuccessfulAndFailedGitCommands()
    {
        var history = new GitCommandActivityHistory();
        var runner = new GitProcessRunner("git", history);

        var output = await runner.RunAsync(
            Path.GetTempPath(),
            "Version",
            GitCommandKind.Internal,
            CancellationToken.None,
            "--version");

        Assert.Contains("git version", output, StringComparison.OrdinalIgnoreCase);
        var success = history.GetLatest(GitCommandFilter.AllCommands);
        Assert.NotNull(success);
        Assert.Equal(GitCommandStatus.Succeeded, success.Status);
        Assert.Equal(0, success.ExitCode);
        Assert.Contains("git --version", success.DisplayCommand, StringComparison.Ordinal);

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(
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
        Assert.False(string.IsNullOrWhiteSpace(failure.StandardError));
    }

    [Fact]
    public async Task RunnerMarksCancelledCommandAsCancelled()
    {
        var executable = OperatingSystem.IsWindows()
            ? Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe"
            : "/bin/sh";
        var arguments = OperatingSystem.IsWindows()
            ? new[] { "/d", "/s", "/c", "ping -n 30 127.0.0.1 >nul" }
            : new[] { "-c", "sleep 30" };
        var history = new GitCommandActivityHistory();
        var runner = new GitProcessRunner(executable, history);
        using var cancellation = new CancellationTokenSource();

        var task = runner.RunAsync(
            Path.GetTempPath(),
            "CancellationActivity",
            GitCommandKind.User,
            cancellation.Token,
            arguments);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (history.GetLatest(GitCommandFilter.AllCommands) is null && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        Assert.NotNull(history.GetLatest(GitCommandFilter.AllCommands));

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);

        var cancelled = history.GetLatest(GitCommandFilter.AllCommands);
        Assert.NotNull(cancelled);
        Assert.Equal(GitCommandStatus.Cancelled, cancelled.Status);
        Assert.NotNull(cancelled.CompletedAt);
    }
}
