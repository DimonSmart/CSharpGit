using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Desktop.Tests;

public sealed class CompactDiffLineTests
{
    [Fact]
    public void RemovesNoiseHeadersAndCalculatesOldAndNewLineNumbers()
    {
        var lines = CompactDiffLine.Build(
        [
            new DiffLine("diff --git a/sample.txt b/sample.txt", DiffLineKind.Header),
            new DiffLine("index 1234567..89abcde 100644", DiffLineKind.Header),
            new DiffLine("--- a/sample.txt", DiffLineKind.Header),
            new DiffLine("+++ b/sample.txt", DiffLineKind.Header),
            new DiffLine("@@ -10,4 +10,5 @@", DiffLineKind.Header),
            new DiffLine(" context", DiffLineKind.Context),
            new DiffLine("-old", DiffLineKind.Removed),
            new DiffLine("+new", DiffLineKind.Added),
            new DiffLine(" unchanged", DiffLineKind.Context),
            new DiffLine("+extra", DiffLineKind.Added),
            new DiffLine("\\ No newline at end of file", DiffLineKind.Context)
        ]);

        Assert.Equal(7, lines.Count);
        Assert.DoesNotContain(lines, line =>
            line.Text.StartsWith("diff --git", StringComparison.Ordinal) ||
            line.Text.StartsWith("index ", StringComparison.Ordinal) ||
            line.Text.StartsWith("--- ", StringComparison.Ordinal) ||
            line.Text.StartsWith("+++ ", StringComparison.Ordinal));

        var hunk = lines[0];
        Assert.Equal(DiffLineKind.Header, hunk.Kind);
        Assert.Null(hunk.OldLineNumber);
        Assert.Null(hunk.NewLineNumber);

        Assert.Equal(10, lines[1].OldLineNumber);
        Assert.Equal(10, lines[1].NewLineNumber);
        Assert.Equal(11, lines[2].OldLineNumber);
        Assert.Null(lines[2].NewLineNumber);
        Assert.Null(lines[3].OldLineNumber);
        Assert.Equal(11, lines[3].NewLineNumber);
        Assert.Equal(12, lines[4].OldLineNumber);
        Assert.Equal(12, lines[4].NewLineNumber);
        Assert.Null(lines[5].OldLineNumber);
        Assert.Equal(13, lines[5].NewLineNumber);
        Assert.Equal(DiffLineKind.Header, lines[6].Kind);
    }

    [Fact]
    public void PreservesMeaningfulMetadataBeforeTheFirstHunk()
    {
        var lines = CompactDiffLine.Build(
        [
            new DiffLine("diff --git a/old.cs b/new.cs", DiffLineKind.Header),
            new DiffLine("similarity index 100%", DiffLineKind.Context),
            new DiffLine("rename from old.cs", DiffLineKind.Context),
            new DiffLine("rename to new.cs", DiffLineKind.Context)
        ]);

        Assert.Equal(3, lines.Count);
        Assert.All(lines, line => Assert.Equal(DiffLineKind.Header, line.Kind));
        Assert.Contains(lines, line => line.Text == "rename from old.cs");
        Assert.Contains(lines, line => line.Text == "rename to new.cs");
    }
}
