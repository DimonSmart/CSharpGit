using System.ComponentModel;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Presentation.Windowing;

// Window-scoped presentation state: native window bindings never subscribe to MainPage.
internal sealed class WindowTitleState : IDisposable
{
    private readonly INotifyPropertyChanged _source;
    private readonly Func<string> _getTitle;
    private int _disposed;

    internal WindowTitleState(OpenRepositoryViewModel repository)
        : this(repository, () => MainWindowTitleFormatter.Format(
            repository.Repository,
            repository.CurrentBranchName,
            repository.CurrentHeadCommit,
            repository.IsDetachedHead))
    {
    }

    // A narrow observable source makes lifetime/isolation verifiable without XAML.
    internal WindowTitleState(INotifyPropertyChanged source, Func<string> getTitle)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _getTitle = getTitle ?? throw new ArgumentNullException(nameof(getTitle));
        _source.PropertyChanged += Repository_PropertyChanged;
    }

    internal string CurrentTitle => _getTitle();

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

        _source.PropertyChanged -= Repository_PropertyChanged;
        TitleChanged = null;
    }
}
