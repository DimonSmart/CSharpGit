namespace CSharpGit.Presentation.Previewing;

internal sealed class FilePreviewService
{
    private readonly IReadOnlyList<IFilePreviewProvider> _providers =
    [
        new ImageFilePreviewProvider(),
        new TextFilePreviewProvider(),
        new BinaryFilePreviewProvider()
    ];

    internal async Task<FilePreviewContent> LoadAsync(
        string gitPath,
        string localPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gitPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);
        cancellationToken.ThrowIfCancellationRequested();

        var info = new FileInfo(localPath);
        if (!info.Exists) throw new FileNotFoundException("The materialized historical file no longer exists.", localPath);

        var initialBytes = await FilePreviewIo.ReadPrefixAsync(
            localPath,
            FilePreviewLimits.ProbeBytes,
            cancellationToken);
        var probe = new FilePreviewProbe(gitPath, localPath, info.Length, initialBytes);
        var provider = _providers.First(candidate => candidate.CanPreview(probe));
        return await provider.LoadAsync(probe, cancellationToken);
    }
}

internal static class FilePreviewIo
{
    internal static async Task<byte[]> ReadPrefixAsync(
        string path,
        int byteLimit,
        CancellationToken cancellationToken)
    {
        if (byteLimit < 0) throw new ArgumentOutOfRangeException(nameof(byteLimit));
        if (byteLimit == 0) return [];

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var targetLength = (int)Math.Min(stream.Length, byteLimit);
        var buffer = new byte[targetLength];
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0) break;
            offset += read;
        }

        return offset == buffer.Length ? buffer : buffer.AsSpan(0, offset).ToArray();
    }
}
