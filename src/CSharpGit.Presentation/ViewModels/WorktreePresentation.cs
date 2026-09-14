using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

internal static class WorktreePresentation
{
    public static string GetPrimaryLabel(WorktreeInfo worktree)
    {
        ArgumentNullException.ThrowIfNull(worktree);

        if (!string.IsNullOrWhiteSpace(worktree.Branch)) return worktree.Branch;
        return GetDirectoryName(worktree.Path) ?? ShortHead(worktree.Head) ?? "detached";
    }

    public static string GetStateIndicator(WorktreeInfo worktree)
    {
        ArgumentNullException.ThrowIfNull(worktree);

        var states = GetStates(worktree, titleCase: false);
        return states.Count == 0 ? string.Empty : $"[{string.Join(", ", states)}]";
    }

    public static string BuildToolTip(WorktreeInfo worktree)
    {
        ArgumentNullException.ThrowIfNull(worktree);

        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(worktree.Branch)) lines.Add($"Branch: {worktree.Branch}");
        lines.Add($"Path: {worktree.Path}");

        var states = GetStates(worktree, titleCase: true);
        if (states.Count > 0) lines.Add($"State: {string.Join(", ", states)}");
        if (worktree.IsLocked && !string.IsNullOrWhiteSpace(worktree.LockReason))
            lines.Add($"Lock reason: {worktree.LockReason}");

        return string.Join(Environment.NewLine, lines);
    }

    public static string? GetBranchNameForCopy(WorktreeInfo worktree)
    {
        ArgumentNullException.ThrowIfNull(worktree);
        return string.IsNullOrWhiteSpace(worktree.Branch) ? null : worktree.Branch;
    }

    public static string GetPathForCopy(WorktreeInfo worktree)
    {
        ArgumentNullException.ThrowIfNull(worktree);
        return worktree.Path;
    }

    private static string? GetDirectoryName(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var trimmed = path.TrimEnd('/', '\\');
        if (trimmed.Length == 0) return null;

        var separator = Math.Max(trimmed.LastIndexOf('/'), trimmed.LastIndexOf('\\'));
        var name = separator >= 0 ? trimmed[(separator + 1)..] : trimmed;
        if (name.Length == 2 && char.IsLetter(name[0]) && name[1] == ':') return null;
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static string? ShortHead(string head) =>
        string.IsNullOrWhiteSpace(head) ? null : head[..Math.Min(8, head.Length)];

    private static List<string> GetStates(WorktreeInfo worktree, bool titleCase)
    {
        var states = new List<string>(3);
        if (worktree.IsDetached) states.Add(titleCase ? "Detached" : "detached");
        if (worktree.IsLocked) states.Add(titleCase ? "Locked" : "locked");
        if (worktree.IsPrunable) states.Add(titleCase ? "Prunable" : "prunable");
        return states;
    }
}
