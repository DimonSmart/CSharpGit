using CSharpGit.Application.Abstractions;

namespace CSharpGit.Presentation.Previewing;

internal enum ImageDiffSideState
{
    Image,
    Missing,
    PreviewUnavailable
}

internal sealed record ImageDiffSideContent(
    ImageDiffSideState State,
    string? LocalPath,
    long FileSize,
    ImageMetadata? Metadata,
    ImageFormat? DetectedFormat,
    string? UnavailableReason = null);

internal sealed record ImageMetadataComparison(
    bool DimensionsChanged,
    bool DimensionsSwapped,
    bool FormatChanged,
    bool FileSizeChanged,
    bool RequiredMetadataUnchanged)
{
    internal static ImageMetadataComparison Compare(
        ImageDiffSideContent original,
        ImageDiffSideContent changed)
    {
        ArgumentNullException.ThrowIfNull(original.Metadata);
        ArgumentNullException.ThrowIfNull(changed.Metadata);

        var oldMetadata = original.Metadata;
        var newMetadata = changed.Metadata;
        var dimensionsChanged =
            oldMetadata.Width != newMetadata.Width || oldMetadata.Height != newMetadata.Height;
        var dimensionsSwapped =
            dimensionsChanged
            && oldMetadata.Width != oldMetadata.Height
            && oldMetadata.Width == newMetadata.Height
            && oldMetadata.Height == newMetadata.Width;
        var formatChanged = oldMetadata.Format != newMetadata.Format;
        var fileSizeChanged = original.FileSize != changed.FileSize;

        return new(
            dimensionsChanged,
            dimensionsSwapped,
            formatChanged,
            fileSizeChanged,
            !dimensionsChanged && !formatChanged && !fileSizeChanged);
    }
}

internal sealed record ImageDiffPreviewContent(
    ImageDiffSideContent Original,
    ImageDiffSideContent Changed,
    ImageMetadataComparison? Changes);

internal sealed record ImageDiffLoadResult(
    DiffFileVersionPair Versions,
    ImageDiffPreviewContent? Content);
