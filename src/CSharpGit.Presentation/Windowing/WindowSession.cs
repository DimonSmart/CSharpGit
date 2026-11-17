using CSharpGit.Presentation.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CSharpGit.Presentation.Windowing;

internal enum WindowSessionState { Created, Active, PreparingForShutdown, Disposed }

internal sealed class WindowSession(
    IServiceScope scope,
    IWindowContentLifetime content,
    OpenRepositoryViewModel repository)
{
    private readonly object _gate = new();
    private Task? _prepareTask;
    private int _state = (int)WindowSessionState.Created;

    internal OpenRepositoryViewModel Repository { get; } = repository;
    internal WindowSessionState State => (WindowSessionState)Volatile.Read(ref _state);
    internal bool IsActive => State == WindowSessionState.Active;

    internal Task InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_state == (int)WindowSessionState.Created)
                Volatile.Write(ref _state, (int)WindowSessionState.Active);
        }
        return Task.CompletedTask;
    }

    internal Task PrepareForShutdownAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_state == (int)WindowSessionState.Disposed)
                return Task.CompletedTask;
            if (_prepareTask is not null)
                return _prepareTask;
            Volatile.Write(ref _state, (int)WindowSessionState.PreparingForShutdown);
            _prepareTask = content.PrepareForShutdownAsync(cancellationToken);
            return _prepareTask;
        }
    }

    internal void CompleteShutdown()
    {
        lock (_gate)
        {
            if (_state == (int)WindowSessionState.Disposed) return;
            Volatile.Write(ref _state, (int)WindowSessionState.Disposed);
        }

        try { content.CompleteShutdown(); }
        finally { scope.Dispose(); }
    }
}
