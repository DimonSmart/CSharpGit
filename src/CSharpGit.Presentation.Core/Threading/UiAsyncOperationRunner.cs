using System.Diagnostics;

namespace CSharpGit.Presentation.Threading;

/// <summary>Observes a UI-owned operation without scheduling it on another thread.</summary>
internal static class UiAsyncOperationRunner
{
    internal static async Task RunAsync(
        Func<Task> operation,
        Action<Exception> reportError,
        Func<bool>? isCancellationExpected = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(reportError);

        try
        {
            await operation();
        }
        catch (Exception exception)
        {
            if (exception is OperationCanceledException && isCancellationExpected is not null)
            {
                try
                {
                    if (isCancellationExpected()) return;
                }
                catch (Exception guardFailure)
                {
                    Trace.TraceError($"UI cancellation guard failed: {guardFailure}");
                }
            }

            try
            {
                reportError(exception);
            }
            catch (Exception reportingFailure)
            {
                Trace.TraceError($"UI error reporting failed: {reportingFailure}");
            }
        }
    }
}
