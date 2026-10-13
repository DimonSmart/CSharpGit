namespace CSharpGit.Presentation.Controls;

/// <summary>Presentation-only width metrics for the two repository launcher sections.</summary>
internal static class RepositoryTileGridMetrics
{
    internal const double MinCardWidth = 260;
    internal const double MaxCardWidth = 340;
    internal const double Gap = 8;
    internal const double DefaultPreviewAspectRatio = 1200d / 630d;
    internal const double MinimumWideTextWidth = 236;
    internal const double MaximumWidePreviewWidth = 360;

    /// <summary>
    /// Reserve at least 220 DIP of content and 16 DIP of insets for the text
    /// column. With an ordinary Social Preview this gives full-height imagery
    /// without letterboxing, an extra media frame or cropping.
    /// </summary>
    internal static double WidePreviewWidth(double cardWidth, double cardHeight, double aspectRatio)
    {
        if (!double.IsFinite(cardWidth) || cardWidth <= 0
            || !double.IsFinite(cardHeight) || cardHeight <= 0)
            return 0;

        if (!double.IsFinite(aspectRatio) || aspectRatio <= 0)
            aspectRatio = DefaultPreviewAspectRatio;

        var available = Math.Max(0, cardWidth - MinimumWideTextWidth);
        return Math.Min(Math.Min(cardHeight * aspectRatio, MaximumWidePreviewWidth), available);
    }


    internal static int ColumnSpan(bool hasReadyPreview, int columns) =>
        hasReadyPreview && columns >= 2 ? 2 : 1;

    internal static double VisibleCardWidth(double cellWidth, int span) =>
        Math.Max(0, cellWidth * span - Gap);

    /// <summary>
    /// The trailing gutter belongs to the cell, so a full row always fits inside the
    /// measured GridView width. A partial last row never stretches its cards.
    /// </summary>
    internal static (int Columns, double CellWidth) Calculate(double width)
    {
        if (!double.IsFinite(width) || width <= 0)
            return (0, 0);

        var columns = (int)Math.Min(int.MaxValue,
            Math.Max(1, Math.Floor(width / (MinCardWidth + Gap))));
        var widthPerCell = Math.Floor(width / columns * 100) / 100;
        var cellWidth = Math.Min(MaxCardWidth + Gap, widthPerCell);
        return (columns, cellWidth);
    }
}
