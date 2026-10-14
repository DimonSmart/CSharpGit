using System.Text;

namespace CSharpGit.Presentation.Previewing;

internal sealed class TextFilePreviewProvider : IFilePreviewProvider
{
    public bool CanPreview(FilePreviewProbe probe) =>
        PreviewTextEncoding.TryDetect(probe.InitialBytes, out _);

    public async Task<FilePreviewContent> LoadAsync(FilePreviewProbe probe, CancellationToken cancellationToken)
    {
        if (!PreviewTextEncoding.TryDetect(probe.InitialBytes, out var encoding))
            return new BinaryPreviewContent(probe.FileSize, "Preview is not available for this binary file.");

        var bytes = await FilePreviewIo.ReadPrefixAsync(
            probe.LocalPath,
            FilePreviewLimits.TextReadBytes,
            cancellationToken);
        var isTruncated = probe.FileSize > bytes.Length;

        try
        {
            var text = encoding.Decode(bytes, flush: !isTruncated);
            return new TextPreviewContent(
                text,
                FileLanguageClassifier.Classify(probe.GitPath),
                isTruncated,
                probe.FileSize);
        }
        catch (DecoderFallbackException)
        {
            return new BinaryPreviewContent(probe.FileSize, "Preview is not available for this binary file.");
        }
    }
}

internal readonly record struct PreviewTextEncoding(Encoding Encoding, int BomLength)
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly UnicodeEncoding StrictUtf16Le = new(false, true, true);
    private static readonly UnicodeEncoding StrictUtf16Be = new(true, true, true);

    internal static bool TryDetect(byte[] bytes, out PreviewTextEncoding result)
    {
        if (HasPrefix(bytes, 0xEF, 0xBB, 0xBF))
            result = new PreviewTextEncoding(StrictUtf8, 3);
        else if (HasPrefix(bytes, 0xFF, 0xFE))
            result = new PreviewTextEncoding(StrictUtf16Le, 2);
        else if (HasPrefix(bytes, 0xFE, 0xFF))
            result = new PreviewTextEncoding(StrictUtf16Be, 2);
        else
        {
            if (bytes.Contains((byte)0))
            {
                result = default;
                return false;
            }
            result = new PreviewTextEncoding(StrictUtf8, 0);
        }

        try
        {
            var chars = result.DecodeToChars(bytes, flush: false);
            if (chars.Length == 0) return true;

            var controls = 0;
            foreach (var character in chars)
            {
                if (char.IsControl(character) && character is not '\r' and not '\n' and not '\t' and not '\f')
                    controls++;
            }

            return controls <= Math.Max(1, chars.Length / 20);
        }
        catch (DecoderFallbackException)
        {
            result = default;
            return false;
        }
    }

    internal string Decode(byte[] bytes, bool flush) => new(DecodeToChars(bytes, flush));

    private char[] DecodeToChars(byte[] bytes, bool flush)
    {
        var offset = Math.Min(BomLength, bytes.Length);
        var decoder = Encoding.GetDecoder();
        var chars = new char[Encoding.GetMaxCharCount(bytes.Length - offset)];
        decoder.Convert(
            bytes,
            offset,
            bytes.Length - offset,
            chars,
            0,
            chars.Length,
            flush,
            out _,
            out var charsUsed,
            out _);
        return chars.AsSpan(0, charsUsed).ToArray();
    }

    private static bool HasPrefix(IReadOnlyList<byte> bytes, params byte[] prefix)
    {
        if (bytes.Count < prefix.Length) return false;
        for (var index = 0; index < prefix.Length; index++)
        {
            if (bytes[index] != prefix[index]) return false;
        }
        return true;
    }
}
