using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

internal sealed record RepositoryTreeDescriptor(
    string Key,
    RepositoryTreeNodeKind Kind,
    string Name,
    string? ReferenceName = null,
    object? Value = null,
    bool IsCurrent = false,
    bool DefaultExpanded = false,
    string? AssociatedWorktreePath = null,
    IReadOnlyList<RepositoryTreeDescriptor>? ChildNodes = null)
{
    public IReadOnlyList<RepositoryTreeDescriptor> Children { get; } = ChildNodes ?? Array.Empty<RepositoryTreeDescriptor>();
}

internal static class RepositoryTreeDescriptorBuilder
{
    public const string WorktreesRootKey = "group:worktrees";
    public const string BranchesRootKey = "group:branches";
    public const string RemotesRootKey = "group:remotes";
    public const string TagsRootKey = "group:tags";
    public const string StashesRootKey = "group:stashes";

    public static IReadOnlyList<RepositoryTreeDescriptor> Build(
        IReadOnlyList<GitBranch> localBranches,
        IReadOnlyList<GitBranch> remoteBranches,
        IReadOnlyList<GitRemote> remotes,
        IReadOnlyList<GitTag> tags,
        IReadOnlyList<GitStash> stashes,
        IReadOnlyList<WorktreeInfo> worktrees)
    {
        var worktreesByBranch = worktrees
            .Where(worktree => !string.IsNullOrWhiteSpace(worktree.Branch))
            .GroupBy(worktree => worktree.Branch!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Path, StringComparer.Ordinal);

        return
        [
            BuildWorktreesRoot(worktrees),
            new RepositoryTreeDescriptor(
                BranchesRootKey,
                RepositoryTreeNodeKind.Group,
                "Branches",
                DefaultExpanded: true,
                ChildNodes: BuildBranchHierarchy(localBranches, remoteName: null, worktreesByBranch)),
            new RepositoryTreeDescriptor(
                RemotesRootKey,
                RepositoryTreeNodeKind.Group,
                "Remotes",
                ChildNodes: BuildRemoteNodes(remoteBranches, remotes)),
            new RepositoryTreeDescriptor(
                TagsRootKey,
                RepositoryTreeNodeKind.Group,
                "Tags",
                ChildNodes: tags.Select(tag => new RepositoryTreeDescriptor(
                    $"tag:{tag.Name}",
                    RepositoryTreeNodeKind.Tag,
                    tag.Name,
                    tag.Name,
                    tag)).ToArray()),
            new RepositoryTreeDescriptor(
                StashesRootKey,
                RepositoryTreeNodeKind.Group,
                "Stashes",
                ChildNodes: stashes.Select(stash => new RepositoryTreeDescriptor(
                    $"stash:{stash.Commit}",
                    RepositoryTreeNodeKind.Stash,
                    $"{stash.Name}: {stash.Message}",
                    stash.Commit,
                    stash)).ToArray())
        ];
    }

    public static RepositoryTreeDescriptor BuildWorktreesRoot(IReadOnlyList<WorktreeInfo> worktrees) =>
        new(
            WorktreesRootKey,
            RepositoryTreeNodeKind.Group,
            "Worktrees",
            DefaultExpanded: true,
            ChildNodes: worktrees
                .OrderByDescending(worktree => worktree.IsPrimary)
                .ThenBy(WorktreePresentation.GetPrimaryLabel, StringComparer.OrdinalIgnoreCase)
                .ThenBy(worktree => worktree.Path, StringComparer.OrdinalIgnoreCase)
                .Select(worktree => new RepositoryTreeDescriptor(
                    $"worktree:{worktree.Path}",
                    RepositoryTreeNodeKind.Worktree,
                    BuildWorktreeName(worktree),
                    worktree.Branch ?? worktree.Head,
                    worktree,
                    worktree.IsCurrent))
                .ToArray());

    private static IReadOnlyList<RepositoryTreeDescriptor> BuildRemoteNodes(
        IReadOnlyList<GitBranch> remoteBranches,
        IReadOnlyList<GitRemote> remotes)
    {
        var result = new List<RepositoryTreeDescriptor>(remotes.Count);
        foreach (var remote in remotes.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var prefix = remote.Name + "/";
            var branches = remoteBranches
                .Where(branch => branch.Name.StartsWith(prefix, StringComparison.Ordinal))
                .ToArray();

            result.Add(new RepositoryTreeDescriptor(
                $"remote:{remote.Name}",
                RepositoryTreeNodeKind.Remote,
                remote.Name,
                Value: remote,
                ChildNodes: BuildBranchHierarchy(branches, remote.Name, null)));
        }

        return result;
    }

    private static IReadOnlyList<RepositoryTreeDescriptor> BuildBranchHierarchy(
        IReadOnlyList<GitBranch> branches,
        string? remoteName,
        IReadOnlyDictionary<string, string>? worktreesByBranch)
    {
        var root = new BranchFolderBuilder(string.Empty, string.Empty, null);
        var remotePrefix = remoteName is null ? null : remoteName + "/";

        foreach (var branch in branches)
        {
            var displayName = remotePrefix is not null && branch.Name.StartsWith(remotePrefix, StringComparison.Ordinal)
                ? branch.Name[remotePrefix.Length..]
                : branch.Name;
            var parts = displayName.Split('/', StringSplitOptions.None);
            var folder = root;

            for (var index = 0; index < parts.Length - 1; index++)
            {
                var prefix = string.Join('/', parts.Take(index + 1));
                if (!folder.Folders.TryGetValue(parts[index], out var childFolder))
                {
                    var key = remoteName is null
                        ? $"branch-folder:local:{prefix}"
                        : $"branch-folder:remote:{remoteName}:{prefix}";
                    var info = new BranchFolderInfo(
                        remoteName is null ? BranchFolderScope.Local : BranchFolderScope.Remote,
                        prefix,
                        remoteName);
                    childFolder = new BranchFolderBuilder(parts[index], key, info);
                    folder.Folders.Add(parts[index], childFolder);
                }

                folder = childFolder;
            }

            var leafName = parts[^1];
            folder.Leaves.Add(new RepositoryTreeDescriptor(
                remoteName is null ? $"local-branch:{branch.Name}" : $"remote-branch:{branch.Name}",
                remoteName is null ? RepositoryTreeNodeKind.LocalBranch : RepositoryTreeNodeKind.RemoteBranch,
                leafName,
                branch.Name,
                branch,
                branch.IsCurrent,
                AssociatedWorktreePath: remoteName is null ? worktreesByBranch?.GetValueOrDefault(branch.Name) : null));
        }

        return BuildBranchChildren(root);
    }

    private static IReadOnlyList<RepositoryTreeDescriptor> BuildBranchChildren(BranchFolderBuilder folder)
    {
        var result = new List<RepositoryTreeDescriptor>(folder.Leaves.Count + folder.Folders.Count);
        result.AddRange(folder.Leaves.OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase));
        result.AddRange(folder.Folders.Values
            .OrderBy(child => child.Name, StringComparer.OrdinalIgnoreCase)
            .Select(child => new RepositoryTreeDescriptor(
                child.Key,
                RepositoryTreeNodeKind.BranchFolder,
                child.Name,
                Value: child.Info,
                ChildNodes: BuildBranchChildren(child))));
        return result;
    }

    private static string BuildWorktreeName(WorktreeInfo worktree)
    {
        var primary = worktree.Branch ?? ShortHead(worktree.Head);
        var states = new List<string>();
        if (worktree.IsDetached) states.Add("detached");
        if (worktree.IsLocked)
            states.Add(string.IsNullOrWhiteSpace(worktree.LockReason) ? "locked" : $"locked: {worktree.LockReason}");
        if (worktree.IsPrunable) states.Add("prunable");
        var state = states.Count == 0 ? string.Empty : $" [{string.Join(", ", states)}]";
        return $"{primary}{state} — {worktree.Path}";
    }

    private static string ShortHead(string head) =>
        string.IsNullOrWhiteSpace(head) ? "unknown" : head[..Math.Min(8, head.Length)];

    private sealed class BranchFolderBuilder(string name, string key, BranchFolderInfo? info)
    {
        public string Name { get; } = name;
        public string Key { get; } = key;
        public BranchFolderInfo? Info { get; } = info;
        public List<RepositoryTreeDescriptor> Leaves { get; } = [];
        public Dictionary<string, BranchFolderBuilder> Folders { get; } = new(StringComparer.Ordinal);
    }
}
