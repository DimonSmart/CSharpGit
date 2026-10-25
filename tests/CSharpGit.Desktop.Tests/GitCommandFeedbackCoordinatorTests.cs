using System.Text;
using CSharpGit.Application;
using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.Controls;

namespace CSharpGit.Desktop.Tests;

public sealed class GitCommandFeedbackCoordinatorTests
{
    [Fact]
    public void FastSuccessfulCommandShowsFinalOnlyAfterDelayThenAutoCloses()
    {
        var now = DateTimeOffset.UtcNow;
        var history = new GitCommandActivityHistory();
        var coordinator = Create(history, () => now, autoClose: true);
        var id = history.Started("git", "/repo", ["pull"], GitCommandKind.User);
        history.Completed(id, 0);

        now += TimeSpan.FromMilliseconds(149);
        Assert.False(coordinator.Capture()!.IsVisible);
        now += TimeSpan.FromMilliseconds(1);
        var shown = coordinator.Capture()!;
        Assert.True(shown.IsVisible);
        Assert.Equal(GitCommandStatus.Succeeded, shown.Status);
        now += TimeSpan.FromMilliseconds(4999);
        Assert.True(coordinator.Capture()!.IsVisible);
        now += TimeSpan.FromMilliseconds(1);
        Assert.False(coordinator.Capture()!.IsVisible);
    }

    [Fact]
    public void DelayedUiStillShowsSuccessForTheFullConfiguredDuration()
    {
        var now = DateTimeOffset.UtcNow;
        var history = new GitCommandActivityHistory();
        var coordinator = Create(history, () => now, autoClose: true);
        var id = history.Started("git", "/repo", ["pull"], GitCommandKind.User);
        now += TimeSpan.FromMilliseconds(150);
        Assert.Equal(GitCommandStatus.Running, coordinator.Capture()!.Status);

        history.Completed(id, 0);
        now += TimeSpan.FromSeconds(10); // UI was busy with the repository refresh.
        var success = coordinator.Capture()!;
        Assert.Equal(GitCommandStatus.Succeeded, success.Status);
        Assert.True(success.IsVisible);
        now += TimeSpan.FromSeconds(5).Subtract(TimeSpan.FromMilliseconds(1));
        Assert.True(coordinator.Capture()!.IsVisible);
        now += TimeSpan.FromMilliseconds(1);
        Assert.False(coordinator.Capture()!.IsVisible);
    }

    [Fact]
    public void HiddenPresentationDoesNotConsumeSuccessCountdown()
    {
        var now = DateTimeOffset.UtcNow;
        var history = new GitCommandActivityHistory();
        var coordinator = Create(history, () => now, autoClose: true);
        var id = history.Started("git", "/repo", ["pull"], GitCommandKind.User);
        history.Completed(id, 0);
        now += TimeSpan.FromSeconds(10);
        coordinator.Capture(presentationAvailable: false);
        now += TimeSpan.FromSeconds(10);
        coordinator.Capture(presentationAvailable: false);
        Assert.True(coordinator.Capture()!.IsVisible);
        now += TimeSpan.FromSeconds(5);
        Assert.False(coordinator.Capture()!.IsVisible);
    }

    [Fact]
    public void MissingCompletionNotificationIsRecoveredFromActivitySource()
    {
        var now = DateTimeOffset.UtcNow;
        var history = new GitCommandActivityHistory();
        var coordinator = new GitCommandFeedbackCoordinator(history, () => now);
        coordinator.Configure(true, false);

        var id = history.Started("git", "/repo", ["pull"], GitCommandKind.User);
        coordinator.Handle(new GitCommandActivityChangedEventArgs(
            history.Get(id)!, GitCommandActivityChangeKind.Started), false);

        now += TimeSpan.FromMilliseconds(150);
        Assert.Equal(GitCommandStatus.Running, coordinator.Capture()!.Status);

        // The history knows the command finished, but the coordinator
        // deliberately does not receive its terminal notification.
        history.OutputReceived(id, GitOutputStream.StandardError, "done\r");
        history.Completed(id, 0);
        now += TimeSpan.FromSeconds(1);

        var final = coordinator.Capture()!;
        Assert.Equal(id, final.ActivityId);
        Assert.Equal(GitCommandStatus.Succeeded, final.Status);
        Assert.Equal(0, final.ExitCode);
        Assert.Contains("done", final.PreviewText);
    }

    [Fact]
    public void DelayedStartedAfterCompletionDoesNotRestoreRunning()
    {
        var now = DateTimeOffset.UtcNow;
        var history = new GitCommandActivityHistory();
        var coordinator = new GitCommandFeedbackCoordinator(history, () => now);
        coordinator.Configure(true, false);

        var id = history.Started("git", "/repo", ["fetch"], GitCommandKind.User);
        var staleStarted = history.Get(id)!;
        history.Completed(id, 0);

        coordinator.Handle(new GitCommandActivityChangedEventArgs(
            staleStarted, GitCommandActivityChangeKind.Started), false);
        now += TimeSpan.FromMilliseconds(150);

        Assert.Equal(GitCommandStatus.Succeeded, coordinator.Capture()!.Status);
    }

    [Fact]
    public void PausesAutoCloseWhileHoveredOrFocusedAndKeepsRemainingTime()
    {
        var now = DateTimeOffset.UtcNow;
        var history = new GitCommandActivityHistory();
        var coordinator = Create(history, () => now, autoClose: true);
        var id = history.Started("git", "/repo", ["fetch"], GitCommandKind.User);
        history.Completed(id, 0);
        now += TimeSpan.FromMilliseconds(150);
        Assert.True(coordinator.Capture()!.IsVisible);

        now += TimeSpan.FromMilliseconds(800);
        Assert.True(coordinator.Capture()!.IsVisible);
        coordinator.Interacting(hovered: true, focused: false);
        now += TimeSpan.FromSeconds(5);
        Assert.True(coordinator.Capture()!.IsVisible);
        coordinator.Interacting(hovered: false, focused: true);
        now += TimeSpan.FromSeconds(5);
        Assert.True(coordinator.Capture()!.IsVisible);
        coordinator.Interacting(hovered: false, focused: false);
        now += TimeSpan.FromMilliseconds(4199);
        Assert.True(coordinator.Capture()!.IsVisible);
        now += TimeSpan.FromMilliseconds(1);
        Assert.False(coordinator.Capture()!.IsVisible);
    }

    [Fact]
    public void DismissedActivityCannotReopenButNextInvocationCan()
    {
        var now = DateTimeOffset.UtcNow;
        var history = new GitCommandActivityHistory();
        var coordinator = Create(history, () => now);
        var old = history.Started("git", "/repo", ["fetch"], GitCommandKind.User);
        coordinator.Dismiss(old);
        history.OutputReceived(old, GitOutputStream.StandardError, "progress\r");
        history.Completed(old, 0);
        now += TimeSpan.FromSeconds(1);
        Assert.Null(coordinator.Capture());

        var next = history.Started("git", "/repo", ["push"], GitCommandKind.User);
        now += TimeSpan.FromMilliseconds(150);
        Assert.Equal(next, coordinator.Capture()!.ActivityId);
    }

    [Fact]
    public void UnreadFailureIsStickyUntilNewFailureReplacesIt()
    {
        var now = DateTimeOffset.UtcNow;
        var history = new GitCommandActivityHistory();
        var coordinator = Create(history, () => now);
        var first = history.Started("git", "/repo", ["push"], GitCommandKind.User);
        history.Completed(first, 1);
        now += TimeSpan.FromMilliseconds(150);
        Assert.Equal(first, coordinator.Capture()!.ActivityId);

        var second = history.Started("git", "/repo", ["fetch"], GitCommandKind.User);
        history.Completed(second, 0);
        Assert.Equal(first, coordinator.Capture()!.ActivityId);

        var third = history.Started("git", "/repo", ["pull"], GitCommandKind.User);
        history.Completed(third, 1);
        now += TimeSpan.FromMilliseconds(150);
        Assert.Equal(third, coordinator.Capture()!.ActivityId);
        Assert.Equal(GitCommandStatus.Failed, coordinator.Capture()!.Status);
    }

    [Fact]
    public void InternalActivityDoesNotReplaceUserCommandAndCloneUsesEmbeddedOwner()
    {
        var now = DateTimeOffset.UtcNow;
        var history = new GitCommandActivityHistory();
        var coordinator = Create(history, () => now, cloneDialogActive: true);
        var id = history.Started("git", "/repo", ["clone", "--", "origin", "target"], GitCommandKind.User);
        history.Started("git", "/repo", ["status"], GitCommandKind.Internal);
        now += TimeSpan.FromMilliseconds(150);
        var snapshot = coordinator.Capture()!;
        Assert.Equal(id, snapshot.ActivityId);
        Assert.Equal(GitCommandFeedbackOwner.EmbeddedClone, snapshot.Owner);
    }

    [Fact]
    public void ProgressCarriageReturnReplacesLineAndUnsafeControlsAreEscaped()
    {
        var now = DateTimeOffset.UtcNow;
        var history = new GitCommandActivityHistory();
        var coordinator = Create(history, () => now);
        history.Started("git", "/repo", ["fetch"], GitCommandKind.User);
        now += TimeSpan.FromMilliseconds(150);
        history.OutputReceived(history.GetLatest()!.Id, GitOutputStream.StandardError, "5%\r");
        history.OutputReceived(history.GetLatest()!.Id, GitOutputStream.StandardError, "90%\r");
        history.OutputReceived(history.GetLatest()!.Id, GitOutputStream.StandardError, "100%\n");
        history.OutputReceived(history.GetLatest()!.Id, GitOutputStream.StandardOutput, "safe\0");
        var output = coordinator.Capture()!.PreviewText;
        Assert.Contains("100%", output);
        Assert.DoesNotContain("5%", output);
        Assert.Contains("\\0", output);
        Assert.DoesNotContain('\0', output);
    }

    [Fact]
    public void PreviewIsBoundedWhileOriginalCaptureRemainsAvailable()
    {
        var now = DateTimeOffset.UtcNow;
        var history = new GitCommandActivityHistory();
        var coordinator = Create(history, () => now);
        var id = history.Started("git", "/repo", ["log"], GitCommandKind.User);
        now += TimeSpan.FromMilliseconds(150);
        for (var n = 0; n < 100; n++)
            history.OutputReceived(id, GitOutputStream.StandardOutput, new string('X', 1000) + "\n");

        var snapshot = coordinator.Capture()!;
        Assert.True(snapshot.PreviewTruncated);
        Assert.True(Encoding.UTF8.GetByteCount(snapshot.PreviewText) <=
            GitCommandFeedbackCoordinator.MaximumPreviewBytes + 128);
        Assert.True(history.Get(id)!.StandardOutput.Length > snapshot.PreviewText.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(30)]
    public void ConfiguredSuccessDurationControlsDismissal(int seconds)
    {
        var now = DateTimeOffset.UtcNow;
        var history = new GitCommandActivityHistory();
        var coordinator = Create(history, () => now, autoClose: true, durationSeconds: seconds);
        var id = history.Started("git", "/repo", ["fetch"], GitCommandKind.User);
        history.Completed(id, 0);
        now += TimeSpan.FromMilliseconds(150);
        Assert.Equal(seconds != 0, coordinator.Capture()!.IsVisible);
        if (seconds == 0) return;
        now += TimeSpan.FromSeconds(seconds).Subtract(TimeSpan.FromMilliseconds(1));
        Assert.True(coordinator.Capture()!.IsVisible);
        now += TimeSpan.FromMilliseconds(1);
        Assert.False(coordinator.Capture()!.IsVisible);
    }

    [Fact]
    public void ChangingDurationRestartsCountdownForVisibleSuccess()
    {
        var now = DateTimeOffset.UtcNow;
        var history = new GitCommandActivityHistory();
        var coordinator = Create(history, () => now, autoClose: true);
        var id = history.Started("git", "/repo", ["pull"], GitCommandKind.User);
        history.Completed(id, 0);
        now += TimeSpan.FromMilliseconds(150);
        Assert.True(coordinator.Capture()!.IsVisible);
        now += TimeSpan.FromSeconds(4);
        Assert.True(coordinator.Capture()!.IsVisible);
        coordinator.Configure(true, true, 10);
        now += TimeSpan.FromSeconds(9);
        Assert.True(coordinator.Capture()!.IsVisible);
        now += TimeSpan.FromSeconds(1);
        Assert.False(coordinator.Capture()!.IsVisible);
    }

    private static GitCommandFeedbackCoordinator Create(
        GitCommandActivityHistory history,
        Func<DateTimeOffset> clock,
        bool autoClose = false,
        bool cloneDialogActive = false,
        int durationSeconds = 5)
    {
        var coordinator = new GitCommandFeedbackCoordinator(history, clock);
        coordinator.Configure(true, autoClose, durationSeconds);
        history.Changed += (_, args) => coordinator.Handle(args, cloneDialogActive);
        return coordinator;
    }
}
