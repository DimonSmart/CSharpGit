using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using CSharpGit.Application;
using CSharpGit.Application.Abstractions;

namespace CSharpGit.Git;

internal sealed class GitCommandExecutor
{
    private readonly string _gitExecutable;
    private readonly IGitCommandActivitySink _activitySink;
    private readonly Action<int>? _processStarted;

    internal static GitCommandExecutor Default { get; } = new(new GitCliOptions());

    public GitCommandExecutor(GitCliOptions options)
        : this(options, null, null)
    {
    }

    internal GitCommandExecutor(
        GitCliOptions options,
        IGitCommandActivitySink? activitySink,
        Action<int>? processStarted = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _gitExecutable = string.IsNullOrWhiteSpace(options.ExecutablePath) ? "git" : options.ExecutablePath;
        _activitySink = activitySink ?? GitCommandActivitySession.Current;
        _processStarted = processStarted;
    }

    internal string ExecutablePath => _gitExecutable;

    public Task<string> ExecuteAsync(
        string workingDirectory,
        string operation,
        CancellationToken cancellationToken,
        params string[] arguments) =>
        ExecuteAsync(workingDirectory, operation, GitCommandKind.Internal, cancellationToken, null, arguments);

    public Task<string> ExecuteAsync(
        string workingDirectory,
        string operation,
        GitCommandKind commandKind,
        CancellationToken cancellationToken,
        params string[] arguments) =>
        ExecuteAsync(workingDirectory, operation, commandKind, cancellationToken, null, arguments);

    public async Task<string> ExecuteAsync(
        string workingDirectory,
        string operation,
        GitCommandKind commandKind,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string?>? environment,
        IReadOnlyList<string> arguments)
    {
        var result = await ExecuteForResultAsync(
            workingDirectory,
            operation,
            commandKind,
            cancellationToken,
            environment,
            arguments);
        ThrowIfFailed(result);
        return result.StandardOutput;
    }

    public Task<GitCommandResult> ExecuteForResultAsync(
        string workingDirectory,
        string operation,
        GitCommandKind commandKind,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string?>? environment,
        IReadOnlyList<string> arguments) =>
        ExecuteProcessCoreAsync(
            _gitExecutable,
            workingDirectory,
            operation,
            commandKind,
            cancellationToken,
            arguments,
            _activitySink,
            environment,
            _processStarted);

    internal Task<GitCommandResult> ExecuteForResultPreservingGitEditorAsync(
        string workingDirectory,
        string operation,
        CancellationToken cancellationToken,
        IReadOnlyList<string> arguments) =>
        ExecuteProcessCoreAsync(
            _gitExecutable,
            workingDirectory,
            operation,
            GitCommandKind.Internal,
            cancellationToken,
            arguments,
            _activitySink,
            null,
            _processStarted,
            installNoOpGitEditor: false);

    public async Task ExecuteToFileAsync(
        string workingDirectory,
        string operation,
        CancellationToken cancellationToken,
        string destination,
        params string[] arguments)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = CreateStartInfo(_gitExecutable, workingDirectory, arguments, null);
        using var process = new Process { StartInfo = startInfo };
        var stopwatch = Stopwatch.StartNew();
        process.Start();
        var activityId = _activitySink.Started(_gitExecutable, workingDirectory, arguments, GitCommandKind.Internal);
        _processStarted?.Invoke(process.Id);
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
                await file.FlushAsync(CancellationToken.None);
            }

            error = await errorTask;
            stopwatch.Stop();
            var result = new GitCommandResult(process.ExitCode, "[binary output omitted]", error.TrimEnd('\r', '\n'));
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

    private static async Task<GitCommandResult> ExecuteProcessCoreAsync(
        string executable,
        string workingDirectory,
        string operation,
        GitCommandKind commandKind,
        CancellationToken cancellationToken,
        IReadOnlyList<string> arguments,
        IGitCommandActivitySink? activitySink,
        IReadOnlyDictionary<string, string?>? environment = null,
        Action<int>? processStarted = null,
        bool installNoOpGitEditor = true)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = CreateStartInfo(executable, workingDirectory, arguments, environment, installNoOpGitEditor);

        using var process = new Process { StartInfo = startInfo };
        var stopwatch = Stopwatch.StartNew();
        process.Start();
        var activityId = activitySink?.Started(executable, workingDirectory, arguments, commandKind);
        processStarted?.Invoke(process.Id);

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
        var result = new GitCommandResult(process.ExitCode, output, error);
        if (activityId is { } completedId)
            activitySink!.Completed(completedId, result.ExitCode, result.StandardOutput, result.StandardError);
        return result;
    }

    private static ProcessStartInfo CreateStartInfo(
        string executable,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?>? environment,
        bool installNoOpGitEditor = true)
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
        if (installNoOpGitEditor)
            startInfo.Environment["GIT_EDITOR"] = CreateNoOpEditorCommand();
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        if (environment is not null)
            foreach (var variable in environment) startInfo.Environment[variable.Key] = variable.Value;
        return startInfo;
    }

    private static string CreateNoOpEditorCommand()
    {
        if (!OperatingSystem.IsWindows()) return "/bin/sh -c :";
        var commandProcessor = Environment.GetEnvironmentVariable("ComSpec");
        if (string.IsNullOrWhiteSpace(commandProcessor)) commandProcessor = "cmd.exe";
        return $"\"{commandProcessor.Replace("\"", "\\\"")}\" /d /c rem";
    }

    private static void ThrowIfFailed(GitCommandResult result)
    {
        if (result.ExitCode == 0) return;
        throw new GitCommandExecutionException(result);
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
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

internal sealed record GitCommandResult(int ExitCode, string StandardOutput, string StandardError);

internal sealed class GitCommandExecutionException : InvalidOperationException
{
    public GitCommandExecutionException(GitCommandResult result)
        : base(string.IsNullOrWhiteSpace(result.StandardError)
            ? $"Git exited with code {result.ExitCode}."
            : $"Git exited with code {result.ExitCode}: {result.StandardError.Trim()}")
    {
        Result = result;
    }

    public GitCommandResult Result { get; }
}
