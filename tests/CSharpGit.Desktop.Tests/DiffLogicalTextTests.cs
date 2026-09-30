using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Desktop.Tests;

public sealed class DiffLogicalTextTests
{
    [Fact]
    public void SameLineSelectionReturnsExactSubstring()
    {
        var model = Model("+alpha beta");

        Assert.Equal("alpha", model.GetSelectedText(0, 1, 0, 6));
    }

    [Fact]
    public void MultilineSelectionKeepsPartialFirstAndLastLines()
    {
        var model = Model("+Foo(bar);", "+Middle();", "+Bar(foo);");

        var selected = model.GetSelectedText(0, 1, 2, 4);

        Assert.Equal(
            string.Join(Environment.NewLine, "Foo(bar);", "+Middle();", "+Bar"),
            selected);
    }

    [Fact]
    public void ReverseSelectionNormalizesToTheSameRange()
    {
        var model = Model("+first", " second", "-third");

        Assert.Equal(
            model.GetSelectedText(0, 2, 2, 4),
            model.GetSelectedText(2, 4, 0, 2));
    }

    [Fact]
    public void SelectionToColumnZeroOfNextLineIncludesExactlyOneLogicalNewline()
    {
        var model = Model("+first", "+second");

        Assert.Equal("+first" + Environment.NewLine, model.GetSelectedText(0, 0, 1, 0));
    }

    [Fact]
    public void PrefixesAndDisplayedHeadersAreOrdinaryLogicalText()
    {
        var model = Model("@@ -1 +1 @@", "+added", "-removed", " context");

        Assert.Equal(
            string.Join(Environment.NewLine, "@@ -1 +1 @@", "+added", "-removed", " context"),
            model.Text);
    }

    [Fact]
    public void SelectAllTextHasNoArtificialTrailingNewline()
    {
        var model = Model("+one", "-two");

        Assert.Equal("+one" + Environment.NewLine + "-two", model.Text);
        Assert.False(model.Text.EndsWith(Environment.NewLine, StringComparison.Ordinal));
    }

    [Fact]
    public void EmptySelectionProducesNoClipboardPayload()
    {
        var model = Model("+value");

        Assert.Equal(string.Empty, model.GetSelectedText(0, 3, 0, 3));
    }

    [Fact]
    public void InvalidUtf16BoundaryExpandsInsteadOfReturningHalfASurrogatePair()
    {
        var model = Model("+A😀B");
        var emojiStart = model.Text.IndexOf("😀", StringComparison.Ordinal);

        Assert.Equal("😀", model.GetSelectedText(emojiStart, emojiStart + 1));
    }

    private static DiffLogicalText Model(params string[] text) =>
        new(text.Select(line => new CompactDiffLine(line, DiffLineKind.Context, null, null)).ToArray());
}
