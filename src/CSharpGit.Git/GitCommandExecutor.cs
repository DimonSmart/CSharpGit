using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using CSharpGit.Application;
using CSharpGit.Application.Abstractions;

namespace CSharpGit.Git;

internal sealed class GitCommandExecutor
{
    private const string BinaryOutputOmitted = "[binary output omitted]";
    private const int TextReadBufferSize = 4096;
    private const int MaximumStandardErrorBytes = 4 * 1024 * 1024;

    private readonly string _gitExecutable;
    private readonly IGitCommandActivitySink _activitySink;
    private readonly Action<int>? _processStarted;
    private readonly Func<string, Stream>? _destinationStreamFactory;

    internal GitCommandExecutor(
        GitCliOptions options,
        IGitCommandActivitySink activitySink,
        Action<int>? processStarted = null,
        Func<string, Stream>? destinationStreamFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _gitExecutable = string.IsNullOrWhiteSpace(options.ExecutablePath) ? "git" : options.ExecutablePath;
        _activitySink = activitySink ?? throw new ArgumentNullException(nameof(activitySink));
        _processStarted = processStarted;
        _destinationStreamFactory = destinationStreamFactory;
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

    public async Task<GitCommandResult> ExecuteForResultAsync(
        string workingDirectory,
        string operation,
        GitCommandKind commandKind,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string?>? environment,
        IReadOnlyList<string> arguments)
    {
        try
        {
            return await ExecuteProcessCoreAsync(
                _gitExecutable, workingDirectory, operation, commandKind,
                cancellationToken, arguments, _activitySink, environment, _processStarted);
        }
        catch (Exception) when (
            !cancellationToken.IsCancellationRequested &&
            RequiresSensitiveDiagnostics(arguments))
        {
            // Never attach the original exception: process-start errors may include arguments.
            throw new InvalidOperationException($"Git {operation} failed; sensitive diagnostics omitted.");
        }
    }

    internal async Task<string> ExecuteAsyncPreservingOutputEndings(
        string workingDirectory,
        string operation,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        var result = await ExecuteForResultPreservingOutputEndingsAsync(
            workingDirectory,
            operation,
            GitCommandKind.Internal,
            cancellationToken,
            null,
            arguments);
        ThrowIfFailed(result);
        return result.StandardOutput;
    }

    internal async Task<string> ExecuteAsyncPreservingOutputEndings(
        string workingDirectory,
        string operation,
        CancellationToken cancellationToken,
        int? maxStandardOutputBytes,
        IReadOnlyList<string> arguments)
    {
        var result = await ExecuteForResultPreservingOutputEndingsAsync(
            workingDirectory,
            operation,
            GitCommandKind.Internal,
            cancellationToken,
            null,
            arguments,
            maxStandardOutputBytes);
        ThrowIfFailed(result);
        return result.StandardOutput;
    }

    internal Task<GitCommandResult> ExecuteForResultPreservingOutputEndingsAsync(
        string workingDirectory,
        string operation,
        GitCommandKind commandKind,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string?>? environment,
        IReadOnlyList<string> arguments,
        int? maxStandardOutputBytes = null) =>
        ExecuteProcessCoreAsync(
            _gitExecutable,
            workingDirectory,
            operation,
            commandKind,
            cancellationToken,
            arguments,
            _activitySink,
            environment,
            _processStarted,
            trimOutputEndings: false,
            maxStandardOutputBytes: maxStandardOutputBytes);

    // Config snapshots can contain arbitrary user secrets. Retain the process
    // lifecycle for diagnostics, but never forward its stdout/stderr to the activity sink.
    internal async Task<GitCommandResult> ExecuteForResultSensitiveAsync(
        string workingDirectory,
        string operation,
        CancellationToken cancellationToken,
        IReadOnlyList<string> arguments)
    {
        try
        {
            return await ExecuteProcessCoreAsync(
                _gitExecutable, workingDirectory, operation, GitCommandKind.Internal,
                cancellationToken, arguments, _activitySink, null, _processStarted,
                trimOutputEndings: false, suppressOutputEvents: true);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            throw new InvalidOperationException($"Git {operation} failed; sensitive diagnostics omitted.");
        }
    }

    internal Task<GitCommandResult> ExecuteForResultPreservingGitEditorAsync(
        string workingDirectory,
        string operation,
        CancellationToken cancellationToken,
        IReadOnlyList<string> arguments) =>
        ExecuteForResultPreservingGitEditorAsync(
            workingDirectory,
            operation,
            GitCommandKind.Internal,
            cancellationToken,
            null,
            arguments);

    internal Task<GitCommandResult> ExecuteForResultPreservingGitEditorAsync(
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

        // Destination failures must not create an orphaned process.
        await using var file = _destinationStreamFactory?.Invoke(destination)
            ?? new FileStream(destination, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 81920, FileOptions.Asynchronous);
        var startInfo = CreateStartInfo(_gitExecutable, workingDirectory, arguments, null);
        using var process = new Process { StartInfo = startInfo };
        var stopwatch = Stopwatch.StartNew();
        process.Start();
        Guid? activityId = null;
        Task copyTask = Task.CompletedTask;
        Task<string> errorTask = Task.FromResult(string.Empty);
        var finished = false;
        try
        {
            activityId = _activitySink.Started(_gitExecutable, workingDirectory, arguments, GitCommandKind.Internal);
            _activitySink.OutputReceived(activityId.Value, GitOutputStream.StandardOutput, BinaryOutputOmitted);
            _processStarted?.Invoke(process.Id);
            errorTask = PumpTextAsync(
                process.StandardError,
                activityId,
                GitOutputStream.StandardError,
                _activitySink,
                maxOutputBytes: MaximumStandardErrorBytes,
                outputLimitExceeded: () => KillProcessTree(process));
            copyTask = process.StandardOutput.BaseStream.CopyToAsync(file, CancellationToken.None);

            await WaitForProcessAndPumpsAsync(process, cancellationToken, copyTask, errorTask);
            await file.FlushAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var error = errorTask.Result.TrimEnd('\r', '\n');
            var result = new GitCommandResult(process.ExitCode, BinaryOutputOmitted, error);
            stopwatch.Stop();
            _activitySink.Completed(activityId.Value, result.ExitCode);
            finished = true;
            Trace.WriteLine($"Git command operation={operation} duration={stopwatch.ElapsedMilliseconds}ms");
            ThrowIfFailed(result);
        }
        catch
        {
            // Include failures in activity callbacks, stream copy, flush and
            // process coordination, not just cancellation.
            if (!finished)
            {
                await CleanupAfterFailureAsync(process, copyTask, errorTask);
                if (activityId is { } id)
                {
                    try { _activitySink.Cancelled(id, TryGetExitCode(process)); }
                    catch (Exception) { Trace.WriteLine("Git activity cleanup failed."); }
                }
            }
            throw;
        }
        finally
        {
            stopwatch.Stop();
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
        bool installNoOpGitEditor = true,
        bool trimOutputEndings = true,
        int? maxStandardOutputBytes = null,
        bool suppressOutputEvents = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maxStandardOutputBytes is <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxStandardOutputBytes));
        var sensitive = suppressOutputEvents || RequiresSensitiveDiagnostics(arguments);
        var effectiveArguments = GitProgressPolicy.Apply(arguments, commandKind);
        var startInfo = CreateStartInfo(executable, workingDirectory, effectiveArguments, environment, installNoOpGitEditor);

        using var process = new Process { StartInfo = startInfo };
        var stopwatch = Stopwatch.StartNew();
        process.Start();

        Guid? activityId = null;
        Task<string> outputTask = Task.FromResult(string.Empty);
        Task<string> errorTask = Task.FromResult(string.Empty);
        var finished = false;
        try
        {
            activityId = activitySink?.Started(
                executable, workingDirectory,
                sensitive
                    ? (arguments.Count > 0 && arguments[0] is "config" or "remote"
                        ? [arguments[0], "[sensitive Git arguments omitted]"]
                        : ["[sensitive Git arguments omitted]"])
                    : effectiveArguments,
                commandKind);
            processStarted?.Invoke(process.Id);

            outputTask = PumpTextAsync(
                process.StandardOutput,
                sensitive ? null : activityId,
                GitOutputStream.StandardOutput,
                sensitive ? null : activitySink,
                maxStandardOutputBytes,
                () => KillProcessTree(process));
            errorTask = PumpTextAsync(
                process.StandardError,
                sensitive ? null : activityId,
                GitOutputStream.StandardError,
                sensitive ? null : activitySink,
                maxOutputBytes: MaximumStandardErrorBytes,
                outputLimitExceeded: () => KillProcessTree(process));

            await WaitForProcessAndPumpsAsync(process, cancellationToken, outputTask, errorTask);
            cancellationToken.ThrowIfCancellationRequested();

            var rawOutput = outputTask.Result;
            var output = trimOutputEndings ? rawOutput.TrimEnd('\r', '\n') : rawOutput;
            var error = errorTask.Result.TrimEnd('\r', '\n');
            stopwatch.Stop();
            Trace.WriteLine($"Git command operation={operation} duration={stopwatch.ElapsedMilliseconds}ms");

            var result = sensitive
                ? new GitCommandResult(
                    process.ExitCode,
                    process.ExitCode == 0 ? output : string.Empty,
                    process.ExitCode == 0 ? string.Empty : SafeSensitiveFailure(error, output, arguments))
                : new GitCommandResult(process.ExitCode, output, error);
            if (activityId is { } completedId)
                activitySink!.Completed(completedId, result.ExitCode);
            finished = true;
            return result;
        }
        catch
        {
            if (!finished)
            {
                await CleanupAfterFailureAsync(process, outputTask, errorTask);
                if (activityId is { } cancelledId)
                {
                    try { activitySink!.Cancelled(cancelledId, TryGetExitCode(process)); }
                    catch (Exception exception) { Trace.WriteLine($"Git activity cleanup failed: {exception}"); }
                }
            }
            throw;
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    private static async Task WaitForProcessAndPumpsAsync(
        Process process, CancellationToken cancellationToken, params Task[] pumps)
    {
        var pending = new List<Task>(pumps.Length + 1) { process.WaitForExitAsync(cancellationToken) };
        pending.AddRange(pumps);
        while (pending.Count > 0)
        {
            var completed = await Task.WhenAny(pending);
            await completed; // Propagate pump errors immediately; otherwise Git may block on its pipes.
            pending.Remove(completed);
        }
    }

    private static async Task CleanupAfterFailureAsync(Process process, params Task[] pumps)
    {
        KillProcessTree(process);
        try { await WaitForExitAfterKillAsync(process).WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (Exception exception) { Trace.WriteLine($"Git process shutdown failed: {exception}"); }
        try { await Task.WhenAll(pumps).WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (Exception exception) { Trace.WriteLine($"Git stream drain failed: {exception}"); }
    }

    private static async Task<string> PumpTextAsync(
        StreamReader reader,
        Guid? activityId,
        GitOutputStream stream,
        IGitCommandActivitySink? activitySink,
        int? maxOutputBytes = null,
        Action? outputLimitExceeded = null)
    {
        var result = new StringBuilder();
        var buffer = new char[TextReadBufferSize];
        var outputBytes = 0;
        var utf8Encoder = Encoding.UTF8.GetEncoder();

        while (true)
        {
            int read;
            read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), CancellationToken.None);

            if (read == 0) break;
            if (maxOutputBytes is { } limit)
            {
                // Encoder preserves a surrogate pair across successive ReadAsync calls.
                var chunkBytes = utf8Encoder.GetByteCount(buffer.AsSpan(0, read), flush: false);
                if (outputBytes > limit - chunkBytes)
                {
                    outputLimitExceeded?.Invoke();
                    throw new GitCommandOutputLimitExceededException(limit);
                }
                outputBytes += chunkBytes;
            }

            result.Append(buffer, 0, read);
            if (activityId is { } id)
                activitySink!.OutputReceived(id, stream, new string(buffer, 0, read));
        }

        if (maxOutputBytes is { } finalLimit)
        {
            var remainingBytes = utf8Encoder.GetByteCount(ReadOnlySpan<char>.Empty, flush: true);
            if (outputBytes > finalLimit - remainingBytes)
            {
                outputLimitExceeded?.Invoke();
                throw new GitCommandOutputLimitExceededException(finalLimit);
            }
        }
        return result.ToString();
    }

    // Unknown config keys and URL authentication schemes cannot be protected by regex.
    // This guard applies to every entry point using ExecuteProcessCoreAsync.
    private static bool RequiresSensitiveDiagnostics(IReadOnlyList<string> arguments)
    {
        var commandIndex = 0;
        var hasInlineConfiguration = false;
        while (commandIndex < arguments.Count && arguments[commandIndex].StartsWith("-", StringComparison.Ordinal))
        {
            var argument = arguments[commandIndex++];
            if (argument is "-c" or "--config-env"
                || argument.StartsWith("--config-env=", StringComparison.Ordinal)
                || (argument.StartsWith("-c", StringComparison.Ordinal) && argument.Length > 2))
                hasInlineConfiguration = true;
            if (argument is "-C" or "--git-dir" or "--work-tree" or "-c" or "--config-env")
                commandIndex++;
        }

        // A shell used by executable tests also uses -c, but has no Git subcommand.
        if (commandIndex >= arguments.Count) return false;
        var command = arguments[commandIndex];
        return hasInlineConfiguration
            || command == "config"
            || (command == "remote" && commandIndex + 1 < arguments.Count &&
                arguments.Skip(commandIndex + 1).Any(value => value is "-v" or "--verbose"));
    }

    private static string SafeSensitiveFailure(
        string stderr, string stdout, IReadOnlyList<string> arguments)
    {
        // The raw diagnostic is used only for classification. Never copy any portion of it.
        if (!arguments.Contains("push", StringComparer.Ordinal))
            return string.Empty;
        var diagnostic = stderr + "\n" + stdout;
        if (diagnostic.Contains("no configured push destination", StringComparison.OrdinalIgnoreCase))
            return "no configured push destination";
        if (diagnostic.Contains("has no upstream branch", StringComparison.OrdinalIgnoreCase))
            return "has no upstream branch";
        if (diagnostic.Contains("upstream branch of your current branch does not match", StringComparison.OrdinalIgnoreCase))
            return "upstream branch of your current branch does not match";
        return string.Empty;
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
        catch (Exception exception) when (exception is IOException or GitCommandOutputLimitExceededException)
        {
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

internal sealed class GitCommandOutputLimitExceededException : InvalidOperationException
{
    public GitCommandOutputLimitExceededException(int limitBytes)
        : base($"Git command output exceeded the configured {limitBytes}-byte limit.")
    {
        LimitBytes = limitBytes;
    }

    public int LimitBytes { get; }
}
