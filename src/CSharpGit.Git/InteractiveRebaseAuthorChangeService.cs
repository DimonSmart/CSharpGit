using System.Text;
using CSharpGit.Application.Abstractions;

namespace CSharpGit.Git;

internal sealed class InteractiveRebaseAuthorChangeService : IInteractiveRebaseAuthorChangeService
{
    internal const string BeginMarker = "# CSharpGit: change-author begin";
    internal const string EndMarker = "# CSharpGit: change-author end";

    public InteractiveRebaseAuthorChangeAnalysis Analyze(
        string todoText,
        int selectionStart,
        int selectionLength)
    {
        var state = Scan(todoText, selectionStart, selectionLength);
        return new InteractiveRebaseAuthorChangeAnalysis(
            state.SelectedEligibleCount,
            state.AllEligibleCount,
            state.SelectedUnsupportedCount,
            state.AllUnsupportedCount);
    }

    public InteractiveRebaseAuthorChangeResult Apply(
        InteractiveRebaseAuthorChangeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var authorName = GitIdentityValidation.NormalizeRequiredAuthorName(request.AuthorName);
        var authorEmail = GitIdentityValidation.NormalizeRequiredAuthorEmail(request.AuthorEmail);
        var state = Scan(request.TodoText, request.SelectionStart, request.SelectionLength);

        var applyAll = request.Scope switch
        {
            InteractiveRebaseAuthorChangeScope.SelectedCommitLines => false,
            InteractiveRebaseAuthorChangeScope.AllEligibleCommits => true,
            _ => throw new ArgumentOutOfRangeException(nameof(request.Scope))
        };

        var target = new bool[state.Lines.Count];
        var scopedCommit = new bool[state.Lines.Count];
        var changedCount = 0;

        for (var index = 0; index < state.Lines.Count; index++)
        {
            var inScope = applyAll || state.Selected[index];
            scopedCommit[index] = inScope
                && state.CommandKinds[index] is TodoCommandKind.Candidate or TodoCommandKind.SquashOrFixup;

            if (!inScope || !state.Eligible[index])
                continue;

            target[index] = true;
            changedCount++;
        }

        var command = BuildExecCommand(authorName, authorEmail, request.ResetAuthorDate);
        var defaultNewline = state.Lines
            .Select(line => line.Terminator)
            .FirstOrDefault(value => value.Length > 0)
            ?? Environment.NewLine;
        var builder = new StringBuilder(
            request.TodoText.Length + changedCount * (command.Length + 96));

        for (var index = 0; index < state.Lines.Count; index++)
        {
            var line = state.Lines[index];
            var existingGeneratedBlock = IsGeneratedBlockAt(state.Lines, index + 1);

            if (target[index])
            {
                AppendLineAndGeneratedBlock(
                    builder,
                    line,
                    command,
                    defaultNewline,
                    existingGeneratedBlock
                        ? state.Lines[index + 3].Terminator
                        : line.Terminator);

                if (existingGeneratedBlock)
                    index += 3;

                continue;
            }

            builder.Append(line.Text);
            builder.Append(line.Terminator);

            if (scopedCommit[index] && !state.Eligible[index] && existingGeneratedBlock)
                index += 3;
        }

        var eligibleCount = applyAll
            ? state.AllEligibleCount
            : state.SelectedEligibleCount;
        var unsupportedCount = applyAll
            ? state.AllUnsupportedCount
            : state.SelectedUnsupportedCount;

        return new InteractiveRebaseAuthorChangeResult(
            builder.ToString(),
            eligibleCount,
            changedCount,
            unsupportedCount);
    }

    private static ScanState Scan(
        string todoText,
        int selectionStart,
        int selectionLength)
    {
        ArgumentNullException.ThrowIfNull(todoText);
        if (selectionStart < 0 || selectionStart > todoText.Length)
            throw new ArgumentOutOfRangeException(nameof(selectionStart));
        if (selectionLength < 0 || selectionLength > todoText.Length - selectionStart)
            throw new ArgumentOutOfRangeException(nameof(selectionLength));

        var lines = ParseLines(todoText);
        var commandKinds = lines
            .Select(line => ParseCommandKind(line.Text))
            .ToArray();
        var eligible = new bool[lines.Count];

        for (var index = 0; index < lines.Count; index++)
        {
            if (commandKinds[index] != TodoCommandKind.Candidate)
                continue;

            var nextCommitCommand = TodoCommandKind.None;
            for (var next = index + 1; next < lines.Count; next++)
            {
                if (commandKinds[next] == TodoCommandKind.None)
                    continue;

                nextCommitCommand = commandKinds[next];
                break;
            }

            eligible[index] = nextCommitCommand != TodoCommandKind.SquashOrFixup;
        }

        var selected = new bool[lines.Count];
        var selectedEligibleCount = 0;
        var allEligibleCount = 0;
        var selectedUnsupportedCount = 0;
        var allUnsupportedCount = 0;

        for (var index = 0; index < lines.Count; index++)
        {
            selected[index] = IntersectsSelection(lines[index], selectionStart, selectionLength);

            if (eligible[index])
            {
                allEligibleCount++;
                if (selected[index])
                    selectedEligibleCount++;
                continue;
            }

            if (commandKinds[index] is not (TodoCommandKind.Candidate or TodoCommandKind.SquashOrFixup))
                continue;

            allUnsupportedCount++;
            if (selected[index])
                selectedUnsupportedCount++;
        }

        return new ScanState(
            lines,
            commandKinds,
            eligible,
            selected,
            selectedEligibleCount,
            allEligibleCount,
            selectedUnsupportedCount,
            allUnsupportedCount);
    }

    private static IReadOnlyList<TodoLine> ParseLines(string text)
    {
        if (text.Length == 0)
            return [];

        var result = new List<TodoLine>();
        var position = 0;

        while (position < text.Length)
        {
            var start = position;
            while (position < text.Length && text[position] != '\r' && text[position] != '\n')
                position++;

            var contentEnd = position;
            string terminator;
            if (position >= text.Length)
            {
                terminator = string.Empty;
            }
            else if (text[position] == '\r'
                     && position + 1 < text.Length
                     && text[position + 1] == '\n')
            {
                terminator = "\r\n";
                position += 2;
            }
            else
            {
                terminator = text[position].ToString();
                position++;
            }

            result.Add(new TodoLine(
                text[start..contentEnd],
                terminator,
                start,
                position));
        }

        return result;
    }

    private static TodoCommandKind ParseCommandKind(string line)
    {
        var span = line.AsSpan().TrimStart();
        if (span.Length == 0 || span[0] == '#')
            return TodoCommandKind.None;

        var end = 0;
        while (end < span.Length && !char.IsWhiteSpace(span[end]))
            end++;

        return span[..end].ToString() switch
        {
            "pick" or "p" or "reword" or "r" or "edit" or "e" => TodoCommandKind.Candidate,
            "squash" or "s" or "fixup" or "f" => TodoCommandKind.SquashOrFixup,
            _ => TodoCommandKind.None
        };
    }

    private static bool IntersectsSelection(
        TodoLine line,
        int selectionStart,
        int selectionLength)
    {
        if (selectionLength == 0)
        {
            if (selectionStart >= line.Start && selectionStart < line.EndExclusive)
                return true;

            return line.Terminator.Length == 0
                   && selectionStart == line.EndExclusive
                   && selectionStart >= line.Start;
        }

        var selectionEnd = selectionStart + selectionLength;
        return line.Start < selectionEnd && line.EndExclusive > selectionStart;
    }

    private static bool IsGeneratedBlockAt(
        IReadOnlyList<TodoLine> lines,
        int index)
    {
        if (index < 0 || index + 2 >= lines.Count)
            return false;

        return string.Equals(lines[index].Text, BeginMarker, StringComparison.Ordinal)
               && IsExecLine(lines[index + 1].Text)
               && string.Equals(lines[index + 2].Text, EndMarker, StringComparison.Ordinal);
    }

    private static bool IsExecLine(string value)
    {
        var trimmed = value.TrimStart();
        return trimmed.StartsWith("exec ", StringComparison.Ordinal)
               || trimmed.StartsWith("x ", StringComparison.Ordinal);
    }

    private static void AppendLineAndGeneratedBlock(
        StringBuilder builder,
        TodoLine line,
        string command,
        string defaultNewline,
        string finalTerminator)
    {
        var newline = line.Terminator.Length > 0 ? line.Terminator : defaultNewline;

        builder.Append(line.Text);
        builder.Append(newline);
        builder.Append(BeginMarker);
        builder.Append(newline);
        builder.Append(command);
        builder.Append(newline);
        builder.Append(EndMarker);
        builder.Append(finalTerminator);
    }

    private static string BuildExecCommand(
        string authorName,
        string authorEmail,
        bool resetAuthorDate)
    {
        var author = $"{authorName} <{authorEmail}>";
        var command =
            "exec git commit --amend --no-edit --no-verify --no-gpg-sign --author "
            + QuoteRebaseExecArgument(author);

        if (resetAuthorDate)
            command += " --date=now";

        return command;
    }

    internal static string QuoteRebaseExecArgument(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.IndexOf('\0') >= 0)
            throw new ArgumentException("Shell arguments cannot contain NUL.", nameof(value));

        return "'" + value.Replace("'", "'\\''") + "'";
    }

    private enum TodoCommandKind
    {
        None,
        Candidate,
        SquashOrFixup
    }

    private sealed record TodoLine(
        string Text,
        string Terminator,
        int Start,
        int EndExclusive);

    private sealed record ScanState(
        IReadOnlyList<TodoLine> Lines,
        TodoCommandKind[] CommandKinds,
        bool[] Eligible,
        bool[] Selected,
        int SelectedEligibleCount,
        int AllEligibleCount,
        int SelectedUnsupportedCount,
        int AllUnsupportedCount);
}
