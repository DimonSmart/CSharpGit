using System.Text;
using CSharpGit.Presentation.Previewing;

namespace CSharpGit.Desktop.Tests;

public sealed class FilePreviewServiceTests
{
    [Theory]
    [InlineData("code.cs", "csharp")]
    [InlineData("data.json", "json")]
    [InlineData("script.ps1", "powershell")]
    [InlineData("config.yaml", "yaml")]
    public async Task Utf8TextUsesLanguageMetadata(string gitPath, string languageId)
    {
        var result = await PreviewAsync(gitPath, Encoding.UTF8.GetBytes("hello\nworld"));

        var text = Assert.IsType<TextPreviewContent>(result);
        Assert.Equal(languageId, text.LanguageId);
        Assert.Equal("hello\nworld", text.Text);
        Assert.False(text.IsTruncated);
    }

    [Fact]
    public async Task UnknownExtensionWithValidUtf8IsText()
    {
        var result = await PreviewAsync("README.unknown", Encoding.UTF8.GetBytes("plain text"));

        var text = Assert.IsType<TextPreviewContent>(result);
        Assert.Null(text.LanguageId);
    }

    [Fact]
    public async Task Utf8BomIsText()
    {
        var result = await PreviewAsync("note.txt", WithPreamble(new UTF8Encoding(true), "hello"));

        Assert.Equal("hello", Assert.IsType<TextPreviewContent>(result).Text);
    }

    [Fact]
    public async Task Utf16LittleEndianBomIsText()
    {
        var encoding = new UnicodeEncoding(false, true, true);
        var result = await PreviewAsync("note.txt", WithPreamble(encoding, "hello"));

        Assert.Equal("hello", Assert.IsType<TextPreviewContent>(result).Text);
    }

    [Fact]
    public async Task Utf16BigEndianBomIsText()
    {
        var encoding = new UnicodeEncoding(true, true, true);
        var result = await PreviewAsync("note.txt", WithPreamble(encoding, "hello"));

        Assert.Equal("hello", Assert.IsType<TextPreviewContent>(result).Text);
    }

    [Fact]
    public async Task ValidImageWithUnrelatedExtensionUsesImagePreviewAndMetadata()
    {
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAMAAAACCAIAAAASFvFNAAAAEElEQVR4nGP8zwAFTDAGAwATKQED8NgHhAAAAABJRU5ErkJggg==");

        var result = await PreviewAsync("picture.bin", png);

        var image = Assert.IsType<ImagePreviewContent>(result);
        Assert.True(image.CanRender);
        Assert.Equal(ImageFormat.Png, image.Metadata!.Format);
        Assert.Equal(3, image.Metadata.Width);
        Assert.Equal(2, image.Metadata.Height);
    }

    [Fact]
    public async Task ImageExtensionWithInvalidBinaryContentUsesBinaryFallback()
    {
        var result = await PreviewAsync("broken.png", [0x00, 0x01, 0x02, 0x03]);

        Assert.IsType<BinaryPreviewContent>(result);
    }

    [Fact]
    public async Task PngSignatureIsImage()
    {
        var result = await PreviewAsync("wrong.txt", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        Assert.IsType<ImagePreviewContent>(result);
    }

    [Fact]
    public async Task JpegSignatureIsImage()
    {
        var result = await PreviewAsync("image.bin", [0xFF, 0xD8, 0xFF, 0xE0]);

        Assert.IsType<ImagePreviewContent>(result);
    }

    [Fact]
    public async Task NulByteIsBinaryEvenWhenNamedText()
    {
        var result = await PreviewAsync("looks-like-text.txt", [0x61, 0x00, 0x62, 0x63]);

        Assert.IsType<BinaryPreviewContent>(result);
    }

    [Fact]
    public async Task LargeTextIsBoundedAndDoesNotEmitReplacementForSplitUtf8Character()
    {
        var prefix = Enumerable.Repeat((byte)'a', FilePreviewLimits.TextReadBytes - 1).ToArray();
        var euro = Encoding.UTF8.GetBytes("€");
        var bytes = new byte[prefix.Length + euro.Length + 32];
        prefix.CopyTo(bytes, 0);
        euro.CopyTo(bytes, prefix.Length);
        Array.Fill(bytes, (byte)'z', prefix.Length + euro.Length, 32);

        var result = await PreviewAsync("large.txt", bytes);

        var text = Assert.IsType<TextPreviewContent>(result);
        Assert.True(text.IsTruncated);
        Assert.DoesNotContain('\uFFFD', text.Text);
        Assert.True(Encoding.UTF8.GetByteCount(text.Text) <= FilePreviewLimits.TextReadBytes);
    }

    [Fact]
    public async Task OversizedImageIsRejectedBeforeRendererDecode()
    {
        var path = TempPath();
        try
        {
            var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAMAAAACCAIAAAASFvFNAAAAEElEQVR4nGP8zwAFTDAGAwATKQED8NgHhAAAAABJRU5ErkJggg==");
            await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(png);
                stream.SetLength(FilePreviewLimits.ImageDecodeBytes + 1);
                stream.Position = stream.Length - 12;
                await stream.WriteAsync(png.AsMemory(png.Length - 12, 12));
            }

            var result = await new FilePreviewService().LoadAsync("large.png", path, CancellationToken.None);

            var image = Assert.IsType<ImagePreviewContent>(result);
            Assert.False(image.CanRender);
            Assert.NotNull(image.Metadata);
            Assert.Contains("size limit", image.UnavailableReason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<FilePreviewContent> PreviewAsync(string gitPath, byte[] bytes)
    {
        var path = TempPath();
        try
        {
            await File.WriteAllBytesAsync(path, bytes);
            return await new FilePreviewService().LoadAsync(gitPath, path, CancellationToken.None);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static byte[] WithPreamble(Encoding encoding, string text) =>
        [.. encoding.GetPreamble(), .. encoding.GetBytes(text)];

    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"csharpgit-preview-{Guid.NewGuid():N}");
}
