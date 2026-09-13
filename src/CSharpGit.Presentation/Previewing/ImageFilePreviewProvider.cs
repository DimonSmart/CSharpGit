namespace CSharpGit.Presentation.Previewing;

internal sealed class ImageFilePreviewProvider : IFilePreviewProvider
{
    public bool CanPreview(FilePreviewProbe probe) =>
        IsPng(probe.InitialBytes) || IsJpeg(probe.InitialBytes);

    public Task<FilePreviewContent> LoadAsync(FilePreviewProbe probe, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FilePreviewContent result = probe.FileSize > FilePreviewLimits.ImageDecodeBytes
            ? new BinaryPreviewContent(
                probe.FileSize,
                "Image preview is not available because the file exceeds the preview size limit.")
            : new ImagePreviewContent(probe.LocalPath, probe.FileSize);
        return Task.FromResult(result);
    }

    private static bool IsPng(IReadOnlyList<byte> bytes) =>
        bytes.Count >= 8
        && bytes[0] == 0x89
        && bytes[1] == 0x50
        && bytes[2] == 0x4E
        && bytes[3] == 0x47
        && bytes[4] == 0x0D
        && bytes[5] == 0x0A
        && bytes[6] == 0x1A
        && bytes[7] == 0x0A;

    private static bool IsJpeg(IReadOnlyList<byte> bytes) =>
        bytes.Count >= 3
        && bytes[0] == 0xFF
        && bytes[1] == 0xD8
        && bytes[2] == 0xFF;
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
