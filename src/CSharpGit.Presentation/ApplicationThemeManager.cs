using CSharpGit.Application.Abstractions;
using Microsoft.UI.Xaml;

namespace CSharpGit.Presentation;

internal sealed class ApplicationThemeManager : IDisposable
{
    private readonly IAppSettingsService _settings;
    private readonly object _gate = new();
    private readonly HashSet<FrameworkElement> _roots = [];
    private bool _disposed;

    public ApplicationThemeManager(IAppSettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        _settings.Changed += Settings_Changed;
    }

    public IDisposable Register(FrameworkElement root)
    {
        ArgumentNullException.ThrowIfNull(root);

        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ApplicationThemeManager));
            _roots.Add(root);
        }

        ApplyCurrentTheme(root);
        return new Registration(this, root);
    }

    internal static ElementTheme ToElementTheme(ApplicationThemeMode mode) => mode switch
    {
        ApplicationThemeMode.System => ElementTheme.Default,
        ApplicationThemeMode.Light => ElementTheme.Light,
        ApplicationThemeMode.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default
    };

    private void Settings_Changed(object? sender, EventArgs e)
    {
        FrameworkElement[] roots;
        lock (_gate)
        {
            if (_disposed) return;
            roots = [.. _roots];
        }

        foreach (var root in roots) ApplyCurrentTheme(root);
    }

    private void ApplyCurrentTheme(FrameworkElement root)
    {
        if (root.DispatcherQueue.HasThreadAccess)
        {
            ApplyCurrentThemeOnDispatcher(root);
            return;
        }

        root.DispatcherQueue.TryEnqueue(() => ApplyCurrentThemeOnDispatcher(root));
    }

    private void ApplyCurrentThemeOnDispatcher(FrameworkElement root)
    {
        lock (_gate)
        {
            if (_disposed || !_roots.Contains(root)) return;
        }

        var requestedTheme = ToElementTheme(_settings.ThemeMode);
        if (root.RequestedTheme != requestedTheme) root.RequestedTheme = requestedTheme;
    }

    private void Unregister(FrameworkElement root)
    {
        lock (_gate) _roots.Remove(root);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _roots.Clear();
        }

        _settings.Changed -= Settings_Changed;
    }

    private sealed class Registration(ApplicationThemeManager owner, FrameworkElement root) : IDisposable
    {
        private ApplicationThemeManager? _owner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.Unregister(root);
        }
    }
}
