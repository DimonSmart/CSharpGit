using System.ComponentModel;

namespace CSharpGit.Presentation;

// A window-owned title publisher with no XAML or root-page dependencies.
internal sealed class WindowTitleState : IDisposable
{
    private readonly INotifyPropertyChanged _source;
    private readonly Func<string> _getTitle;
    private readonly HashSet<string> _trackedProperties;
    private int _disposed;

    internal WindowTitleState(INotifyPropertyChanged source, Func<string> getTitle, params string[] trackedProperties)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _getTitle = getTitle ?? throw new ArgumentNullException(nameof(getTitle));
        _trackedProperties = new HashSet<string>(trackedProperties, StringComparer.Ordinal);
        _source.PropertyChanged += Repository_PropertyChanged;
    }

    internal string CurrentTitle => _getTitle();
    internal event Action<string>? TitleChanged;

    private void Repository_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (_trackedProperties.Count == 0 || _trackedProperties.Contains(args.PropertyName ?? string.Empty))
            TitleChanged?.Invoke(CurrentTitle);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _source.PropertyChanged -= Repository_PropertyChanged;
        TitleChanged = null;
    }
}
