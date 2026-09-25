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
            var discovery = await _runner.RunAsync(
                path,
                cancellationToken,
                true,
                "rev-parse",
                "--path-format=absolute",
                "--show-toplevel",
                "--absolute-git-dir",
                "--git-common-dir");
            var values = discovery
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (values.Length != 3)
                throw new RepositoryOpenException("Git returned an unexpected repository discovery result.");

            var root = Path.GetFullPath(values[0]);
            var gitDirectory = Path.GetFullPath(values[1]);
            var commonDirectory = Path.GetFullPath(values[2]);
            return new Repository(
                Path.GetFullPath(path),
                root,
                gitDirectory,
                !PathsEqual(gitDirectory, commonDirectory))
            {
                GitCommonDirectory = commonDirectory
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
