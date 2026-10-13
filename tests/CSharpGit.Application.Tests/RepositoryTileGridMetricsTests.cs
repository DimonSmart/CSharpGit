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
    [Theory]
    [InlineData(535.99, 1, 348)]
    [InlineData(536, 2, 268)]
    [InlineData(668, 2, 334)]
    [InlineData(992, 3, 330.66)]
    public void PreviewSpanAdaptsToTheCurrentGrid(double width, int columns, double cellWidth)
    {
        var metrics = RepositoryTileGridMetrics.Calculate(width);
        Assert.Equal(columns, metrics.Columns);
        Assert.Equal(cellWidth, metrics.CellWidth, 2);
        Assert.Equal(1, RepositoryTileGridMetrics.ColumnSpan(false, metrics.Columns));
        var previewSpan = RepositoryTileGridMetrics.ColumnSpan(true, metrics.Columns);
        Assert.Equal(columns >= 2 ? 2 : 1, previewSpan);
        Assert.Equal(cellWidth - 8, RepositoryTileGridMetrics.VisibleCardWidth(cellWidth, 1), 2);
        if (columns >= 2)
        {
            var visibleWidth = RepositoryTileGridMetrics.VisibleCardWidth(cellWidth, previewSpan);
            Assert.Equal(2 * cellWidth - 8, visibleWidth, 2);
            Assert.True(visibleWidth <= columns * cellWidth);
        }
    }

    [Fact]
    public void PreviewSpanFollowsResizeWithoutRetainingThePreviousLayout()
    {
        var widths = new[] { 500d, 536d, 500d, 992d, 992d };
        var spans = widths.Select(width =>
        {
            var (columns, _) = RepositoryTileGridMetrics.Calculate(width);
            return RepositoryTileGridMetrics.ColumnSpan(true, columns);
        }).ToArray();
        Assert.Equal(new[] { 1, 2, 1, 2, 2 }, spans);
        Assert.Equal(1, RepositoryTileGridMetrics.ColumnSpan(true, 0));
        Assert.Equal(1, RepositoryTileGridMetrics.ColumnSpan(false, 10));
    }

    [Theory]
    [InlineData(72)]
    [InlineData(268)]
    [InlineData(348)]
    public void CompactAndWideWidthsRespectCellGutters(double cellWidth)
    {
        Assert.Equal(cellWidth - 8, RepositoryTileGridMetrics.VisibleCardWidth(cellWidth, 1));
        Assert.Equal(cellWidth * 2 - 8, RepositoryTileGridMetrics.VisibleCardWidth(cellWidth, 2));
    }

    [Theory]
    [InlineData(528, 112, 1200d / 630d, 213.33333333333334)]
    [InlineData(528, 112, 16d / 9d, 199.11111111111111)]
    [InlineData(528, 112, 1, 112)]
    [InlineData(528, 112, 4, 292)] // Reserve 236 DIP for text.
    [InlineData(660, 112, 4, 360)] // Do not consume the entire large tile.
    [InlineData(660, 112, 0.5, 56)]
    public void FullHeightWidePreviewPreservesImageAspectAndRoomForActions(
        double width, double height, double aspectRatio, double expected)
    {
        var mediaWidth = RepositoryTileGridMetrics.WidePreviewWidth(width, height, aspectRatio);
        Assert.Equal(expected, mediaWidth, 2);
        Assert.True(mediaWidth + RepositoryTileGridMetrics.MinimumWideTextWidth <= width);
        Assert.True(mediaWidth <= RepositoryTileGridMetrics.MaximumWidePreviewWidth);
    }

    [Theory]
    [InlineData(double.NaN, 112, 1.9, 0)]
    [InlineData(528, double.PositiveInfinity, 1.9, 0)]
    [InlineData(0, 112, 1.9, 0)]
    [InlineData(-1, 112, 1.9, 0)]
    public void InvalidTileGeometryCannotTriggerImageOverflow(
        double width, double height, double aspectRatio, double expected)
    {
        Assert.Equal(expected, RepositoryTileGridMetrics.WidePreviewWidth(width, height, aspectRatio));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void MissingImageAspectRatioUsesTypicalSocialPreviewWithoutCropping(double aspectRatio)
    {
        Assert.Equal(
            RepositoryTileGridMetrics.WidePreviewWidth(528, 112,
                RepositoryTileGridMetrics.DefaultPreviewAspectRatio),
            RepositoryTileGridMetrics.WidePreviewWidth(528, 112, aspectRatio));
    }

}
