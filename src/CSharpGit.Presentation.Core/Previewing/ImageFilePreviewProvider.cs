namespace CSharpGit.Presentation.Previewing;

internal sealed class ImageFilePreviewProvider(ImageMetadataReader metadataReader) : IFilePreviewProvider
{
    public bool CanPreview(FilePreviewProbe probe) =>
        metadataReader.DetectFormat(probe.InitialBytes) is not null;

    public async Task<FilePreviewContent> LoadAsync(
        FilePreviewProbe probe,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await metadataReader.ReadAsync(probe.LocalPath, cancellationToken);
        if (result.Status == ImageMetadataReadStatus.Unsupported)
        {
            return new BinaryPreviewContent(
                probe.FileSize,
                "Preview is not available for this binary file.");
        }

        if (result.Status != ImageMetadataReadStatus.Success || result.Metadata is null)
        {
            return new ImagePreviewContent(
                probe.LocalPath,
                probe.FileSize,
                null,
                false,
                "Preview unavailable because the image header is invalid or corrupt.",
                result.DetectedFormat);
        }

        var canRender = ImagePreviewSafety.CanRender(probe.FileSize, result.Metadata, out var reason);
        return new ImagePreviewContent(
            probe.LocalPath,
            probe.FileSize,
            result.Metadata,
            canRender,
            reason,
            result.Metadata.Format);
    }
}

internal sealed class BinaryFilePreviewProvider : IFilePreviewProvider
{
    public bool CanPreview(FilePreviewProbe probe) => true;

    public Task<FilePreviewContent> LoadAsync(FilePreviewProbe probe, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<FilePreviewContent>(
            new BinaryPreviewContent(probe.FileSize, "Preview is not available for this binary file."));
    }
}
