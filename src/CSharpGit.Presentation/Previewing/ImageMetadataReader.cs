using System.Buffers.Binary;

namespace CSharpGit.Presentation.Previewing;

internal enum ImageMetadataReadStatus
{
    Success,
    Unsupported,
    Invalid
}

internal sealed record ImageMetadataReadResult(
    ImageMetadataReadStatus Status,
    ImageMetadata? Metadata,
    ImageFormat? DetectedFormat,
    string? Error = null);

internal static class SharedImageMetadataReader
{
    internal static ImageMetadataReader Instance { get; } = new();
}

internal sealed class ImageMetadataReader
{
    internal ImageFormat? DetectFormat(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8
            && bytes[0] == 0x89
            && bytes[1] == 0x50
            && bytes[2] == 0x4E
            && bytes[3] == 0x47
            && bytes[4] == 0x0D
            && bytes[5] == 0x0A
            && bytes[6] == 0x1A
            && bytes[7] == 0x0A)
            return ImageFormat.Png;

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return ImageFormat.Jpeg;

        return null;
    }

    internal async Task<ImageMetadataReadResult> ReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var signature = new byte[8];
        var signatureLength = await ReadAtMostAsync(stream, signature, cancellationToken);
        var detected = DetectFormat(signature.AsSpan(0, signatureLength));
        if (detected is null)
            return new(ImageMetadataReadStatus.Unsupported, null, null);

        stream.Position = 0;
        return detected.Value switch
        {
            ImageFormat.Png => await ReadPngAsync(stream, cancellationToken),
            ImageFormat.Jpeg => await ReadJpegAsync(stream, cancellationToken),
            _ => new(ImageMetadataReadStatus.Unsupported, null, detected)
        };
    }

    private static async Task<ImageMetadataReadResult> ReadPngAsync(
        FileStream stream,
        CancellationToken cancellationToken)
    {
        var header = new byte[24];
        if (!await ReadExactlyAsync(stream, header, cancellationToken))
            return Invalid(ImageFormat.Png, "PNG header is incomplete.");

        if (header[0] != 0x89 || header[1] != 0x50 || header[2] != 0x4E || header[3] != 0x47
            || header[4] != 0x0D || header[5] != 0x0A || header[6] != 0x1A || header[7] != 0x0A
            || BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8, 4)) != 13
            || header[12] != (byte)'I' || header[13] != (byte)'H'
            || header[14] != (byte)'D' || header[15] != (byte)'R')
            return Invalid(ImageFormat.Png, "PNG IHDR is invalid.");

        var width = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(20, 4));
        if (width is 0 or > int.MaxValue || height is 0 or > int.MaxValue)
            return Invalid(ImageFormat.Png, "PNG dimensions are invalid.");

        return new(
            ImageMetadataReadStatus.Success,
            new ImageMetadata(ImageFormat.Png, (int)width, (int)height),
            ImageFormat.Png);
    }

    private static async Task<ImageMetadataReadResult> ReadJpegAsync(
        FileStream stream,
        CancellationToken cancellationToken)
    {
        var two = new byte[2];
        if (!await ReadExactlyAsync(stream, two, cancellationToken) || two[0] != 0xFF || two[1] != 0xD8)
            return Invalid(ImageFormat.Jpeg, "JPEG SOI marker is invalid.");

        var one = new byte[1];
        long scanned = 2;
        while (scanned < FilePreviewLimits.MetadataScanBytes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var prefix = await ReadByteAsync(stream, one, cancellationToken);
            if (prefix < 0)
                return Invalid(ImageFormat.Jpeg, "JPEG ended before a size-bearing frame header.");
            scanned++;
            if (prefix != 0xFF)
                return Invalid(ImageFormat.Jpeg, "JPEG marker stream is invalid.");

            int marker;
            do
            {
                marker = await ReadByteAsync(stream, one, cancellationToken);
                if (marker < 0)
                    return Invalid(ImageFormat.Jpeg, "JPEG marker is incomplete.");
                scanned++;
            } while (marker == 0xFF);

            if (marker == 0x00)
                return Invalid(ImageFormat.Jpeg, "JPEG marker stream is invalid.");
            if (marker is 0xD8 or 0x01 || marker is >= 0xD0 and <= 0xD7)
                continue;
            if (marker is 0xD9 or 0xDA)
                return Invalid(ImageFormat.Jpeg, "JPEG dimensions were not found before image data.");

            if (!await ReadExactlyAsync(stream, two, cancellationToken))
                return Invalid(ImageFormat.Jpeg, "JPEG segment length is incomplete.");
            scanned += 2;
            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(two);
            if (segmentLength < 2)
                return Invalid(ImageFormat.Jpeg, "JPEG segment length is invalid.");

            var payloadLength = segmentLength - 2;
            if (payloadLength > FilePreviewLimits.MetadataScanBytes - scanned
                || stream.Position > stream.Length - payloadLength)
                return Invalid(ImageFormat.Jpeg, "JPEG metadata scan limit was exceeded.");

            if (IsStartOfFrame(marker))
            {
                if (payloadLength < 5)
                    return Invalid(ImageFormat.Jpeg, "JPEG frame header is incomplete.");

                var frame = new byte[5];
                if (!await ReadExactlyAsync(stream, frame, cancellationToken))
                    return Invalid(ImageFormat.Jpeg, "JPEG frame header is incomplete.");

                var height = BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(1, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(3, 2));
                if (width == 0 || height == 0)
                    return Invalid(ImageFormat.Jpeg, "JPEG dimensions are invalid.");

                return new(
                    ImageMetadataReadStatus.Success,
                    new ImageMetadata(ImageFormat.Jpeg, width, height),
                    ImageFormat.Jpeg);
            }

            stream.Seek(payloadLength, SeekOrigin.Current);
            scanned += payloadLength;
        }

        return Invalid(ImageFormat.Jpeg, "JPEG metadata scan limit was exceeded.");
    }

    private static bool IsStartOfFrame(int marker) =>
        marker is 0xC0 or 0xC1 or 0xC2 or 0xC3
            or 0xC5 or 0xC6 or 0xC7
            or 0xC9 or 0xCA or 0xCB
            or 0xCD or 0xCE or 0xCF;

    private static ImageMetadataReadResult Invalid(ImageFormat format, string error) =>
        new(ImageMetadataReadStatus.Invalid, null, format, error);

    private static async Task<int> ReadAtMostAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0) break;
            offset += read;
        }
        return offset;
    }

    private static async Task<bool> ReadExactlyAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken) =>
        await ReadAtMostAsync(stream, buffer, cancellationToken) == buffer.Length;

    private static async Task<int> ReadByteAsync(
        Stream stream,
        byte[] oneByteBuffer,
        CancellationToken cancellationToken)
    {
        var read = await stream.ReadAsync(oneByteBuffer.AsMemory(0, 1), cancellationToken);
        return read == 0 ? -1 : oneByteBuffer[0];
    }
}

internal static class ImagePreviewSafety
{
    internal static bool CanRender(long fileSize, ImageMetadata metadata, out string? reason)
    {
        if (fileSize > FilePreviewLimits.ImageDecodeBytes)
        {
            reason = "Image exceeds the preview size limit.";
            return false;
        }

        if (metadata.Width > FilePreviewLimits.MaxImageDimension
            || metadata.Height > FilePreviewLimits.MaxImageDimension
            || !IsPixelCountWithinLimit(metadata.Width, metadata.Height, FilePreviewLimits.MaxDecodedPixels))
        {
            reason = "Image dimensions exceed the preview safety limit.";
            return false;
        }

        reason = null;
        return true;
    }

    internal static bool IsPixelCountWithinLimit(long width, long height, long limit)
    {
        if (width <= 0 || height <= 0 || limit < 0) return false;
        if (width > long.MaxValue / height) return false;
        return width * height <= limit;
    }
}
