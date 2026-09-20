using System.Text;
using CSharpGit.Application;
using CSharpGit.Application.Abstractions;

namespace CSharpGit.Application.Tests;

public sealed class GitCommandStreamingActivityTests
{
    [Fact]
    public void StreamingOutputIsAccumulatedPerStreamAndGetReturnsCurrentSnapshot()
    {
        var history = new GitCommandActivityHistory();
        var id = history.Started("git", "/repo", ["fetch", "origin"], GitCommandKind.User);

        history.OutputReceived(id, GitOutputStream.StandardOutput, "one");
        history.OutputReceived(id, GitOutputStream.StandardError, "err-");
        history.OutputReceived(id, GitOutputStream.StandardOutput, "-two");
        history.OutputReceived(id, GitOutputStream.StandardError, "two");

        var activity = history.Get(id);
        Assert.NotNull(activity);
        Assert.Equal("one-two", activity.StandardOutput);
        Assert.Equal("err-two", activity.StandardError);
        Assert.Equal(GitCommandStatus.Running, activity.Status);
    }

    [Fact]
    public void CompletionFailureAndCancellationPreserveAccumulatedOutput()
    {
        var history = new GitCommandActivityHistory();

        var success = history.Started("git", "/repo", ["fetch"], GitCommandKind.User);
        history.OutputReceived(success, GitOutputStream.StandardOutput, "success");
        history.Completed(success, 0);

        var failure = history.Started("git", "/repo", ["push"], GitCommandKind.User);
        history.OutputReceived(failure, GitOutputStream.StandardError, "rejected");
        history.Completed(failure, 1);

        var cancelled = history.Started("git", "/repo", ["clone"], GitCommandKind.User);
        history.OutputReceived(cancelled, GitOutputStream.StandardOutput, "partial");
        history.Cancelled(cancelled, 137);

        Assert.Equal("success", history.Get(success)!.StandardOutput);
        Assert.Equal(GitCommandStatus.Succeeded, history.Get(success)!.Status);
        Assert.Equal("rejected", history.Get(failure)!.StandardError);
        Assert.Equal(GitCommandStatus.Failed, history.Get(failure)!.Status);
        Assert.Equal("partial", history.Get(cancelled)!.StandardOutput);
        Assert.Equal(GitCommandStatus.Cancelled, history.Get(cancelled)!.Status);
    }

    [Fact]
    public void OutputIsBoundedDuringStreamingAndTruncationIsPublishedOnce()
    {
        var history = new GitCommandActivityHistory();
        var outputEvents = 0;
        history.Changed += (_, args) =>
        {
            if (args.ChangeKind == GitCommandActivityChangeKind.Output)
                outputEvents++;
        };
        var id = history.Started("git", "/repo", ["log"], GitCommandKind.Internal);

        history.OutputReceived(id, GitOutputStream.StandardOutput,
            new string('x', GitCommandActivityHistory.MaximumOutputBytes));
        history.OutputReceived(id, GitOutputStream.StandardOutput, "overflow");
        var eventsAfterTruncation = outputEvents;
        history.OutputReceived(id, GitOutputStream.StandardOutput, "ignored");

        var activity = history.Get(id)!;
        Assert.True(activity.StandardOutputTruncated);
        Assert.Equal(eventsAfterTruncation, outputEvents);
        Assert.Equal(1, CountOccurrences(activity.StandardOutput, "[output truncated]"));
        Assert.True(Encoding.UTF8.GetByteCount(activity.StandardOutput) <= GitCommandActivityHistory.MaximumOutputBytes);
    }

    [Fact]
    public void Utf8BoundaryDoesNotCreateReplacementCharacters()
    {
        var history = new GitCommandActivityHistory();
        var id = history.Started("git", "/repo", ["log"], GitCommandKind.Internal);
        var prefix = new string('x', GitCommandActivityHistory.MaximumOutputBytes - 32);

        history.OutputReceived(id, GitOutputStream.StandardOutput, prefix);
        history.OutputReceived(id, GitOutputStream.StandardOutput, string.Concat(Enumerable.Repeat("😀", 32)));

        var output = history.Get(id)!.StandardOutput;
        Assert.DoesNotContain('�', output);
        Assert.Contains("[output truncated]", output, StringComparison.Ordinal);
        Assert.True(Encoding.UTF8.GetByteCount(output) <= GitCommandActivityHistory.MaximumOutputBytes);
    }

    [Fact]
    public void OneHundredAndFirstEntryReportsEvictedActivity()
    {
        var history = new GitCommandActivityHistory();
        Guid first = default;
        Guid? evicted = null;
        GitCommandKind? evictedKind = null;
        history.Changed += (_, args) =>
        {
            if (args.ChangeKind == GitCommandActivityChangeKind.Started)
            {
                evicted = args.EvictedActivityId;
                evictedKind = args.EvictedCommandKind;
            }
        };

        for (var index = 0; index <= GitCommandActivityHistory.MaximumHistoryEntries; index++)
        {
            var id = history.Started("git", "/repo", ["status", index.ToString()],
                index == 0 ? GitCommandKind.User : GitCommandKind.Internal);
            if (index == 0) first = id;
        }

        Assert.Equal(first, evicted);
        Assert.Equal(GitCommandKind.User, evictedKind);
        Assert.Null(history.Get(first));
    }

    [Fact]
    public void ChangedCallbacksRunOutsideHistoryLock()
    {
        var history = new GitCommandActivityHistory();
        var callbackCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        history.Changed += (_, _) =>
        {
            var task = Task.Run(() => history.GetSnapshot(GitCommandFilter.AllCommands));
            if (task.Wait(TimeSpan.FromSeconds(2)))
                callbackCompleted.TrySetResult(true);
        };

        history.Started("git", "/repo", ["status"], GitCommandKind.Internal);

        Assert.True(callbackCompleted.Task.Wait(TimeSpan.FromSeconds(2)));
    }

    private static int CountOccurrences(string value, string token) =>
        (value.Length - value.Replace(token, string.Empty, StringComparison.Ordinal).Length) / token.Length;
}
