namespace CSharpGit.Presentation.Windowing;

// Window-local UI callbacks run on the owning dispatcher. The core owns sequencing,
// shutdown admission, error aggregation and the shared asynchronous preparation result.
internal sealed class WindowWorkspaceLifetime
{
    private int _shutdownStarted;
    private int _shutdownFinalized;
    private readonly TaskCompletionSource _prepared = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Action[] _prepare = [];
    private Func<Task> _waitForDiagnostics = () => Task.CompletedTask;
    private Action[] _complete = [];
    private bool _configured;

    internal bool IsShuttingDown => Volatile.Read(ref _shutdownStarted) != 0;

    internal void Configure(Action[] prepare, Func<Task> waitForDiagnostics, Action[] complete)
    {
        if (_configured) throw new InvalidOperationException("Workspace lifetime already configured.");
        _configured = true;
        _prepare = prepare;
        _waitForDiagnostics = waitForDiagnostics;
        _complete = complete;
    }

    internal Task PrepareAsync()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) == 0)
            _ = PrepareCoreAsync();
        return _prepared.Task;
    }

    private async Task PrepareCoreAsync()
    {
        var failures = RunAll(_prepare);
        try { await _waitForDiagnostics(); }
        catch (Exception exception) { failures.Add(exception); }

        if (failures.Count == 0) _prepared.TrySetResult();
        else _prepared.TrySetException(new AggregateException("Workspace preparation failed.", failures));
    }

    internal void Complete()
    {
        if (Interlocked.Exchange(ref _shutdownFinalized, 1) != 0) return;
        // A failed initialization may go directly to completion. Later preparation
        // must not run callbacks against disposed controls or wait indefinitely.
        if (Interlocked.Exchange(ref _shutdownStarted, 1) == 0)
            _prepared.TrySetResult();
        var failures = RunAll(_complete);
        if (failures.Count != 0)
            throw new AggregateException("Workspace cleanup failed.", failures);
    }

    private static List<Exception> RunAll(IEnumerable<Action> actions)
    {
        var failures = new List<Exception>();
        foreach (var action in actions)
        {
            try { action(); }
            catch (Exception exception) { failures.Add(exception); }
        }
        return failures;
    }
}
