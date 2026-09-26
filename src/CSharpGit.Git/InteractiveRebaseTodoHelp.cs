namespace CSharpGit.Git;

internal static class InteractiveRebaseTodoHelp
{
    internal static readonly string Text = string.Join(
        Environment.NewLine,
        [
            "# Commands:",
            "# p, pick <commit>   = use commit",
            "# r, reword <commit> = use commit, but edit the commit message",
            "# e, edit <commit>   = use commit, but stop for amending",
            "# s, squash <commit> = combine with previous commit and edit the commit message",
            "# f, fixup <commit>  = combine with previous commit and discard this commit message",
            "# x, exec <command>  = run command using shell",
            "# b, break           = stop here; continue later",
            "# d, drop <commit>   = remove commit",
            "#",
            "# Lines are executed from top to bottom.",
            "# Reorder commit lines to reorder commits.",
            "# Removing a commit line drops that commit.",
            "# Lines beginning with # and empty lines are ignored."
        ]);
}
