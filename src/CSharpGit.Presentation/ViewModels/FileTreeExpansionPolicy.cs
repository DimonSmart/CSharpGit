namespace CSharpGit.Presentation.ViewModels;

internal static class FileTreeExpansionPolicy
{
    public const int AutoExpandFileLimit = 100;

    public static bool ShouldExpandByDefault(int fileCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fileCount);
        return fileCount <= AutoExpandFileLimit;
    }
}
