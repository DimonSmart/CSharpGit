namespace CSharpGit.Presentation.Controls;

/// <summary>Presentation-only width metrics for the two repository launcher sections.</summary>
internal static class RepositoryTileGridMetrics
{
    internal const double MinCardWidth = 260;
    internal const double MaxCardWidth = 340;
    internal const double Gap = 8;

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
