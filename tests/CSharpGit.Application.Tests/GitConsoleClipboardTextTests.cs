using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.Controls;

namespace CSharpGit.Application.Tests;

public sealed class GitConsoleClipboardTextTests
{
    [Fact]
    public void CopyAllEscapesUnsafeControlsWithoutChangingCapturedActivity()
    {
        const string standardOutput = "a\0b\u001Ec\r\n\t";
        const string standardError = "error\u007F";
        var activity = CreateActivity(standardOutput, standardError);

        var text = GitConsoleClipboardText.Build(activity);

        Assert.Contains("a\\0b\\x1Ec\r\n\t", text, StringComparison.Ordinal);
        Assert.Contains("error\\x7F", text, StringComparison.Ordinal);
        Assert.DoesNotContain('\0', text);
        Assert.DoesNotContain('\u001E', text);
        Assert.DoesNotContain('\u007F', text);
        Assert.Equal(standardOutput, activity.StandardOutput);
        Assert.Equal(standardError, activity.StandardError);
    }

    [Fact]
    public void UnsafeControlEscapingPreservesStructuralTextCharacters()
    {
        const string input = "a\rb\nc\td";

        var text = GitOutputTextEscaper.EscapeUnsafeControls(input);

        Assert.Equal(input, text);
    }

    private static GitCommandActivity CreateActivity(string standardOutput, string standardError) =>
        new(
            Guid.NewGuid(),
            DateTimeOffset.Parse("2026-09-24T12:00:00+00:00"),
            DateTimeOffset.Parse("2026-09-24T12:00:01+00:00"),
            TimeSpan.FromSeconds(1),
            "/repo",
            "git",
            ["for-each-ref"],
            "git for-each-ref",
            GitCommandKind.Internal,
            0,
            standardOutput,
            standardError,
            GitCommandStatus.Succeeded,
            false,
            false);
}
