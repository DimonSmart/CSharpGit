using CSharpGit.Presentation;

namespace CSharpGit.Desktop.Tests;

public sealed class WindowCloseCoordinatorTests
{
    [Fact]
    public void RepeatedRequestsWhileConfirmingDoNotStartAnotherConfirmation()
    {
        var gate = new WindowCloseCoordinator();
        Assert.Equal(WindowCloseDecision.Confirm, gate.Request(false, true));
        Assert.Equal(WindowCloseStage.Confirming, gate.Stage);
        Assert.Equal(WindowCloseDecision.Cancel, gate.Request(false, true));
        Assert.True(gate.CompleteConfirmation(true, false));
        Assert.Equal(WindowCloseStage.Idle, gate.Stage);
        Assert.True(gate.TrySchedule(() => true));
        Assert.Equal(WindowCloseDecision.Cancel, gate.Request(false, false));
    }

    [Fact]
    public void CancelledConfirmationPreservesAbilityToConfirmAgain()
    {
        var gate = new WindowCloseCoordinator();
        Assert.Equal(WindowCloseDecision.Confirm, gate.Request(false, true));
        Assert.False(gate.CompleteConfirmation(false, false));
        Assert.Equal(WindowCloseDecision.Confirm, gate.Request(false, true));
        gate.CancelConfirmation(); // Also used after a dialog failure.
        Assert.Equal(WindowCloseDecision.Confirm, gate.Request(false, true));
    }

    [Fact]
    public void ConfirmationRechecksSafetyBeforeAdmittingShutdown()
    {
        var gate = new WindowCloseCoordinator();
        Assert.Equal(WindowCloseDecision.Confirm, gate.Request(false, true));
        Assert.False(gate.CompleteConfirmation(true, true));
        Assert.Equal(WindowCloseStage.Idle, gate.Stage);
        Assert.Equal(WindowCloseDecision.Cancel, gate.Request(true, false));
        Assert.Equal(WindowCloseDecision.Schedule, gate.Request(false, false));
    }

    [Fact]
    public void DispatcherRejectionDoesNotStartCleanupAndAllowsRetry()
    {
        var gate = new WindowCloseCoordinator();
        var started = false;
        Assert.False(gate.TrySchedule(() => false));
        Assert.False(gate.BeginShutdown());
        Assert.Equal(WindowCloseStage.Idle, gate.Stage);
        Assert.True(gate.TrySchedule(() => true));
        started = gate.BeginShutdown();
        Assert.True(started);
    }

    [Fact]
    public void DispatcherExceptionAlsoReturnsToIdle()
    {
        var gate = new WindowCloseCoordinator();
        Assert.Throws<InvalidOperationException>(() => gate.TrySchedule(
            () => throw new InvalidOperationException("Injected dispatcher failure")));
        Assert.Equal(WindowCloseStage.Idle, gate.Stage);
        Assert.Equal(WindowCloseDecision.Schedule, gate.Request(false, false));
    }

    [Fact]
    public void MultipleCloseRequestsCauseOneCleanupAndFinalCloseRemainsAuthorized()
    {
        var gate = new WindowCloseCoordinator();
        Assert.True(gate.TrySchedule(() => true));
        Assert.Equal(WindowCloseDecision.Cancel, gate.Request(false, false));
        Assert.True(gate.BeginShutdown());
        Assert.False(gate.BeginShutdown());
        Assert.Equal(WindowCloseDecision.Cancel, gate.Request(false, true));
        gate.AuthorizeFinalClose();
        Assert.Equal(WindowCloseDecision.AllowFinalClose, gate.Request(true, true));
        Assert.False(gate.BeginShutdown());
    }

    [Fact]
    public void ReentrantScheduleIsRejectedWhileCallbackIsQueued()
    {
        var gate = new WindowCloseCoordinator();
        Assert.True(gate.TrySchedule(() =>
        {
            Assert.Equal(WindowCloseDecision.Cancel, gate.Request(false, false));
            Assert.False(gate.TrySchedule(() => true));
            return true;
        }));
        Assert.Equal(WindowCloseStage.Scheduled, gate.Stage);
    }
}
