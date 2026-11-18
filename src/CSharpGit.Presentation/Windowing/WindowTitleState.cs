using System.ComponentModel;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Presentation.Windowing;

// Window-scoped presentation state: native window bindings never subscribe to MainPage.
internal sealed class WindowTitleState : IDisposable
{
    private readonly OpenRepositoryViewModel _repository;
    private int _disposed;

    internal WindowTitleState(OpenRepositoryViewModel repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _repository.PropertyChanged += Repository_PropertyChanged;
    }

    internal string CurrentTitle => MainWindowTitleFormatter.Format(
        _repository.Repository,
        _repository.CurrentBranchName,
        _repository.CurrentHeadCommit,
        _repository.IsDetachedHead);

    internal event Action<string>? TitleChanged;

    private void Repository_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        if (args.PropertyName is nameof(OpenRepositoryViewModel.Repository)
            or nameof(OpenRepositoryViewModel.CurrentBranchName)
            or nameof(OpenRepositoryViewModel.CurrentHeadCommit)
            or nameof(OpenRepositoryViewModel.IsDetachedHead))
            TitleChanged?.Invoke(CurrentTitle);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _repository.PropertyChanged -= Repository_PropertyChanged;
        TitleChanged = null;
    }
}
