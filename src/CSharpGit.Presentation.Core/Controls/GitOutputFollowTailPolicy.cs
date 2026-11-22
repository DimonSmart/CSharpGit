namespace CSharpGit.Presentation.Controls;

/// <summary>Viewport policy shared by compact feedback and Git Console output.</summary>
internal static class GitOutputFollowTailPolicy
{
    internal const double BottomTolerance = 8;

    internal static bool IsNearBottom(double verticalOffset, double scrollableHeight) =>
        double.IsFinite(verticalOffset) &&
        double.IsFinite(scrollableHeight) &&
        scrollableHeight - verticalOffset <= BottomTolerance;

    internal static bool ShouldDeferUpdate(bool hasFocus, int selectionLength) =>
        hasFocus && selectionLength > 0;
}
