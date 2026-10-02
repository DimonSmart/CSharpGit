namespace CSharpGit.Presentation.Previewing;

internal static class FilePreviewLimits
{
    internal const int ProbeBytes = 16 * 1024;
    internal const int TextReadBytes = 2 * 1024 * 1024;
    internal const int MetadataScanBytes = 4 * 1024 * 1024;
    internal const long ImageDecodeBytes = 32L * 1024 * 1024;
    internal const int MaxImageDimension = 32 * 1024;
    internal const long MaxDecodedPixels = 64L * 1024 * 1024;
}

internal enum ImageFormat
{
    Png,
    Jpeg
}

internal sealed record ImageMetadata(
    ImageFormat Format,
    int Width,
    int Height);

internal sealed record FilePreviewProbe(
    string GitPath,
    string LocalPath,
    long FileSize,
    byte[] InitialBytes);

internal abstract record FilePreviewContent(long FileSize);

internal sealed record TextPreviewContent(
    string Text,
    string? LanguageId,
    bool IsTruncated,
    long FileSize) : FilePreviewContent(FileSize);

internal sealed record ImagePreviewContent(
    string LocalPath,
    long FileSize,
    ImageMetadata? Metadata,
    bool CanRender,
    string? UnavailableReason = null,
    ImageFormat? DetectedFormat = null) : FilePreviewContent(FileSize);

internal sealed record BinaryPreviewContent(
    long FileSize,
    string Message) : FilePreviewContent(FileSize);

internal interface IFilePreviewProvider
{
    bool CanPreview(FilePreviewProbe probe);

    Task<FilePreviewContent> LoadAsync(
        FilePreviewProbe probe,
        CancellationToken cancellationToken);
}
