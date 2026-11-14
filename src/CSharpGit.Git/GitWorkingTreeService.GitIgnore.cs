using System.Text;
using CSharpGit.Domain;

namespace CSharpGit.Git;

internal sealed partial class GitWorkingTreeService
{
    public Task AddFileToGitIgnoreAsync(
        Repository repository,
        WorkingTreeChange change,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (change.IndexStatus != '?' || change.WorkingTreeStatus != '?' || change.IsConflicted)
            throw new InvalidOperationException("Only untracked files can be added to .gitignore.");

        return AppendGitIgnoreRuleAsync(repository, change.Path, isFolder: false, cancellationToken);
    }

    public Task AddFolderToGitIgnoreAsync(
        Repository repository,
        string folderPath,
        CancellationToken cancellationToken = default) =>
        AppendGitIgnoreRuleAsync(repository, folderPath, isFolder: true, cancellationToken);

    internal static string BuildGitIgnorePattern(Repository repository, string relativePath, bool isFolder)
    {
        ArgumentNullException.ThrowIfNull(repository);
        GitPathValidator.ValidateRepositoryRelative(relativePath);

        var segments = relativePath.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or "..") ||
            relativePath.IndexOfAny(['\0', '\r', '\n']) >= 0 ||
            string.Equals(segments[0], ".git", StringComparison.OrdinalIgnoreCase) ||
            (!isFolder && string.Equals(segments[^1], ".gitignore", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("This path cannot be added to .gitignore.", nameof(relativePath));

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repository.RepositoryRoot));
        var fullPath = Path.GetFullPath(
            Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, comparison))
            throw new ArgumentException("The path is outside the repository.", nameof(relativePath));

        var pattern = new StringBuilder("/");
        foreach (var character in relativePath)
        {
            if (character is '*' or '?' or '[' or ']' or '\\')
                pattern.Append('\\');
            pattern.Append(character);
        }

        if (isFolder) pattern.Append('/');
        return pattern.ToString();
    }

    private static async Task AppendGitIgnoreRuleAsync(
        Repository repository,
        string relativePath,
        bool isFolder,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pattern = BuildGitIgnorePattern(repository, relativePath, isFolder);
        var ignoreFile = Path.Combine(repository.RepositoryRoot, ".gitignore");

        if (new FileInfo(ignoreFile).LinkTarget is not null)
            throw new InvalidOperationException("Refusing to edit a symbolic-link .gitignore.");

        var existing = File.Exists(ignoreFile)
            ? await File.ReadAllTextAsync(ignoreFile, cancellationToken)
            : string.Empty;
        if (existing.Split('\n').Any(line => line.TrimEnd('\r') == pattern)) return;

        var newline = existing.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var separator = existing.Length > 0 && existing[^1] is not ('\n' or '\r')
            ? newline
            : string.Empty;
        await File.AppendAllTextAsync(
            ignoreFile,
            separator + pattern + newline,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);
    }
}
