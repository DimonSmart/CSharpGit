namespace CSharpGit.Presentation.Windowing;

/// <summary>Idempotent UI lifetime owned by one window. Closing admission is separate.</summary>
internal sealed class WindowWorkspaceLifetime
{
    private int _shutdownStarted;
    private int _shutdownFinalized;
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
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0) return Task.CompletedTask;
        RunAll(_prepare);
        return _waitForDiagnostics();
    }

    internal void Complete()
    {
        if (Interlocked.Exchange(ref _shutdownFinalized, 1) != 0) return;
        Volatile.Write(ref _shutdownStarted, 1);
        RunAll(_complete);
    }

    private static void RunAll(IEnumerable<Action> actions)
    {
        List<Exception>? failures = null;
        foreach (var action in actions)
        {
            try { action(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }
        if (failures is not null) throw new AggregateException("Workspace cleanup failed.", failures);
    }
}
