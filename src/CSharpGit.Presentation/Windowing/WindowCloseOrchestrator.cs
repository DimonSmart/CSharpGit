using CSharpGit.Presentation.Threading;
using Microsoft.UI.Xaml;

namespace CSharpGit.Presentation.Windowing;

// Native Window and host hooks stay in App. Close admission and shutdown sequencing live here.
public interface IWindowCloseHost
{
    void Report(string phase);
    void Log(string phase);
    void LogFailure(Exception exception, string operation);
    Task StopHostAsync();
    void ReleaseWindowBindings();
    void CloseAuthorizedWindow();
    void FailFinalNativeClose();
}

internal interface ICloseConfirmationDialog
{
    Task<bool> ConfirmDiscardCommitMessageAsync(CancellationToken cancellationToken);
}

internal sealed class WindowCloseOrchestrator(
    Window window,
    UIElement content,
    WindowSession session,
    ICloseConfirmationDialog confirmation,
    IWindowCloseHost host)
{
    private readonly WindowCloseCoordinator _coordinator = new();
    private static readonly TimeSpan DiagnosticsShutdownBudget = TimeSpan.FromSeconds(3);

    internal WindowCloseStage Stage => _coordinator.Stage;

    internal bool RequestClose()
    {
        host.Report("Closing");
        if (_coordinator.Stage == WindowCloseStage.FinalClose)
        {
            host.Report("FinalCloseAllowed");
            return true;
        }

        var decision = _coordinator.Request(
            session.IsUnsafeToClose,
            session.RequiresCloseConfirmation);

        switch (decision)
        {
            case WindowCloseDecision.AllowFinalClose:
                host.Report("FinalCloseAllowed");
                return true;
            case WindowCloseDecision.Confirm:
                host.Report("CloseCanceled: commit draft requires confirmation");
                host.Log("ConfirmationRequired");
                _ = UiAsyncOperationRunner.RunAsync(
                    ConfirmAndCloseAsync,
                    exception =>
                    {
                        _coordinator.CancelConfirmation();
                        host.LogFailure(exception, "Close confirmation");
                    });
                return false;
            case WindowCloseDecision.Schedule:
                ScheduleShutdown();
                return false;
            default:
                if (session.IsUnsafeToClose)
                {
                    host.Report("CloseCanceled: protected operation is active");
                    host.Log("ProtectedOperationRejected");
                }
                return false;
        }
    }

    internal void RequestApplicationRestartClose(object contentIdentity)
    {
        if (Stage != WindowCloseStage.Idle
            || !ReferenceEquals(window.Content, contentIdentity)
            || session.IsUnsafeToClose)
            return;
        ScheduleShutdown();
    }

    private async Task ConfirmAndCloseAsync()
    {
        try
        {
            host.Report("CloseConfirmationStarted");
            var confirmed = await confirmation.ConfirmDiscardCommitMessageAsync(CancellationToken.None);
            var accepted = _coordinator.CompleteConfirmation(
                confirmed,
                session.IsUnsafeToClose || !ReferenceEquals(window.Content, content));
            if (!accepted)
            {
                host.Report("CloseConfirmationCanceled");
                host.Log("ConfirmationCanceled");
                return;
            }
            host.Report("CloseConfirmed");
            host.Log("ConfirmationAccepted");
            ScheduleShutdown();
        }
        catch (Exception exception)
        {
            _coordinator.CancelConfirmation();
            host.LogFailure(exception, "Close confirmation");
        }
    }

    private void ScheduleShutdown()
    {
        try
        {
            var scheduled = _coordinator.TrySchedule(() =>
            {
                // macOS windowShouldClose must return before managed cleanup.
                if (OperatingSystem.IsMacOS())
                    return window.DispatcherQueue.TryEnqueue(BeginShutdown);

                // Keep the existing Windows WM_CLOSE and Linux paths.
                BeginShutdown();
                return true;
            });
            if (!scheduled)
            {
                host.Report("CloseSchedulingRejected");
                host.Log("ScheduleRejected");
                host.LogFailure(
                    new InvalidOperationException("Window dispatcher rejected shutdown scheduling."),
                    "Window shutdown scheduling");
                return;
            }
            host.Report("CloseScheduled");
            host.Log("Scheduled");
        }
        catch (Exception exception)
        {
            host.Report("CloseSchedulingFailed");
            host.LogFailure(exception, "Window shutdown scheduling");
        }
    }

    private void BeginShutdown()
    {
        if (!_coordinator.BeginShutdown()) return;
        host.Report("CloseAccepted");
        host.Report("ShutdownStarted");
        host.Log("CleanupStarted");
        _ = UiAsyncOperationRunner.RunAsync(
            FinalizeWindowShutdownAsync,
            exception => host.LogFailure(exception, "Window shutdown"));
    }

    private async Task FinalizeWindowShutdownAsync()
    {
        try
        {
            try
            {
                await UiBoundedTaskWait.WaitAsync(
                    session.PrepareForShutdownAsync(CancellationToken.None),
                    DiagnosticsShutdownBudget,
                    exception => host.LogFailure(exception, "History diagnostics shutdown"));
            }
            finally
            {
                // The diagnostic budget cannot bypass mandatory cleanup.
                host.ReleaseWindowBindings();
                session.CompleteShutdown();
                host.Report("PageShutdownCompleted");
                host.Log("PageShutdownCompleted");
            }
        }
        catch (Exception exception) { host.LogFailure(exception, "Final window cleanup"); }
        finally
        {
            if (OperatingSystem.IsMacOS())
            {
                try { await host.StopHostAsync(); }
                catch (Exception exception) { host.LogFailure(exception, "Host shutdown before window close"); }
            }

            _coordinator.AuthorizeFinalClose();
            host.Log("FinalCloseAuthorized");
            try
            {
                if (window.DispatcherQueue.HasThreadAccess)
                    host.CloseAuthorizedWindow();
                else if (!window.DispatcherQueue.TryEnqueue(host.CloseAuthorizedWindow))
                {
                    host.LogFailure(new InvalidOperationException(
                        "Window dispatcher rejected final close after cleanup."), "Final window close");
                    host.FailFinalNativeClose();
                }
            }
            catch (Exception exception)
            {
                host.LogFailure(exception, "Final window close");
                host.FailFinalNativeClose();
            }
        }
    }
}
