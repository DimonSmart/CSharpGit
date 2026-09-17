using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

internal static class BranchMovePlanning
{
    public static GitBranch? GetSourceBranch(RepositoryTreeNodeKind kind, object? value) =>
        kind == RepositoryTreeNodeKind.LocalBranch ? value as GitBranch : null;

    public static bool TryGetDestinationPrefix(
        RepositoryTreeNodeKind kind,
        string key,
        object? value,
        out string destinationPrefix)
    {
        if (kind == RepositoryTreeNodeKind.Group
            && string.Equals(key, RepositoryTreeDescriptorBuilder.BranchesRootKey, StringComparison.Ordinal))
        {
            destinationPrefix = string.Empty;
            return true;
        }

        if (kind == RepositoryTreeNodeKind.BranchFolder
            && value is BranchFolderInfo { Scope: BranchFolderScope.Local } folder)
        {
            destinationPrefix = folder.Prefix;
            return true;
        }

        destinationPrefix = string.Empty;
        return false;
    }

    public static string BuildTargetName(GitBranch sourceBranch, string destinationPrefix)
    {
        ArgumentNullException.ThrowIfNull(sourceBranch);
        ArgumentNullException.ThrowIfNull(destinationPrefix);

        var oldName = sourceBranch.Name;
        var separatorIndex = oldName.LastIndexOf('/');
        var sourceLeafName = separatorIndex < 0
            ? oldName
            : oldName[(separatorIndex + 1)..];

        return destinationPrefix.Length == 0
            ? sourceLeafName
            : $"{destinationPrefix}/{sourceLeafName}";
    }
}
