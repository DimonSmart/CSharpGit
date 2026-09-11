using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace CSharpGit.Git;

internal sealed class GitProcessRunner
{
    private readonly string _gitExecutable;
    private int _invocationCount;

    public GitProcessRunner(string gitExecutable)
    {
        _gitExecutable = string.IsNullOrWhiteSpace(gitExecutable) ? "git" : gitExecutable;
    }

    public int InvocationCount => Volatile.Read(ref _invocationCount);

    public async Task<string> RunAsync(
        string workingDirectory,
        string operation,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        Interlocked.Increment(ref _invocationCount);
        return await RunProcessAsync(
            _gitExecutable,
            workingDirectory,
            operation,
            cancellationToken,
            arguments);
    }

    internal static async Task<string> RunProcessAsync(
        string executable,
        string workingDirectory,
        string operation,
        CancellationToken cancellationToken,
        IReadOnlyList<string> arguments,
        Action<int>? processStarted = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        var stopwatch = Stopwatch.StartNew();
        process.Start();
        processStarted?.Invoke(process.Id);

        // Do not cancel pipe readers independently. On cancellation the process tree is
        // terminated first, then both streams are drained before the Process is disposed.
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            await WaitForExitAfterKillAsync(process);
            await DrainAfterKillAsync(outputTask, errorTask);
            throw;
        }

        var output = await outputTask;
        var error = (await errorTask).Trim();
        cancellationToken.ThrowIfCancellationRequested();

        stopwatch.Stop();
        Trace.WriteLine($"Git command operation={operation} duration={stopwatch.ElapsedMilliseconds}ms");
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? $"Git exited with code {process.ExitCode}."
                : $"Git exited with code {process.ExitCode}: {error}");
        return output.TrimEnd('\r', '\n');
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // The process can win the race and exit between HasExited and Kill.
        }
    }

    private static async Task WaitForExitAfterKillAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync(CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            // Already exited/disposed by the time the cancellation continuation ran.
        }
    }

    private static async Task DrainAfterKillAsync(Task<string> outputTask, Task<string> errorTask)
    {
        try
        {
            await Task.WhenAll(outputTask, errorTask);
        }
        catch (IOException)
        {
            // A killed process can close a redirected pipe while the async read completes.
        }
    }
}
