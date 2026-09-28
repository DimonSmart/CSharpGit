using CSharpGit.Domain;

namespace CSharpGit.Application;

public sealed record RemoteBranchDeletionTarget(
    GitRemote Remote,
    string BranchName,
    GitBranch? LocalBranch);

public sealed record LocalBranchRemoteDeletionTarget(
    GitRemote Remote,
    string BranchName);

public static class BranchDeletionResolver
{
    public static RemoteBranchDeletionTarget Resolve(
        GitBranch remoteBranch,
        IEnumerable<GitBranch> localBranches,
        IEnumerable<GitRemote> remotes)
    {
        ArgumentNullException.ThrowIfNull(remoteBranch);
        ArgumentNullException.ThrowIfNull(localBranches);
        ArgumentNullException.ThrowIfNull(remotes);

        var configuredRemotes = remotes.ToArray();
        var remote = ResolveConfiguredRemote(remoteBranch.Name, configuredRemotes)
            ?? throw new InvalidOperationException($"Remote branch '{remoteBranch.Name}' does not belong to a configured remote.");

        var branchName = remoteBranch.Name[(remote.Name.Length + 1)..];
        if (string.IsNullOrEmpty(branchName))
            throw new InvalidOperationException($"Remote branch '{remoteBranch.Name}' has no branch name.");

        var locals = localBranches.ToArray();
        var localBranch = locals.FirstOrDefault(candidate =>
                              string.Equals(candidate.Upstream, remoteBranch.Name, StringComparison.Ordinal))
                          ?? locals.FirstOrDefault(candidate =>
                              string.Equals(candidate.Name, branchName, StringComparison.Ordinal));

        return new RemoteBranchDeletionTarget(remote, branchName, localBranch);
    }

    public static LocalBranchRemoteDeletionTarget? ResolveRemoteForLocal(
        GitBranch localBranch,
        IEnumerable<GitBranch> remoteBranches,
        IEnumerable<GitRemote> remotes)
    {
        ArgumentNullException.ThrowIfNull(localBranch);
        ArgumentNullException.ThrowIfNull(remoteBranches);
        ArgumentNullException.ThrowIfNull(remotes);

        var configuredRemotes = remotes.ToArray();

        if (!string.IsNullOrWhiteSpace(localBranch.Upstream))
            return ResolveRemoteTarget(localBranch.Upstream, configuredRemotes);

        var matches = remoteBranches
            .Select(candidate => ResolveRemoteTarget(candidate.Name, configuredRemotes))
            .OfType<LocalBranchRemoteDeletionTarget>()
            .Where(candidate => string.Equals(candidate.BranchName, localBranch.Name, StringComparison.Ordinal))
            .GroupBy(candidate => (candidate.Remote.Name, candidate.BranchName))
            .Select(group => group.First())
            .Take(2)
            .ToArray();

        return matches.Length == 1 ? matches[0] : null;
    }

    private static LocalBranchRemoteDeletionTarget? ResolveRemoteTarget(
        string referenceName,
        IReadOnlyCollection<GitRemote> remotes)
    {
        var remote = ResolveConfiguredRemote(referenceName, remotes);
        if (remote is null)
            return null;

        var branchName = referenceName[(remote.Name.Length + 1)..];
        return string.IsNullOrEmpty(branchName)
            ? null
            : new LocalBranchRemoteDeletionTarget(remote, branchName);
    }

    private static GitRemote? ResolveConfiguredRemote(
        string referenceName,
        IEnumerable<GitRemote> remotes) =>
        remotes
            .Where(candidate => referenceName.StartsWith(candidate.Name + "/", StringComparison.Ordinal))
            .OrderByDescending(candidate => candidate.Name.Length)
            .FirstOrDefault();
}
