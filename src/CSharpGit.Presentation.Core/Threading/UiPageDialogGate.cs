namespace CSharpGit.Presentation.Threading;

/// <summary>
/// A page-local modal gate. Shutdown cancels queued requests without
/// permitting a second dialog to appear after the owning page is closed.
/// </summary>
internal sealed class UiPageDialogGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    internal async Task<TResult> ExecuteAsync<TResult>(
        Func<Task<TResult>> show,
        TResult canceledResult,
        CancellationToken pageLifetime)
    {
        ArgumentNullException.ThrowIfNull(show);

        try
        {
            await _gate.WaitAsync(pageLifetime);
        }
        catch (OperationCanceledException) when (pageLifetime.IsCancellationRequested)
        {
            return canceledResult;
        }

        try
        {
            if (pageLifetime.IsCancellationRequested)
                return canceledResult;

            // Keep the semaphore throughout ShowAsync: concurrent dialogs sharing
            // a XamlRoot cannot be shown on Uno/WinUI.
            var result = await show();
            // Even if the modal completed successfully, closing the owner in
            // the meantime invalidates any accepted answer.
            return pageLifetime.IsCancellationRequested ? canceledResult : result;
        }
        finally
        {
            _gate.Release();
        }
    }
}
