using CSharpGit.Presentation.Controls;

namespace CSharpGit.Application.Tests;

public sealed class GitOutputDisplayBufferTests
{
    [Theory]
    [InlineData("a\nb", "a\nb")]
    [InlineData("a\rb", "b")]
    [InlineData("10%\r20%\r30%", "30%")]
    [InlineData("a\r\nb", "a\nb")]
    [InlineData("a\tb", "a\tb")]
    public void RawOutputIsNormalizedForDisplay(string input, string expected)
    {
        var buffer = new GitOutputDisplayBuffer();

        buffer.Append(input);

        Assert.Equal(expected, buffer.Text);
    }

    [Fact]
    public void ResetEscapesNulAsVisibleText()
    {
        var buffer = new GitOutputDisplayBuffer();

        var text = buffer.Reset("a\0b");

        Assert.Equal("a\\0b", text);
        Assert.Equal(new[] { 'a', '\\', '0', 'b' }, text.ToCharArray());
        Assert.DoesNotContain('\0', text);
    }

    [Fact]
    public void ResetEscapesRecordSeparator()
    {
        var buffer = new GitOutputDisplayBuffer();

        buffer.Reset("a\u001Eb");

        Assert.Equal("a\\x1Eb", buffer.Text);
    }

    [Theory]
    [InlineData("\u007F", "\\x7F")]
    [InlineData("\u0085", "\\x85")]
    public void ResetEscapesDelAndC1Controls(string input, string expected)
    {
        var buffer = new GitOutputDisplayBuffer();

        buffer.Reset(input);

        Assert.Equal(expected, buffer.Text);
    }

    [Fact]
    public void PrintableUnicodeIsPreserved()
    {
        const string input = "café Привет 日本語";
        var buffer = new GitOutputDisplayBuffer();

        buffer.Reset(input);

        Assert.Equal(input, buffer.Text);
    }

    [Fact]
    public void MachineReadableSeparatorsAreVisible()
    {
        var buffer = new GitOutputDisplayBuffer();

        buffer.Reset("refs/heads/main\0abc123\0origin/main\u001E");

        Assert.Equal("refs/heads/main\\0abc123\\0origin/main\\x1E", buffer.Text);
    }

    [Fact]
    public void ForEachRefRegressionShapeIsPresentationSafe()
    {
        var buffer = new GitOutputDisplayBuffer();

        buffer.Reset("refs/heads/main\0<hash>\0\0origin/main\0\u001E");

        Assert.Equal("refs/heads/main\\0<hash>\\0\\0origin/main\\0\\x1E", buffer.Text);
    }

    [Fact]
    public void StreamingControlCharacterProducesEncodedDelta()
    {
        var buffer = new GitOutputDisplayBuffer();
        buffer.Append("foo");

        var delimiterDelta = buffer.Append("\0");
        var tailDelta = buffer.Append("bar");

        Assert.Equal("foo\\0bar", buffer.Text);
        Assert.True(delimiterDelta.IsAppend);
        Assert.Equal("\\0", delimiterDelta.Text);
        Assert.True(tailDelta.IsAppend);
        Assert.Equal("bar", tailDelta.Text);
    }

    [Fact]
    public void CarriageReturnAcrossChunksReplacesCurrentLine()
    {
        var buffer = new GitOutputDisplayBuffer();

        buffer.Append("10%\r");
        var delta = buffer.Append("20%");

        Assert.Equal("20%", buffer.Text);
        Assert.Equal(0, delta.ReplaceFrom);
        Assert.Equal("20%", delta.Text);
    }

    [Fact]
    public void CrLfAcrossChunksProducesOneNewline()
    {
        var buffer = new GitOutputDisplayBuffer();

        buffer.Append("a\r");
        buffer.Append("\nb");

        Assert.Equal("a\nb", buffer.Text);
    }

    [Fact]
    public void CarriageReturnThenEscapedCharacterAcrossChunksUsesPresentationIndexes()
    {
        var buffer = new GitOutputDisplayBuffer();

        buffer.Append("old\r");
        var delta = buffer.Append("\0x");

        Assert.Equal("\\0x", buffer.Text);
        Assert.Equal(0, delta.ReplaceFrom);
        Assert.Equal("\\0x", delta.Text);
    }

    [Fact]
    public void PlainAppendProducesTailDelta()
    {
        var buffer = new GitOutputDisplayBuffer();
        buffer.Append("a\n");

        var delta = buffer.Append("b");

        Assert.True(delta.IsAppend);
        Assert.Equal("b", delta.Text);
        Assert.Equal("a\nb", buffer.Text);
    }

    [Fact]
    public void DisplayBufferContainsNoUnsafeControlCharacters()
    {
        var buffer = new GitOutputDisplayBuffer();

        buffer.Reset("old\0\u001E\u007F\rnew\t\u0085\nlast");

        Assert.All(
            buffer.Text,
            character => Assert.False(
                char.IsControl(character) &&
                character is not '\n' and not '\t',
                $"Unexpected control character U+{(int)character:X4}."));
    }
}
