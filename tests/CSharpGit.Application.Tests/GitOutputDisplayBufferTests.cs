using CSharpGit.Presentation.Controls;

namespace CSharpGit.Application.Tests;

public sealed class GitOutputDisplayBufferTests
{
    [Theory]
    [InlineData("a\nb", "a\nb")]
    [InlineData("a\rb", "b")]
    [InlineData("10%\r20%\r30%", "30%")]
    [InlineData("a\r\nb", "a\nb")]
    public void RawOutputIsNormalizedForDisplay(string input, string expected)
    {
        var buffer = new GitOutputDisplayBuffer();

        buffer.Append(input);

        Assert.Equal(expected, buffer.Text);
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
    public void PlainAppendProducesTailDelta()
    {
        var buffer = new GitOutputDisplayBuffer();
        buffer.Append("a\n");

        var delta = buffer.Append("b");

        Assert.True(delta.IsAppend);
        Assert.Equal("b", delta.Text);
        Assert.Equal("a\nb", buffer.Text);
    }
}
