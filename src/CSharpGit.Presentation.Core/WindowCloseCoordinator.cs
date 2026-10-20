namespace CSharpGit.Presentation;

// Pure close-admission state. UI dispatch, confirmation and resource cleanup
// remain owned by App, but cannot independently authorize a second shutdown.
internal enum WindowCloseStage { Idle, Confirming, Scheduled, ShuttingDown, FinalClose }

internal enum WindowCloseDecision { Cancel, Confirm, Schedule, AllowFinalClose }

internal sealed class WindowCloseCoordinator
{
    internal WindowCloseStage Stage { get; private set; } = WindowCloseStage.Idle;

    internal WindowCloseDecision Request(bool isUnsafe, bool requiresConfirmation)
    {
        if (Stage == WindowCloseStage.FinalClose) return WindowCloseDecision.AllowFinalClose;
        if (isUnsafe || Stage != WindowCloseStage.Idle) return WindowCloseDecision.Cancel;

        if (!requiresConfirmation) return WindowCloseDecision.Schedule;
        Stage = WindowCloseStage.Confirming;
        return WindowCloseDecision.Confirm;
    }

    internal bool CompleteConfirmation(bool confirmed, bool isUnsafe)
    {
        if (Stage != WindowCloseStage.Confirming) return false;
        Stage = WindowCloseStage.Idle;
        return confirmed && !isUnsafe;
    }

    internal void CancelConfirmation()
    {
        if (Stage == WindowCloseStage.Confirming) Stage = WindowCloseStage.Idle;
    }

    // The queued shutdown must be accepted before page/host disposal begins.
    // Roll back admission on rejection or exception so the user can retry.
    internal bool TrySchedule(Func<bool> enqueue)
    {
        ArgumentNullException.ThrowIfNull(enqueue);
        if (Stage != WindowCloseStage.Idle) return false;
        Stage = WindowCloseStage.Scheduled;
        try
        {
            if (enqueue()) return true;
        }
        catch
        {
            Stage = WindowCloseStage.Idle;
            throw;
        }
        Stage = WindowCloseStage.Idle;
        return false;
    }

    internal bool BeginShutdown()
    {
        if (Stage != WindowCloseStage.Scheduled) return false;
        Stage = WindowCloseStage.ShuttingDown;
        return true;
    }

    internal void AuthorizeFinalClose()
    {
        if (Stage != WindowCloseStage.ShuttingDown)
            throw new InvalidOperationException("Final close requires a started shutdown.");
        Stage = WindowCloseStage.FinalClose;
    }
}
