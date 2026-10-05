using CSharpGit.Application.Abstractions;

namespace CSharpGit.Git.Tests;

public sealed class InteractiveRebaseAuthorChangeServiceTests
{
    private readonly InteractiveRebaseAuthorChangeService _service = new();

    [Fact]
    public void CaretAndPartialSelectionResolveEligibleCommitLines()
    {
        const string todo =
            "pick aaa First\n" +
            "# comment\n" +
            "reword bbb Second\n" +
            "edit ccc Third\n";

        var caret = _service.Analyze(
            todo,
            todo.IndexOf("First", StringComparison.Ordinal),
            0);
        var selectionStart = todo.IndexOf("Second", StringComparison.Ordinal) + 2;
        var selectionEnd = todo.IndexOf("Third", StringComparison.Ordinal) + 2;
        var selection = _service.Analyze(
            todo,
            selectionStart,
            selectionEnd - selectionStart);

        Assert.Equal(1, caret.SelectedEligibleCount);
        Assert.Equal(3, caret.AllEligibleCount);
        Assert.Equal(2, selection.SelectedEligibleCount);
    }

    [Fact]
    public void SquashAndFixupChainsExcludeTheCombinedGroup()
    {
        const string todo =
            "pick a A\n" +
            "pick b B\n" +
            "# between base and fixup\n" +
            "fixup c C\n" +
            "reword d D\n" +
            "edit e E\n" +
            "squash f F\n" +
            "p g G\n";

        var analysis = _service.Analyze(todo, 0, todo.Length);

        Assert.Equal(3, analysis.AllEligibleCount);
        Assert.Equal(4, analysis.AllUnsupportedCount);
    }

    [Fact]
    public void ApplyInsertsGeneratedBlockBeforeExistingUserExec()
    {
        const string todo =
            "pick aaa First\n" +
            "exec echo user-command\n" +
            "pick bbb Second\n";

        var result = _service.Apply(new InteractiveRebaseAuthorChangeRequest(
            todo,
            0,
            "pick aaa First".Length,
            InteractiveRebaseAuthorChangeScope.SelectedCommitLines,
            "Dmitrii Dorogoi",
            "dmitrii@example.com",
            false));

        Assert.Equal(1, result.ChangedCount);
        Assert.Contains(
            "pick aaa First\n" +
            "# CSharpGit: change-author begin\n" +
            "exec git commit --amend --no-edit --no-verify --no-gpg-sign --author 'Dmitrii Dorogoi <dmitrii@example.com>'\n" +
            "# CSharpGit: change-author end\n" +
            "exec echo user-command",
            result.TodoText,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RepeatedApplyReplacesOwnGeneratedBlock()
    {
        const string todo = "pick aaa First\n";
        var first = _service.Apply(new InteractiveRebaseAuthorChangeRequest(
            todo,
            0,
            0,
            InteractiveRebaseAuthorChangeScope.SelectedCommitLines,
            "First User",
            "first@example.com",
            false));
        var second = _service.Apply(new InteractiveRebaseAuthorChangeRequest(
            first.TodoText,
            0,
            0,
            InteractiveRebaseAuthorChangeScope.SelectedCommitLines,
            "Second User",
            "second@example.com",
            true));

        Assert.Equal(
            1,
            second.TodoText.Split(
                InteractiveRebaseAuthorChangeService.BeginMarker,
                StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("First User", second.TodoText, StringComparison.Ordinal);
        Assert.Contains("Second User <second@example.com>", second.TodoText, StringComparison.Ordinal);
        Assert.Contains("--date=now", second.TodoText, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedMarkerBlockIsNotRewritten()
    {
        const string todo =
            "pick aaa First\n" +
            "# CSharpGit: change-author begin\n" +
            "noop\n" +
            "# CSharpGit: change-author end\n";

        var result = _service.Apply(new InteractiveRebaseAuthorChangeRequest(
            todo,
            0,
            0,
            InteractiveRebaseAuthorChangeScope.SelectedCommitLines,
            "User",
            "user@example.com",
            false));

        Assert.Contains(
            "# CSharpGit: change-author begin\nnoop\n# CSharpGit: change-author end",
            result.TodoText,
            StringComparison.Ordinal);
        Assert.Equal(
            2,
            result.TodoText.Split(
                InteractiveRebaseAuthorChangeService.BeginMarker,
                StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void ReapplyRemovesOwnBlockFromNewlyIneligibleSelectedGroup()
    {
        const string todo =
            "pick aaa First\n" +
            "# CSharpGit: change-author begin\n" +
            "exec git commit --amend --no-edit --no-verify --no-gpg-sign --author 'Old <old@example.com>'\n" +
            "# CSharpGit: change-author end\n" +
            "fixup bbb Second\n";

        var result = _service.Apply(new InteractiveRebaseAuthorChangeRequest(
            todo,
            0,
            todo.Length,
            InteractiveRebaseAuthorChangeScope.SelectedCommitLines,
            "New",
            "new@example.com",
            false));

        Assert.Equal(0, result.ChangedCount);
        Assert.DoesNotContain(
            InteractiveRebaseAuthorChangeService.BeginMarker,
            result.TodoText,
            StringComparison.Ordinal);
        Assert.Contains("fixup bbb Second", result.TodoText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void GeneratedLinesUseExistingLineEnding(string newline)
    {
        var todo = $"pick aaa First{newline}pick bbb Second{newline}";

        var result = _service.Apply(new InteractiveRebaseAuthorChangeRequest(
            todo,
            0,
            0,
            InteractiveRebaseAuthorChangeScope.SelectedCommitLines,
            "User",
            "user@example.com",
            false));

        if (newline == "\r\n")
            Assert.DoesNotContain("\n", result.TodoText.Replace("\r\n", string.Empty));
        else
            Assert.DoesNotContain("\r", result.TodoText);
    }

    [Fact]
    public void ShellQuotingKeepsSpecialCharactersInsideOneArgument()
    {
        var quoted = InteractiveRebaseAuthorChangeService.QuoteRebaseExecArgument(
            "A \"quoted\" $ & (test) O'Connor \\ <o'connor@example.com>");

        Assert.True(quoted.StartsWith("'", StringComparison.Ordinal));
        Assert.True(quoted.EndsWith("'", StringComparison.Ordinal));
        Assert.Contains("O'\\''Connor", quoted, StringComparison.Ordinal);
        Assert.Contains("o'\\''connor@example.com", quoted, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Bad\nName")]
    [InlineData("Bad<Name")]
    [InlineData("Bad>Name")]
    public void InvalidAuthorNameIsRejected(string name)
    {
        Assert.Throws<ArgumentException>(() =>
            _service.Apply(new InteractiveRebaseAuthorChangeRequest(
                "pick aaa First\n",
                0,
                0,
                InteractiveRebaseAuthorChangeScope.SelectedCommitLines,
                name,
                "user@example.com",
                false)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("missing-at")]
    [InlineData("@example.com")]
    [InlineData("user@")]
    [InlineData("two@@example.com")]
    [InlineData("white space@example.com")]
    [InlineData("bad<user@example.com")]
    public void InvalidAuthorEmailIsRejected(string email)
    {
        Assert.Throws<ArgumentException>(() =>
            _service.Apply(new InteractiveRebaseAuthorChangeRequest(
                "pick aaa First\n",
                0,
                0,
                InteractiveRebaseAuthorChangeScope.SelectedCommitLines,
                "User",
                email,
                false)));
    }

    [Fact]
    public void FiftyCommitsArePreparedInOneTodoTransformation()
    {
        var todo = string.Join(
            "\n",
            Enumerable.Range(1, 50).Select(index => $"pick {index:x7} Commit {index}"))
            + "\n";

        var result = _service.Apply(new InteractiveRebaseAuthorChangeRequest(
            todo,
            0,
            0,
            InteractiveRebaseAuthorChangeScope.AllEligibleCommits,
            "User",
            "user@example.com",
            false));

        Assert.Equal(50, result.ChangedCount);
        Assert.Equal(
            50,
            result.TodoText.Split(
                InteractiveRebaseAuthorChangeService.BeginMarker,
                StringSplitOptions.None).Length - 1);
    }
}
