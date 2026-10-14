using CSharpGit.Domain;

namespace CSharpGit.Presentation;

internal enum DiffContentPresentation
{
    Text,
    Binary,
    NoTextualPatch
}

internal static class DiffPresentationResolver
{
    public static DiffContentPresentation Resolve(FileDiff diff, int displayedLineCount)
    {
        ArgumentNullException.ThrowIfNull(diff);
        if (diff.IsBinary) return DiffContentPresentation.Binary;
        return displayedLineCount > 0
            ? DiffContentPresentation.Text
            : DiffContentPresentation.NoTextualPatch;
    }
}
