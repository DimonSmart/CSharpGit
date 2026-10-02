using CSharpGit.Domain;
using CSharpGit.Presentation;

namespace CSharpGit.Desktop.Tests;

public sealed class DiffPresentationResolverTests
{
    [Fact]
    public void BinaryWinsOverDisplayedLineCount()
    {
        var diff = new FileDiff("image.png", true, []);

        Assert.Equal(DiffContentPresentation.Binary, DiffPresentationResolver.Resolve(diff, 10));
    }

    [Fact]
    public void NonBinaryWithDisplayedRowsIsText()
    {
        var diff = new FileDiff("file.txt", false, [new DiffLine("+value", DiffLineKind.Added)]);

        Assert.Equal(DiffContentPresentation.Text, DiffPresentationResolver.Resolve(diff, 1));
    }

    [Fact]
    public void NonBinaryWithoutDisplayedRowsIsNoTextualPatch()
    {
        var diff = new FileDiff("file.txt", false, []);

        Assert.Equal(DiffContentPresentation.NoTextualPatch, DiffPresentationResolver.Resolve(diff, 0));
    }
}
