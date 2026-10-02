using CSharpGit.Presentation.Previewing;

namespace CSharpGit.Desktop.Tests;

public sealed class ImageMetadataReaderTests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAMAAAACCAIAAAASFvFNAAAAEElEQVR4nGP8zwAFTDAGAwATKQED8NgHhAAAAABJRU5ErkJggg==");

    private static readonly byte[] Jpeg = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAARCAACAAMDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwDi6KKK+ZP3E//Z");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReadsDimensionsAndFormatWithoutDependingOnExtension(bool png)
    {
        var path = TempPath();
        try
        {
            await File.WriteAllBytesAsync(path, png ? Png : Jpeg);
            var result = await new ImageMetadataReader().ReadAsync(path, CancellationToken.None);

            Assert.Equal(ImageMetadataReadStatus.Success, result.Status);
            Assert.Equal(png ? ImageFormat.Png : ImageFormat.Jpeg, result.Metadata!.Format);
            Assert.Equal(3, result.Metadata.Width);
            Assert.Equal(2, result.Metadata.Height);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SignatureWithoutRequiredHeaderIsInvalidRatherThanSuccessfulImage()
    {
        var path = TempPath();
        try
        {
            await File.WriteAllBytesAsync(path, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
            var result = await new ImageMetadataReader().ReadAsync(path, CancellationToken.None);

            Assert.Equal(ImageMetadataReadStatus.Invalid, result.Status);
            Assert.Equal(ImageFormat.Png, result.DetectedFormat);
            Assert.Null(result.Metadata);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task TruncatedPngWithReadableIhdrIsInvalid()
    {
        var path = TempPath();
        try
        {
            await File.WriteAllBytesAsync(path, Png[..33]);
            var result = await new ImageMetadataReader().ReadAsync(path, CancellationToken.None);

            Assert.Equal(ImageMetadataReadStatus.Invalid, result.Status);
            Assert.Equal(ImageFormat.Png, result.DetectedFormat);
            Assert.Null(result.Metadata);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task JpegWithoutEoiIsInvalidEvenWhenFrameMetadataExists()
    {
        var path = TempPath();
        try
        {
            await File.WriteAllBytesAsync(path, Jpeg[..^2]);
            var result = await new ImageMetadataReader().ReadAsync(path, CancellationToken.None);

            Assert.Equal(ImageMetadataReadStatus.Invalid, result.Status);
            Assert.Equal(ImageFormat.Jpeg, result.DetectedFormat);
            Assert.Null(result.Metadata);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task UnsupportedBinaryIsNotAnImage()
    {
        var path = TempPath();
        try
        {
            await File.WriteAllBytesAsync(path, [0x00, 0x01, 0x02, 0x03]);
            var result = await new ImageMetadataReader().ReadAsync(path, CancellationToken.None);

            Assert.Equal(ImageMetadataReadStatus.Unsupported, result.Status);
            Assert.Null(result.DetectedFormat);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PixelSafetyRejectsOverflowAndDecodedLimit()
    {
        Assert.False(ImagePreviewSafety.IsPixelCountWithinLimit(long.MaxValue, 2, long.MaxValue));
        Assert.False(ImagePreviewSafety.IsPixelCountWithinLimit(
            FilePreviewLimits.MaxDecodedPixels,
            2,
            FilePreviewLimits.MaxDecodedPixels));
        Assert.True(ImagePreviewSafety.IsPixelCountWithinLimit(3, 2, FilePreviewLimits.MaxDecodedPixels));
    }

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"csharpgit-image-metadata-{Guid.NewGuid():N}");
}
