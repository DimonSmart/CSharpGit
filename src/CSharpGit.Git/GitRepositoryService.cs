using CSharpGit.Application.Abstractions;
using CSharpGit.Application.Exceptions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

internal sealed class GitRepositoryService : IRepositoryService
{
    private readonly GitRepositoryCommandRunner _runner;

    internal GitRepositoryService(GitCommandExecutor executor)
        : this(new GitRepositoryCommandRunner(executor))
    {
    }

    internal GitRepositoryService(GitRepositoryCommandRunner runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public async Task<Repository> OpenAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            throw new RepositoryOpenException("The selected folder does not exist.");

        try
        {
            await _runner.EnsureGitAvailableAsync(cancellationToken);
            var root = await _runner.RunAsync(
                path,
                cancellationToken,
                true,
                "rev-parse",
                "--show-toplevel");
            var gitDirectory = await _runner.RunAsync(
                path,
                cancellationToken,
                true,
                "rev-parse",
                "--absolute-git-dir");
            var commonDirectory = await _runner.RunAsync(
                path,
                cancellationToken,
                true,
                "rev-parse",
                "--path-format=absolute",
                "--git-common-dir");

            return new Repository(
                Path.GetFullPath(path),
                Path.GetFullPath(root),
                Path.GetFullPath(gitDirectory),
                !PathsEqual(gitDirectory, commonDirectory))
            {
                GitCommonDirectory = Path.GetFullPath(commonDirectory)
            };
        }
        catch (RepositoryOpenException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new RepositoryOpenException(
                "Git could not be started. Make sure Git is installed and available through PATH.",
                exception);
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
}
