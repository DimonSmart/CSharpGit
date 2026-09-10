using System.Diagnostics;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Infrastructure;

public sealed class RepositoryPathService : IRepositoryPathService
{
    public string ResolveExistingWorkingTreeFile(
        Repository repository,
        string gitPath,
        bool allowFinalLink = false)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ValidateGitPath(gitPath);

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repository.WorkingDirectory));
        var relativePath = gitPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefix = root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, comparison))
            throw new InvalidOperationException("The file path is outside the repository.");

        var parts = relativePath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        for (var index = 0; index < parts.Length; index++)
        {
            current = Path.Combine(current, parts[index]);
            if (!File.Exists(current) && !Directory.Exists(current))
                throw new FileNotFoundException("The file is not present in the current working tree.", fullPath);

            var isFinal = index == parts.Length - 1;
            System.IO.FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(current);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException("The working-tree path could not be validated safely.", exception);
            }

            if ((attributes & System.IO.FileAttributes.ReparsePoint) != 0 && (!isFinal || !allowFinalLink))
                throw new InvalidOperationException("Opening a path through a symbolic link or reparse point is not allowed.");
        }

        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The file is not present in the current working tree.", fullPath);
        return fullPath;
    }

    private static void ValidateGitPath(string gitPath)
    {
        if (string.IsNullOrWhiteSpace(gitPath) || gitPath.Contains('\0') || Path.IsPathRooted(gitPath))
            throw new ArgumentException("Invalid Git file path.", nameof(gitPath));
        if (gitPath.Replace('\\', '/').Split('/').Any(part => part is ".." or "."))
            throw new ArgumentException("Git file path traversal is not allowed.", nameof(gitPath));
    }
}

public sealed class DesktopShellService : IDesktopShellService
{
    public string RevealDescription => OperatingSystem.IsWindows()
        ? "Reveal in Explorer"
        : OperatingSystem.IsMacOS()
            ? "Reveal in Finder"
            : "Reveal in file manager";

    public Task OpenFileAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = RequireExistingFile(path);
        Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true });
        return Task.CompletedTask;
    }

    public Task RevealFileAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = RequireExistingFile(path);

        ProcessStartInfo startInfo;
        if (OperatingSystem.IsWindows())
        {
            startInfo = CreateCommand("explorer.exe", $"/select,{fullPath}");
        }
        else if (OperatingSystem.IsMacOS())
        {
            startInfo = CreateCommand("open", "-R", fullPath);
        }
        else
        {
            startInfo = CreateCommand("xdg-open", Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException("The containing directory could not be determined."));
        }

        Process.Start(startInfo);
        return Task.CompletedTask;
    }

    private static ProcessStartInfo CreateCommand(string executable, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    private static string RequireExistingFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A file path is required.", nameof(path));
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("The file is no longer available.", fullPath);
        return fullPath;
    }
}
