namespace CSharpGit.Presentation.ViewModels;

internal enum BranchFolderScope
{
    Local,
    Remote
}

internal sealed record BranchFolderInfo(
    BranchFolderScope Scope,
    string Prefix,
    string? RemoteName);
