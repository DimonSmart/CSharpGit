using CSharpGit.Domain;

namespace CSharpGit.Application;

public sealed record RemoteBranchDeletionTarget(
    GitRemote Remote,
    string BranchName,
    GitBranch? LocalBranch);

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

        var remote = remotes
            .Where(candidate => remoteBranch.Name.StartsWith(candidate.Name + "/", StringComparison.Ordinal))
            .OrderByDescending(candidate => candidate.Name.Length)
            .FirstOrDefault()
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
}
