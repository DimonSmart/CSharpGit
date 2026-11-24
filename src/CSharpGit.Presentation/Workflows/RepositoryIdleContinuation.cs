using System.ComponentModel;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Presentation.Workflows;

// One subscription per feature. The owner supplies its own pending predicate and
// asynchronous continuation; this helper observes only the repository busy edge.
internal sealed class RepositoryIdleContinuation : IDisposable
{
    private readonly OpenRepositoryViewModel _repository;
    private readonly Action _onIdle;
    private bool _wasBusy;
    private bool _disposed;

    internal RepositoryIdleContinuation(OpenRepositoryViewModel repository, Action onIdle)
    {
        _repository = repository;
        _onIdle = onIdle;
        _wasBusy = repository.IsBusy;
        repository.PropertyChanged += Repository_PropertyChanged;
    }

    private void Repository_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_disposed || args.PropertyName != nameof(OpenRepositoryViewModel.IsBusy)) return;
        var becameIdle = _wasBusy && !_repository.IsBusy;
        _wasBusy = _repository.IsBusy;
        if (becameIdle) _onIdle();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _repository.PropertyChanged -= Repository_PropertyChanged;
    }
}
