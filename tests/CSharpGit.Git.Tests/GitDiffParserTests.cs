using CSharpGit.Domain;

namespace CSharpGit.Git.Tests;

public sealed class GitDiffParserTests
{
    [Fact]
    public void KeepsDisplayTextCleanWhilePreservingTrailingCarriageReturnMetadata()
    {
        var parsed = GitDiffParser.Parse("@@ -1 +1 @@\r\n-old\r\n+new\n");

        Assert.Equal("@@ -1 +1 @@", parsed.Lines[0].Text);
        Assert.True(parsed.Lines[0].HadTrailingCarriageReturn);
        Assert.Equal("-old", parsed.Lines[1].Text);
        Assert.True(parsed.Lines[1].HadTrailingCarriageReturn);
        Assert.Equal("+new", parsed.Lines[2].Text);
        Assert.False(parsed.Lines[2].HadTrailingCarriageReturn);
        Assert.DoesNotContain(parsed.Lines, line => line.Text.Contains('\r'));
    }

    [Fact]
    public void DetectsCrLfToLfWhenChangedTextIsOtherwiseIdentical()
    {
        var parsed = GitDiffParser.Parse(
            "@@ -1,2 +1,2 @@\n-one\r\n-two\r\n+one\n+two\n");

        var diagnostic = Assert.Single(
            parsed.Diagnostics,
            candidate => candidate.Kind == DiffDiagnosticKind.LineEndingsChanged);
        Assert.Equal(DiffTextLineEnding.CrLf, diagnostic.OriginalLineEnding);
        Assert.Equal(DiffTextLineEnding.Lf, diagnostic.ChangedLineEnding);
    }

    [Fact]
    public void DetectsLfToCrLfWhenChangedTextIsOtherwiseIdentical()
    {
        var parsed = GitDiffParser.Parse(
            "@@ -1,2 +1,2 @@\n-one\n-two\n+one\r\n+two\r\n");

        var diagnostic = Assert.Single(
            parsed.Diagnostics,
            candidate => candidate.Kind == DiffDiagnosticKind.LineEndingsChanged);
        Assert.Equal(DiffTextLineEnding.Lf, diagnostic.OriginalLineEnding);
        Assert.Equal(DiffTextLineEnding.CrLf, diagnostic.ChangedLineEnding);
    }

    [Fact]
    public void MixedEndingsDoNotProduceFalseGlobalLineEndingDiagnostic()
    {
        var parsed = GitDiffParser.Parse(
            "@@ -1,2 +1,2 @@\n-one\r\n-two\n+one\n+two\n");

        Assert.DoesNotContain(
            parsed.Diagnostics,
            candidate => candidate.Kind == DiffDiagnosticKind.LineEndingsChanged);
    }

    [Theory]
    [InlineData("-value\n+﻿value\n", DiffDiagnosticKind.Utf8BomAdded)]
    [InlineData("-﻿value\n+value\n", DiffDiagnosticKind.Utf8BomRemoved)]
    public void DetectsUtf8BomChangeOnFirstLine(string body, DiffDiagnosticKind expected)
    {
        var parsed = GitDiffParser.Parse("@@ -1 +1 @@\n" + body);

        Assert.Contains(parsed.Diagnostics, diagnostic => diagnostic.Kind == expected);
    }

    [Fact]
    public void PreservesGitNoFinalNewlineMarker()
    {
        var parsed = GitDiffParser.Parse(
            "@@ -1 +1 @@\n-old\n\\ No newline at end of file\n+new\n");

        Assert.Contains(parsed.Lines, line => line.Text == "\\ No newline at end of file");
        Assert.Contains(parsed.Diagnostics, diagnostic => diagnostic.Kind == DiffDiagnosticKind.NoFinalNewline);
    }

    [Fact]
    public void HeadersDoNotProduceContentDiagnostics()
    {
        var parsed = GitDiffParser.Parse(
            "diff --git a/a.txt b/a.txt\r\n--- a/a.txt\r\n+++ b/a.txt\r\n");

        Assert.Empty(parsed.Diagnostics);
    }
}
