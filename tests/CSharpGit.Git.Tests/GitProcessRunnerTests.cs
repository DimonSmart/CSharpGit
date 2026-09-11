using System.Diagnostics;

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
}
