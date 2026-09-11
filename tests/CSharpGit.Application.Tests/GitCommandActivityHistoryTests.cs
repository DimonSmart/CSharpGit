using System.Text;
using CSharpGit.Application.Abstractions;

namespace CSharpGit.Application.Tests;

public sealed class GitCommandActivityHistoryTests
{
    [Fact]
    public void HistoryKeepsOnlyLatestOneHundredCommands()
    {
        var history = new GitCommandActivityHistory();

        for (var index = 0; index < 101; index++)
            history.Started("git", "/repo", ["status", index.ToString()], GitCommandKind.Internal);

        var entries = history.GetSnapshot(GitCommandFilter.AllCommands);
        Assert.Equal(100, entries.Count);
        Assert.Equal("git status 100", entries[0].DisplayCommand);
        Assert.Equal("git status 1", entries[^1].DisplayCommand);
        Assert.DoesNotContain(entries, entry => entry.DisplayCommand == "git status 0");
    }

    [Fact]
    public void SuccessfulCommandStoresExitCodeDurationAndStdout()
    {
        var history = new GitCommandActivityHistory();
        var id = history.Started("git", "/repo", ["fetch", "origin"], GitCommandKind.User);

        history.Completed(id, 0, "fetched", string.Empty);

        var entry = Assert.Single(history.GetSnapshot(GitCommandFilter.AllCommands));
        Assert.Equal(GitCommandStatus.Succeeded, entry.Status);
        Assert.Equal(0, entry.ExitCode);
        Assert.True(entry.Duration >= TimeSpan.Zero);
        Assert.Equal("fetched", entry.StandardOutput);
        Assert.NotNull(entry.CompletedAt);
    }

    [Fact]
    public void FailedCommandStoresExitCodeAndStderr()
    {
        var history = new GitCommandActivityHistory();
        var id = history.Started("git", "/repo", ["push", "origin", "main"], GitCommandKind.User);

        history.Completed(id, 1, string.Empty, "rejected");

        var entry = Assert.Single(history.GetSnapshot(GitCommandFilter.AllCommands));
        Assert.Equal(GitCommandStatus.Failed, entry.Status);
        Assert.Equal(1, entry.ExitCode);
        Assert.Equal("rejected", entry.StandardError);
    }

    [Fact]
    public void CancelledCommandIsMarkedCancelled()
    {
        var history = new GitCommandActivityHistory();
        var id = history.Started("git", "/repo", ["fetch", "origin"], GitCommandKind.User);

        history.Cancelled(id, 137, "partial output", "cancelled");

        var entry = Assert.Single(history.GetSnapshot(GitCommandFilter.AllCommands));
        Assert.Equal(GitCommandStatus.Cancelled, entry.Status);
        Assert.Equal(137, entry.ExitCode);
        Assert.Equal("partial output", entry.StandardOutput);
        Assert.Equal("cancelled", entry.StandardError);
    }

    [Fact]
    public void FilteringSeparatesUserCommandsFromAllCommands()
    {
        var history = new GitCommandActivityHistory();
        history.Started("git", "/repo", ["status", "--porcelain"], GitCommandKind.Internal);
        history.Started("git", "/repo", ["fetch", "origin"], GitCommandKind.User);

        var user = history.GetSnapshot(GitCommandFilter.UserCommands);
        var all = history.GetSnapshot(GitCommandFilter.AllCommands);

        Assert.Single(user);
        Assert.Equal("git fetch origin", user[0].DisplayCommand);
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public void CommandFormattingQuotesArgumentsWithSpaces()
    {
        var command = GitCommandFormatter.Format("git", ["commit", "-m", "message with spaces"]);

        Assert.Equal("git commit -m \"message with spaces\"", command);
    }

    [Fact]
    public void OutputIsLimitedAndMarkedAsTruncated()
    {
        var history = new GitCommandActivityHistory();
        var id = history.Started("git", "/repo", ["log"], GitCommandKind.Internal);
        var output = new string('x', GitCommandActivityHistory.MaximumOutputBytes + 256);

        history.Completed(id, 0, output, output);

        var entry = Assert.Single(history.GetSnapshot(GitCommandFilter.AllCommands));
        Assert.True(entry.StandardOutputTruncated);
        Assert.True(entry.StandardErrorTruncated);
        Assert.Contains("[output truncated]", entry.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("[output truncated]", entry.StandardError, StringComparison.Ordinal);
        Assert.True(Encoding.UTF8.GetByteCount(entry.StandardOutput) <= GitCommandActivityHistory.MaximumOutputBytes);
        Assert.True(Encoding.UTF8.GetByteCount(entry.StandardError) <= GitCommandActivityHistory.MaximumOutputBytes);
    }

    [Fact]
    public void SensitiveCommandLineValuesAreMasked()
    {
        var history = new GitCommandActivityHistory();
        history.Started(
            "git",
            "/repo",
            ["fetch", "https://user:secret@example.com/repo.git", "--token", "token-value", "http.extraHeader=Authorization: Bearer bearer-secret"],
            GitCommandKind.User);

        var entry = Assert.Single(history.GetSnapshot(GitCommandFilter.AllCommands));
        Assert.Contains("https://user:***@example.com/repo.git", entry.DisplayCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", entry.DisplayCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("token-value", entry.DisplayCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("bearer-secret", entry.DisplayCommand, StringComparison.Ordinal);
        Assert.Contains("***", entry.DisplayCommand, StringComparison.Ordinal);
    }
}
