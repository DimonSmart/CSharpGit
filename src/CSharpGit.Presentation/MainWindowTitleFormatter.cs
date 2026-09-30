using CSharpGit.Domain;

namespace CSharpGit.Presentation;

internal static class MainWindowTitleFormatter
{
    private const string ApplicationTitle = "CSharpGit";

    internal static string Format(
        Repository? repository,
        string? currentBranchName,
        string? currentHeadCommit,
        bool isDetachedHead)
    {
        if (repository is null)
            return ApplicationTitle;

        var repositoryName = GetRepositoryName(repository.RepositoryRoot);
        if (string.IsNullOrWhiteSpace(repositoryName))
            return ApplicationTitle;

        if (!isDetachedHead && !string.IsNullOrWhiteSpace(currentBranchName))
            return $"{repositoryName} / {currentBranchName} — {ApplicationTitle}";

        if (isDetachedHead && !string.IsNullOrWhiteSpace(currentHeadCommit))
        {
            var shortCommit = currentHeadCommit[..Math.Min(10, currentHeadCommit.Length)];
            return $"{repositoryName} / detached @ {shortCommit} — {ApplicationTitle}";
        }

        return $"{repositoryName} — {ApplicationTitle}";
    }

    private static string GetRepositoryName(string repositoryRoot)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(repositoryRoot);
        var name = Path.GetFileName(normalizedRoot);
        return string.IsNullOrWhiteSpace(name) ? normalizedRoot : name;
    }
}
