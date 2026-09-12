using System.ComponentModel;
using System.Diagnostics;
using CSharpGit.Application.Abstractions;

namespace CSharpGit.Infrastructure;

public sealed class ExternalToolProcessService : IExternalToolProcessService
{
    public string? ResolveExecutable(string commandOrPath)
    {
        if (string.IsNullOrWhiteSpace(commandOrPath)) return null;

        var executable = ReadExecutableToken(commandOrPath.Trim());
        if (string.IsNullOrWhiteSpace(executable)) return null;

        if (Path.IsPathRooted(executable) || executable.Contains(Path.DirectorySeparatorChar) || executable.Contains(Path.AltDirectorySeparatorChar))
        {
            var fullPath = Path.GetFullPath(executable);
            return IsExecutablePathUsable(fullPath) ? fullPath : null;
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path)) return null;

        var extensions = OperatingSystem.IsWindows()
            ? ReadWindowsExecutableExtensions(executable)
            : new[] { string.Empty };

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, executable + extension);
                if (IsExecutablePathUsable(candidate)) return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    public bool IsExecutablePathUsable(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        try
        {
            var fullPath = Path.GetFullPath(path.Trim());
            if (OperatingSystem.IsMacOS() && Directory.Exists(fullPath) && fullPath.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                return true;
            if (!File.Exists(fullPath)) return false;
            if (OperatingSystem.IsWindows()) return true;

            var mode = File.GetUnixFileMode(fullPath);
            return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    public async Task RunShellCommandAsync(
        string rawCommand,
        string fileArgument,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawCommand))
            throw new InvalidOperationException("Git editor is not configured.");
        if (string.IsNullOrWhiteSpace(fileArgument))
            throw new ArgumentException("A file path is required.", nameof(fileArgument));

        cancellationToken.ThrowIfCancellationRequested();
        var fullFilePath = Path.GetFullPath(fileArgument);
        var startInfo = CreateShellStartInfo(rawCommand, fullFilePath, workingDirectory);

        try
        {
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            var standardError = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(cancellationToken);
            var error = (await standardError).Trim();
            if (process.ExitCode != 0)
            {
                var detail = string.IsNullOrWhiteSpace(error)
                    ? $"Editor exited with code {process.ExitCode}."
                    : error;
                throw new InvalidOperationException(detail);
            }
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException($"Could not start the configured editor: {exception.Message}", exception);
        }
    }

    private static ProcessStartInfo CreateShellStartInfo(string rawCommand, string filePath, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = Directory.Exists(workingDirectory) ? workingDirectory : Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };

        if (OperatingSystem.IsWindows())
        {
            startInfo.FileName = Environment.GetEnvironmentVariable("ComSpec") is { Length: > 0 } commandProcessor
                ? commandProcessor
                : "cmd.exe";
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/s");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add($"{rawCommand} {QuoteWindowsArgument(filePath)}");
        }
        else
        {
            startInfo.FileName = "/bin/sh";
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add($"{rawCommand} {ShellQuote(filePath)}");
        }

        return startInfo;
    }

    private static string ReadExecutableToken(string command)
    {
        if (command.Length == 0) return string.Empty;
        if (command[0] is '"' or '\'')
        {
            var quote = command[0];
            var end = command.IndexOf(quote, 1);
            return end > 1 ? command[1..end] : command[1..];
        }

        var whitespace = command.IndexOfAny([' ', '\t', '\r', '\n']);
        return whitespace < 0 ? command : command[..whitespace];
    }

    private static IReadOnlyList<string> ReadWindowsExecutableExtensions(string executable)
    {
        if (!string.IsNullOrWhiteSpace(Path.GetExtension(executable))) return [string.Empty];
        var pathExt = Environment.GetEnvironmentVariable("PATHEXT");
        if (string.IsNullOrWhiteSpace(pathExt)) return [".exe", ".cmd", ".bat", ".com", string.Empty];
        return pathExt.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Prepend(string.Empty)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string QuoteWindowsArgument(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''") + "'";
}
