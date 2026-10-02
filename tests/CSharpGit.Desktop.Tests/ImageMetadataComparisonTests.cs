using CSharpGit.Presentation.Previewing;

namespace CSharpGit.Desktop.Tests;

public sealed class ImageMetadataComparisonTests
{
    [Fact]
    public void DetectsResizeAndSwappedNonSquareDimensions()
    {
        var original = Side(ImageFormat.Png, 1200, 800, 1000);
        var changed = Side(ImageFormat.Png, 800, 1200, 800);

        var comparison = ImageMetadataComparison.Compare(original, changed);

        Assert.True(comparison.DimensionsChanged);
        Assert.True(comparison.DimensionsSwapped);
        Assert.True(comparison.FileSizeChanged);
        Assert.False(comparison.FormatChanged);
        Assert.False(comparison.RequiredMetadataUnchanged);
    }

    [Fact]
    public void DetectsFormatAndFileSizeChangesIndependently()
    {
        var original = Side(ImageFormat.Jpeg, 640, 480, 500);
        var changed = Side(ImageFormat.Png, 640, 480, 750);

        var comparison = ImageMetadataComparison.Compare(original, changed);

        Assert.False(comparison.DimensionsChanged);
        Assert.False(comparison.DimensionsSwapped);
        Assert.True(comparison.FormatChanged);
        Assert.True(comparison.FileSizeChanged);
    }

    [Fact]
    public void ReportsRequiredMetadataUnchanged()
    {
        var original = Side(ImageFormat.Png, 640, 480, 500);
        var changed = Side(ImageFormat.Png, 640, 480, 500);

        var comparison = ImageMetadataComparison.Compare(original, changed);

        Assert.True(comparison.RequiredMetadataUnchanged);
        Assert.False(comparison.DimensionsChanged);
        Assert.False(comparison.FormatChanged);
        Assert.False(comparison.FileSizeChanged);
    }

    [Fact]
    public void MissingStatesRepresentAddedAndDeletedImagesWithoutFakeBitmap()
    {
        var addedOriginal = new ImageDiffSideContent(
            ImageDiffSideState.Missing, null, 0, null, null, "Not present before this change");
        var deletedChanged = new ImageDiffSideContent(
            ImageDiffSideState.Missing, null, 0, null, null, "Deleted by this change");

        Assert.Null(addedOriginal.LocalPath);
        Assert.Null(deletedChanged.LocalPath);
        Assert.Equal(ImageDiffSideState.Missing, addedOriginal.State);
        Assert.Equal(ImageDiffSideState.Missing, deletedChanged.State);
    }

    private static ImageDiffSideContent Side(
        ImageFormat format,
        int width,
        int height,
        long fileSize) =>
        new(
            ImageDiffSideState.Image,
            "image",
            fileSize,
            new ImageMetadata(format, width, height),
            format);
}
