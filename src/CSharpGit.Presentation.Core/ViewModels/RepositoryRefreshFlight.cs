namespace CSharpGit.Presentation.ViewModels;

/// <summary>
/// Coalesces refresh requests in one repository session. There is at most one
/// active pass and one pending rerun; publication is fenced against new requests.
/// Callbacks execute on the caller's captured synchronization context.
/// </summary>
internal sealed class RepositoryRefreshFlight
{
    private readonly object _sync = new();
    private TaskCompletionSource? _active;
    private bool _pending;
    private bool _pendingHistory;

    public Task RunAsync(
        bool includeHistory,
        Func<bool, Func<Action, bool>, Task> pass,
        Action starting,
        Action completed)
    {
        ArgumentNullException.ThrowIfNull(pass);
        lock (_sync)
        {
            if (_active is not null)
            {
                _pending = true;
                _pendingHistory |= includeHistory;
                return _active.Task;
            }

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _active = completion;
            _ = RunCoreAsync(completion, includeHistory, pass, starting, completed);
            return completion.Task;
        }
    }

    private bool PublishIfCurrent(Action publish)
    {
        lock (_sync)
        {
            if (_pending) return false;
            publish();
            return true;
        }
    }

    private async Task RunCoreAsync(
        TaskCompletionSource completion,
        bool includeHistory,
        Func<bool, Func<Action, bool>, Task> pass,
        Action starting,
        Action completed)
    {
        // Do not run callbacks inside RunAsync's initial lock or before the
        // flight has been registered.
        await Task.Yield();
        var started = false;
        try
        {
            starting();
            started = true;

            while (true)
            {
                Exception? failure = null;
                try
                {
                    await pass(includeHistory, PublishIfCurrent);
                }
                catch (Exception exception)
                {
                    failure = exception;
                }

                lock (_sync)
                {
                    if (!_pending)
                    {
                        completed();
                        _active = null;
                        if (failure is null) completion.TrySetResult();
                        else completion.TrySetException(failure);
                        return;
                    }

                    // The just-finished read has been superseded. A failed old
                    // pass does not abort the latest requested state refresh.
                    includeHistory |= _pendingHistory;
                    _pending = false;
                    _pendingHistory = false;
                }
            }
        }
        catch (Exception exception)
        {
            lock (_sync)
            {
                _active = null;
                if (started)
                {
                    try { completed(); }
                    catch { }
                }
                completion.TrySetException(exception);
            }
        }
    }
}
