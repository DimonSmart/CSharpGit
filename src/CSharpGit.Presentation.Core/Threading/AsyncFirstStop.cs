namespace CSharpGit.Presentation.Threading;
internal sealed class AsyncFirstStop<TReason, TResult>
{
    private readonly object _gate = new();
    private Task<TResult>? _completion;
    internal Task<TResult> StopAsync(TReason reason, Func<TReason, Task<TResult>> stop)
    {
        ArgumentNullException.ThrowIfNull(stop);
        TaskCompletionSource<TResult> result;
        lock (_gate)
        {
            if (_completion is not null) return _completion;
            result = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _completion = result.Task;
        }
        _ = FinishAsync(stop, reason, result);
        return result.Task;
    }
    private static async Task FinishAsync(
        Func<TReason, Task<TResult>> stop, TReason reason, TaskCompletionSource<TResult> result)
    {
        try { result.TrySetResult(await stop(reason).ConfigureAwait(false)); }
        catch (Exception exception) { result.TrySetException(exception); }
    }
}
