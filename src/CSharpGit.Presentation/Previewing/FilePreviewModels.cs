namespace CSharpGit.Presentation.Previewing;

internal static class FilePreviewLimits
{
    internal const int ProbeBytes = 16 * 1024;
    internal const int TextReadBytes = 2 * 1024 * 1024;
    internal const long ImageDecodeBytes = 32L * 1024 * 1024;
}

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
    long FileSize) : FilePreviewContent(FileSize);

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
