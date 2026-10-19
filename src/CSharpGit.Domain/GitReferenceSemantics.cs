namespace CSharpGit.Domain;

/// <summary>
/// Git identity comparison: never infer a remote from a shortened presentation string.
/// </summary>
public static class GitReferenceSemantics
{
    public static bool IsSameNameRemoteUpstream(
        GitBranch branch,
        IReadOnlyList<GitRemote> configuredRemotes)
    {
        if (string.IsNullOrEmpty(branch.Name)
            || string.IsNullOrEmpty(branch.UpstreamReference))
            return false;

        return configuredRemotes.Any(remote =>
            !string.IsNullOrEmpty(remote.Name)
            && string.Equals(
                branch.UpstreamReference,
                $"refs/remotes/{remote.Name}/{branch.Name}",
                StringComparison.Ordinal));
    }
}
