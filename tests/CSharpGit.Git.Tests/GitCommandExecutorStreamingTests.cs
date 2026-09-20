using CSharpGit.Application;
using CSharpGit.Application.Abstractions;

namespace CSharpGit.Git.Tests;

public sealed class GitCommandExecutorStreamingTests
{
    [Fact]
    public async Task FirstOutputEventArrivesBeforeProcessCompletes()
    {
        var history = new GitCommandActivityHistory();
        var firstOutput = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        history.Changed += (_, args) =>
        {
            if (args.ChangeKind == GitCommandActivityChangeKind.Output &&
                args.OutputStream == GitOutputStream.StandardOutput &&
                args.OutputChunk?.Contains("early", StringComparison.Ordinal) == true)
                firstOutput.TrySetResult(true);
        };
        var executor = CreateShellExecutor(history);

        var task = executor.ExecuteForResultAsync(
            Path.GetTempPath(),
            "Streaming",
            GitCommandKind.Internal,
            CancellationToken.None,
            null,
            DelayedShellArguments("echo early", "echo late", 2));

        await firstOutput.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(task.IsCompleted);
        var result = await task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains("early", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("late", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StdoutAndStderrArePumpedConcurrently()
    {
        var history = new GitCommandActivityHistory();
        var stdout = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stderr = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        history.Changed += (_, args) =>
        {
            if (args.ChangeKind != GitCommandActivityChangeKind.Output) return;
            if (args.OutputStream == GitOutputStream.StandardOutput &&
                args.OutputChunk?.Contains("out", StringComparison.Ordinal) == true)
                stdout.TrySetResult(true);
            if (args.OutputStream == GitOutputStream.StandardError &&
                args.OutputChunk?.Contains("err", StringComparison.Ordinal) == true)
                stderr.TrySetResult(true);
        };
        var executor = CreateShellExecutor(history);

        var task = executor.ExecuteForResultAsync(
            Path.GetTempPath(),
            "DualStream",
            GitCommandKind.Internal,
            CancellationToken.None,
            null,
            CommandsThenDelayShellArguments("echo out", "echo err 1>&2", 2));

        await Task.WhenAll(
            stdout.Task.WaitAsync(TimeSpan.FromSeconds(5)),
            stderr.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(task.IsCompleted);
        var result = await task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains("out", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("err", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResultRemainsCompleteWhenHistoryIsTruncated()
    {
        var history = new GitCommandActivityHistory();
        var executor = CreateShellExecutor(history);

        var result = await executor.ExecuteForResultAsync(
            Path.GetTempPath(),
            "LargeOutput",
            GitCommandKind.Internal,
            CancellationToken.None,
            null,
            LargeOutputShellArguments());

        Assert.True(result.StandardOutput.Length > GitCommandActivityHistory.MaximumOutputBytes);
        var activity = history.GetLatest(GitCommandFilter.AllCommands);
        Assert.NotNull(activity);
        Assert.True(activity.StandardOutputTruncated);
        Assert.True(activity.StandardOutput.Length < result.StandardOutput.Length);
        Assert.Contains("[output truncated]", activity.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellationPreservesPartialStreamingOutput()
    {
        var history = new GitCommandActivityHistory();
        var firstOutput = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        history.Changed += (_, args) =>
        {
            if (args.ChangeKind == GitCommandActivityChangeKind.Output &&
                args.OutputChunk?.Contains("partial", StringComparison.Ordinal) == true)
                firstOutput.TrySetResult(true);
        };
        var executor = CreateShellExecutor(history);
        using var cancellation = new CancellationTokenSource();

        var task = executor.ExecuteForResultAsync(
            Path.GetTempPath(),
            "Cancellation",
            GitCommandKind.Internal,
            cancellation.Token,
            null,
            DelayedShellArguments("echo partial", "echo never", 30));

        await firstOutput.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await task.WaitAsync(TimeSpan.FromSeconds(8)));
        var activity = history.GetLatest(GitCommandFilter.AllCommands);
        Assert.NotNull(activity);
        Assert.Equal(GitCommandStatus.Cancelled, activity.Status);
        Assert.Contains("partial", activity.StandardOutput, StringComparison.Ordinal);
    }

    private static GitCommandExecutor CreateShellExecutor(IGitCommandActivitySink sink) =>
        new(new GitCliOptions { ExecutablePath = ShellExecutable() }, sink);

    private static string ShellExecutable() => OperatingSystem.IsWindows()
        ? Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe"
        : "/bin/sh";

    private static string[] DelayedShellArguments(string first, string second, int delaySeconds) =>
        WrapShellCommand($"{first} & {DelayCommand(delaySeconds)} & {second}");

    private static string[] CommandsThenDelayShellArguments(string first, string second, int delaySeconds) =>
        WrapShellCommand($"{first} & {second} & {DelayCommand(delaySeconds)}");

    private static string DelayCommand(int delaySeconds) => OperatingSystem.IsWindows()
        ? $"ping -n {delaySeconds + 1} 127.0.0.1 >nul"
        : $"sleep {delaySeconds}";

    private static string[] WrapShellCommand(string command) => OperatingSystem.IsWindows()
        ? ["/d", "/s", "/c", command]
        : ["-c", command.Replace(" & ", "; ", StringComparison.Ordinal)];

    private static string[] LargeOutputShellArguments() => OperatingSystem.IsWindows()
        ? ["/d", "/s", "/c", "for /L %i in (1,1,70000) do @echo 1234567890123456"]
        : ["-c", "head -c 1100000 /dev/zero | tr '\\0' x"];
}
