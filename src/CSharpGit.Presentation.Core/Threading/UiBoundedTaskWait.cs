using System.Diagnostics;

namespace CSharpGit.Presentation.Threading;

internal enum UiBoundedWaitOutcome
{
    Completed,
    Faulted,
    TimedOut
}

/// <summary>Bounds a UI wait without cancelling or abandoning its underlying operation.</summary>
internal static class UiBoundedTaskWait
{
    internal static async Task<UiBoundedWaitOutcome> WaitAsync(
        Task operation,
        TimeSpan budget,
        Action<Exception> reportFailure)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(reportFailure);

        try
        {
            await operation.WaitAsync(budget);
            return UiBoundedWaitOutcome.Completed;
        }
        catch (TimeoutException exception)
        {
            SafeReport(reportFailure, exception);
            // The writer may complete or fail after this UI no longer exists.
            // Continue observation without scheduling or cancelling the writer.
            _ = UiAsyncOperationRunner.RunAsync(
                () => operation,
                fault => SafeReport(reportFailure, fault));
            return UiBoundedWaitOutcome.TimedOut;
        }
        catch (Exception exception)
        {
            SafeReport(reportFailure, exception);
            return UiBoundedWaitOutcome.Faulted;
        }
    }

    private static void SafeReport(Action<Exception> reportFailure, Exception exception)
    {
        try { reportFailure(exception); }
        catch (Exception reportError)
        {
            Trace.TraceError($"UI shutdown failure reporter failed: {reportError}; original: {exception}");
        }
    }
}
