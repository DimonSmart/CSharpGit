using System.Text;
using CSharpGit.Application;
using CSharpGit.Application.Abstractions;

namespace CSharpGit.Application.Tests;

public sealed class GitCommandActivityHistoryTests
{

    [Fact]
    public void EvictedRunningActivityRetainsLifecycleUntilCompletionWithoutReenteringHistory()
    {
        var history = new GitCommandActivityHistory();
        var detached = history.Started("git", "/repo", ["clone"], GitCommandKind.User);
        GitCommandActivity? terminal = null;
        history.Changed += (_, args) =>
        {
            if (args.ActivityId == detached && args.ChangeKind == GitCommandActivityChangeKind.Completed)
                terminal = args.Activity;
        };
        for (var i = 0; i < 100; i++)
            history.Started("git", "/repo", ["status", i.ToString()], GitCommandKind.Internal);

        Assert.Equal(100, history.GetSnapshot(GitCommandFilter.AllCommands).Count);
        Assert.DoesNotContain(history.GetSnapshot(GitCommandFilter.AllCommands), item => item.Id == detached);
        Assert.Equal(GitCommandStatus.Running, history.Get(detached)!.Status);

        history.OutputReceived(detached, GitOutputStream.StandardError, "Receiving objects: 100%\r");
        history.Failed(detached, null);

        Assert.Equal(GitCommandStatus.Failed, terminal!.Status);
        Assert.Null(terminal.ExitCode);
        Assert.Contains("Receiving objects", terminal.StandardError);
        Assert.Null(history.Get(detached));
        Assert.Equal(100, history.GetSnapshot(GitCommandFilter.AllCommands).Count);
    }

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

        history.OutputReceived(id, GitOutputStream.StandardOutput, "fetched");
        history.Completed(id, 0);

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

        history.OutputReceived(id, GitOutputStream.StandardError, "rejected");
        history.Completed(id, 1);

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

        history.OutputReceived(id, GitOutputStream.StandardOutput, "partial output");
        history.OutputReceived(id, GitOutputStream.StandardError, "cancelled");
        history.Cancelled(id, 137);

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

        history.OutputReceived(id, GitOutputStream.StandardOutput, output);
        history.OutputReceived(id, GitOutputStream.StandardError, output);
        history.Completed(id, 0);

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
    [Theory]
    [InlineData(GitOutputStream.StandardOutput)]
    [InlineData(GitOutputStream.StandardError)]
    public void SecretsSplitAcrossChunksNeverAppearInSnapshotsOrEvents(GitOutputStream stream)
    {
        const string secret = "synthetic-token-123456";
        var history = new GitCommandActivityHistory();
        var published = new List<string>();
        history.Changed += (_, args) =>
        {
            if (args.Activity is { } snapshot)
            {
                published.Add(snapshot.DisplayCommand);
                published.Add(snapshot.StandardOutput);
                published.Add(snapshot.StandardError);
            }
            if (args.OutputChunk is { } chunk)
                published.Add(chunk);
        };
        var id = history.Started("git", "/repo",
            ["fetch", "https://user:pass-secret@example.com/repo", "--token", secret],
            GitCommandKind.User);

        history.OutputReceived(id, stream, "Authorization: Bea");
        Assert.DoesNotContain("Authorization: Bea", history.Get(id)!.StandardOutput);
        history.OutputReceived(id, stream, "rer " + secret + "\r");
        history.OutputReceived(id, stream, "\npassword=" + secret + "\0");
        history.OutputReceived(id, stream, "https://user:" + secret[..8]);
        history.OutputReceived(id, stream, secret[8..] + "@example.com/path\n");
        history.Completed(id, 0);

        var activity = history.Get(id)!;
        Assert.DoesNotContain(secret, activity.StandardOutput);
        Assert.DoesNotContain(secret, activity.StandardError);
        Assert.DoesNotContain("pass-secret", activity.DisplayCommand);
        Assert.All(published, value => Assert.DoesNotContain(secret, value, StringComparison.Ordinal));
        Assert.All(published, value => Assert.DoesNotContain("pass-secret", value, StringComparison.Ordinal));
        var copy = CSharpGit.Presentation.Controls.GitConsoleClipboardText.Build(activity);
        Assert.DoesNotContain(secret, copy);
        Assert.DoesNotContain("pass-secret", copy);
        Assert.Contains("***", stream == GitOutputStream.StandardOutput
            ? activity.StandardOutput
            : activity.StandardError);
    }

    [Fact]
    public void NormalUtf8AndMachineSeparatorsSurviveSanitization()
    {
        var history = new GitCommandActivityHistory();
        var id = history.Started("git", "/repo", ["log"], GitCommandKind.Internal);
        history.OutputReceived(id, GitOutputStream.StandardOutput, "é😀\r");
        history.OutputReceived(id, GitOutputStream.StandardOutput, "\nentry\0tail");
        history.Completed(id, 0);
        Assert.Equal("é😀\r\nentry\0tail", history.Get(id)!.StandardOutput);
    }

    [Fact]
    public void OverlongUnterminatedRecordIsSuppressedWithoutLeakingItsEnd()
    {
        var history = new GitCommandActivityHistory();
        var id = history.Started("git", "/repo", ["status"], GitCommandKind.Internal);
        history.OutputReceived(id, GitOutputStream.StandardError, new string('a', 9000));
        history.OutputReceived(id, GitOutputStream.StandardError, "token=synthetic-secret");
        history.Completed(id, 1);
        var activity = history.Get(id)!;
        Assert.True(activity.StandardErrorTruncated);
        Assert.Contains("[output truncated]", activity.StandardError);
        Assert.DoesNotContain("synthetic-secret", activity.StandardError);
        Assert.True(Encoding.UTF8.GetByteCount(activity.StandardError)
            <= GitCommandActivityHistory.MaximumOutputBytes);
    }


    [Fact]
    public void ThrowingActivityObserverDoesNotStrandRunningStateOrSilenceLaterObservers()
    {
        var history = new GitCommandActivityHistory();
        var changes = new List<GitCommandActivityChangeKind>();
        history.Changed += (_, _) => throw new InvalidOperationException("observer failure");
        history.Changed += (_, args) => changes.Add(args.ChangeKind);

        var id = history.Started("git", "/repo", ["pull"], GitCommandKind.User);
        history.OutputReceived(id, GitOutputStream.StandardError, "progress\\r".Replace("\\r", "\r"));
        history.Completed(id, 0);

        var final = history.Get(id)!;
        Assert.Equal(GitCommandStatus.Succeeded, final.Status);
        Assert.Equal(0, final.ExitCode);
        Assert.Contains(GitCommandActivityChangeKind.Started, changes);
        Assert.Contains(GitCommandActivityChangeKind.Output, changes);
        Assert.Contains(GitCommandActivityChangeKind.Completed, changes);
    }
}
