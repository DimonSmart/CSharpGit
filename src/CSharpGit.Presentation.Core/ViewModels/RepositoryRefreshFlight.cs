namespace CSharpGit.Presentation.ViewModels;

/// <summary>
/// Coalesces refresh requests within one repository session. Pending requests
/// retain the strongest requirements: full state and history.
/// </summary>
internal sealed class RepositoryRefreshFlight
{
    private readonly object _sync = new();
    private TaskCompletionSource? _active;
    private bool _pending;
    private bool _pendingHistory;
    private bool _pendingFull;

    // The original API is retained for callers which always perform full reads.
    public Task RunAsync(
        bool includeHistory,
        Func<bool, Func<Action, bool>, Task> pass,
        Action starting,
        Action completed) =>
        RunAsync(includeHistory, localOnly: false,
            (history, _, publish) => pass(history, publish), starting, completed);

    public Task RunAsync(
        bool includeHistory,
        bool localOnly,
        Func<bool, bool, Func<Action, bool>, Task> pass,
        Action starting,
        Action completed)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(starting);
        ArgumentNullException.ThrowIfNull(completed);

        lock (_sync)
        {
            if (_active is not null)
            {
                _pending = true;
                _pendingHistory |= includeHistory;
                _pendingFull |= !localOnly;
                return _active.Task;
            }

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _active = completion;
            // RunCoreAsync yields before invoking any external code.
            _ = RunCoreAsync(completion, includeHistory, localOnly, pass, starting, completed);
            return completion.Task;
        }
    }

    private bool PublishIfCurrent(Action publish)
    {
        ArgumentNullException.ThrowIfNull(publish);
        lock (_sync)
        {
            if (_pending) return false;
        }

        // Never invoke external UI callbacks under the flight's internal lock.
        // A request arriving during this synchronous logical commit is handled
        // by a subsequent pass, so its final state always wins.
        publish();
        return true;
    }

    private async Task RunCoreAsync(
        TaskCompletionSource completion,
        bool includeHistory,
        bool localOnly,
        Func<bool, bool, Func<Action, bool>, Task> pass,
        Action starting,
        Action completed)
    {
        await Task.Yield();
        var startingInvoked = false;
        var detached = false;
        Exception? terminalError = null;

        try
        {
            startingInvoked = true;
            starting();

            while (true)
            {
                Exception? readError = null;
                try
                {
                    await pass(includeHistory, localOnly, PublishIfCurrent);
                }
                catch (Exception exception)
                {
                    readError = exception;
                }

                lock (_sync)
                {
                    if (_pending)
                    {
                        // The earlier read (and any earlier failure) was superseded.
                        includeHistory |= _pendingHistory;
                        localOnly &= !_pendingFull;
                        _pending = false;
                        _pendingHistory = false;
                        _pendingFull = false;
                    }
                    else
                    {
                        // Detach atomically with checking pending. Reentrant calls
                        // from completed() must create a separate, live flight.
                        _active = null;
                        detached = true;
                    }
                }

                if (!detached) continue;
                if (readError is not null) throw readError;
                break;
            }
        }
        catch (Exception exception)
        {
            terminalError = exception;
        }
        finally
        {
            if (!detached)
            {
                lock (_sync)
                {
                    if (ReferenceEquals(_active, completion))
                    {
                        _active = null;
                        _pending = false;
                        _pendingHistory = false;
                        _pendingFull = false;
                    }
                }
            }

            // Both callbacks and their event subscribers execute outside _sync.
            // An exception cannot strand existing awaiters or a newer flight.
            if (startingInvoked)
            {
                try { completed(); }
                catch (Exception exception) { terminalError ??= exception; }
            }

            if (terminalError is null) completion.TrySetResult();
            else completion.TrySetException(terminalError);
        }
    }
}
