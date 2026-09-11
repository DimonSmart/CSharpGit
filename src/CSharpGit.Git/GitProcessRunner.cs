using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using CSharpGit.Application;
using CSharpGit.Application.Abstractions;

namespace CSharpGit.Git;

internal sealed class GitProcessRunner
{
    private readonly string _gitExecutable;
    private readonly IGitCommandActivitySink _activitySink;
    private int _invocationCount;

    public GitProcessRunner(string gitExecutable, IGitCommandActivitySink? activitySink = null)
    {
        _gitExecutable = string.IsNullOrWhiteSpace(gitExecutable) ? "git" : gitExecutable;
        _activitySink = activitySink ?? GitCommandActivitySession.Current;
    }

    public int InvocationCount => Volatile.Read(ref _invocationCount);

    public Task<string> RunAsync(
        string workingDirectory,
        string operation,
        CancellationToken cancellationToken,
        params string[] arguments) =>
        RunAsync(workingDirectory, operation, GitCommandKind.Internal, cancellationToken, arguments);

    public async Task<string> RunAsync(
        string workingDirectory,
        string operation,
        GitCommandKind commandKind,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        Interlocked.Increment(ref _invocationCount);
        var result = await RunProcessCoreAsync(
            _gitExecutable,
            workingDirectory,
            operation,
            commandKind,
            cancellationToken,
            arguments,
            _activitySink);
        ThrowIfFailed(result);
        return result.StandardOutput.TrimEnd('\r', '\n');
    }

    public async Task<GitProcessResult> RunForResultAsync(
        string workingDirectory,
        string operation,
        GitCommandKind commandKind,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string?>? environment,
        IReadOnlyList<string> arguments)
    {
        Interlocked.Increment(ref _invocationCount);
        return await RunProcessCoreAsync(
            _gitExecutable,
            workingDirectory,
            operation,
            commandKind,
            cancellationToken,
            arguments,
            _activitySink,
            environment);
    }

    public async Task RunToFileAsync(
        string workingDirectory,
        string operation,
        CancellationToken cancellationToken,
        string destination,
        params string[] arguments)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _invocationCount);

        var startInfo = CreateStartInfo(_gitExecutable, workingDirectory, arguments, null);
        using var process = new Process { StartInfo = startInfo };
        var stopwatch = Stopwatch.StartNew();
        process.Start();
        var activityId = _activitySink.Started(_gitExecutable, workingDirectory, arguments, GitCommandKind.Internal);
        var errorTask = process.StandardError.ReadToEndAsync();
        string error;

        try
        {
            await using (var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                var copyTask = process.StandardOutput.BaseStream.CopyToAsync(file, CancellationToken.None);
                try
                {
                    await process.WaitForExitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    KillProcessTree(process);
                    await WaitForExitAfterKillAsync(process);
                    await DrainAfterKillAsync(copyTask, errorTask);
                    stopwatch.Stop();
                    error = await SafeReadAsync(errorTask);
                    _activitySink.Cancelled(activityId, TryGetExitCode(process), "[binary output omitted]", error.TrimEnd('\r', '\n'));
                    throw;
                }

                await copyTask;
                await file.FlushAsync(cancellationToken);
            }

            error = await errorTask;
            stopwatch.Stop();
            var result = new GitProcessResult(process.ExitCode, "[binary output omitted]", error.TrimEnd('\r', '\n'));
            _activitySink.Completed(activityId, result.ExitCode, result.StandardOutput, result.StandardError);
            Trace.WriteLine($"Git command operation={operation} duration={stopwatch.ElapsedMilliseconds}ms");
            ThrowIfFailed(result);
        }
        catch
        {
            if (process.HasExited && stopwatch.IsRunning)
                stopwatch.Stop();
            throw;
        }
    }

    internal static async Task<string> RunProcessAsync(
        string executable,
        string workingDirectory,
        string operation,
        CancellationToken cancellationToken,
        IReadOnlyList<string> arguments,
        Action<int>? processStarted = null)
    {
        var result = await RunProcessCoreAsync(
            executable,
            workingDirectory,
            operation,
            GitCommandKind.Internal,
            cancellationToken,
            arguments,
            null,
            null,
            processStarted);
        ThrowIfFailed(result);
        return result.StandardOutput.TrimEnd('\r', '\n');
    }

    private static async Task<GitProcessResult> RunProcessCoreAsync(
        string executable,
        string workingDirectory,
        string operation,
        GitCommandKind commandKind,
        CancellationToken cancellationToken,
        IReadOnlyList<string> arguments,
        IGitCommandActivitySink? activitySink,
        IReadOnlyDictionary<string, string?>? environment = null,
        Action<int>? processStarted = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = CreateStartInfo(executable, workingDirectory, arguments, environment);

        using var process = new Process { StartInfo = startInfo };
        var stopwatch = Stopwatch.StartNew();
        process.Start();
        var activityId = activitySink?.Started(executable, workingDirectory, arguments, commandKind);
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
            stopwatch.Stop();
            var cancelledOutput = (await SafeReadAsync(outputTask)).TrimEnd('\r', '\n');
            var cancelledError = (await SafeReadAsync(errorTask)).TrimEnd('\r', '\n');
            if (activityId is { } cancelledId)
                activitySink!.Cancelled(cancelledId, TryGetExitCode(process), cancelledOutput, cancelledError);
            throw;
        }

        await Task.WhenAll(outputTask, errorTask);
        var output = outputTask.Result.TrimEnd('\r', '\n');
        var error = errorTask.Result.TrimEnd('\r', '\n');
        cancellationToken.ThrowIfCancellationRequested();

        stopwatch.Stop();
        Trace.WriteLine($"Git command operation={operation} duration={stopwatch.ElapsedMilliseconds}ms");
        var result = new GitProcessResult(process.ExitCode, output, error);
        if (activityId is { } completedId)
            activitySink!.Completed(completedId, result.ExitCode, result.StandardOutput, result.StandardError);
        return result;
    }

    private static ProcessStartInfo CreateStartInfo(
        string executable,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?>? environment)
    {
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
        if (environment is not null)
            foreach (var variable in environment) startInfo.Environment[variable.Key] = variable.Value;
        return startInfo;
    }

    private static void ThrowIfFailed(GitProcessResult result)
    {
        if (result.ExitCode == 0) return;
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.StandardError)
            ? $"Git exited with code {result.ExitCode}."
            : $"Git exited with code {result.ExitCode}: {result.StandardError.Trim()}");
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

    private static async Task DrainAfterKillAsync(params Task[] tasks)
    {
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (IOException)
        {
            // A killed process can close a redirected pipe while the async read completes.
        }
    }

    private static async Task<string> SafeReadAsync(Task<string> task)
    {
        try
        {
            return await task;
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    private static int? TryGetExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}

internal sealed record GitProcessResult(int ExitCode, string StandardOutput, string StandardError);
