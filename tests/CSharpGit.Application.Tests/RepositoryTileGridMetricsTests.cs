using CSharpGit.Presentation.Controls;

namespace CSharpGit.Application.Tests;

public sealed class RepositoryTileGridMetricsTests
{
    [Theory]
    [InlineData(double.NaN, 0, 0)]
    [InlineData(double.PositiveInfinity, 0, 0)]
    [InlineData(-10, 0, 0)]
    [InlineData(0, 0, 0)]
    [InlineData(120, 1, 120)]
    [InlineData(400, 1, 348)]
    [InlineData(500, 1, 348)]
    [InlineData(535.99, 1, 348)]
    [InlineData(536, 2, 268)]
    [InlineData(668, 2, 334)]
    [InlineData(992, 3, 330.66)]
    [InlineData(1248, 4, 312)]
    [InlineData(1504, 5, 300.8)]
    [InlineData(1888, 7, 269.71)]
    public void ColumnsAndWidthsFollowUsableViewport(double width, int columns, double expectedCellWidth)
    {
        var actual = RepositoryTileGridMetrics.Calculate(width);
        Assert.Equal(columns, actual.Columns);
        Assert.Equal(expectedCellWidth, actual.CellWidth, precision: 2);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void ExactlyAtTheNextColumnThreshold(int columns)
    {
        var threshold = columns * (RepositoryTileGridMetrics.MinCardWidth + RepositoryTileGridMetrics.Gap);
        Assert.Equal(columns - 1, RepositoryTileGridMetrics.Calculate(threshold - 0.01).Columns);
        var actual = RepositoryTileGridMetrics.Calculate(threshold);
        Assert.Equal(columns, actual.Columns);
        Assert.Equal(RepositoryTileGridMetrics.MinCardWidth + RepositoryTileGridMetrics.Gap, actual.CellWidth);
    }

    [Theory]
    [InlineData(260.4)]
    [InlineData(779.25)]
    [InlineData(1234.56)]
    [InlineData(1504.01)]
    [InlineData(2048.125)]
    public void CellsNeverOverflowAndNeverExceedMaximum(double width)
    {
        var (columns, cellWidth) = RepositoryTileGridMetrics.Calculate(width);
        Assert.True(columns >= 1);
        Assert.InRange(cellWidth, 0, RepositoryTileGridMetrics.MaxCardWidth + RepositoryTileGridMetrics.Gap);
        Assert.True(columns * cellWidth <= width + 0.001);
    }
}
