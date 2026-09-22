namespace CSharpGit.Presentation.ViewModels;

internal enum HistoryDisplayMode
{
    CurrentBranch,
    AllReferences,
    AllReferencesWithReflog
}

public sealed partial class OpenRepositoryViewModel
{
    private bool _showReflog;

    public bool ShowReflog
    {
        get => _showReflog;
        set
        {
            if (_showReflog == value) return;

            _showReflog = value;
            Notify();
            if (value && _selectedScope != Scopes[0])
            {
                _selectedScope = Scopes[0];
                Notify(nameof(SelectedScope));
            }

            _ = PersistShowReflogAsync(value);
            _ = LoadHistoryAsync(true);
        }
    }

    internal HistoryDisplayMode HistoryDisplayMode =>
        _showReflog
            ? HistoryDisplayMode.AllReferencesWithReflog
            : _selectedScope == Scopes[1]
                ? HistoryDisplayMode.CurrentBranch
                : HistoryDisplayMode.AllReferences;

    internal void SetHistoryDisplayMode(HistoryDisplayMode mode)
    {
        var (scope, showReflog) = mode switch
        {
            HistoryDisplayMode.CurrentBranch => (Scopes[1], false),
            HistoryDisplayMode.AllReferences => (Scopes[0], false),
            HistoryDisplayMode.AllReferencesWithReflog => (Scopes[0], true),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };

        var scopeChanged = _selectedScope != scope;
        var reflogChanged = _showReflog != showReflog;
        if (!scopeChanged && !reflogChanged) return;

        _selectedScope = scope;
        _showReflog = showReflog;

        if (scopeChanged) Notify(nameof(SelectedScope));
        if (reflogChanged)
        {
            Notify(nameof(ShowReflog));
            _ = PersistShowReflogAsync(showReflog);
        }

        _ = LoadHistoryAsync(true);
    }

    internal async Task DisableReflogForScopedHistoryAsync()
    {
        if (!_showReflog) return;

        _showReflog = false;
        Notify(nameof(ShowReflog));
        await PersistShowReflogAsync(false);
    }

    private void DisableReflogForScopeChange()
    {
        if (!_showReflog) return;

        _showReflog = false;
        Notify(nameof(ShowReflog));
        _ = PersistShowReflogAsync(false);
    }

    private async Task PersistShowReflogAsync(bool value)
    {
        try
        {
            await _settings.SetShowReflogAsync(value);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ErrorMessage = $"Could not save Show reflog setting: {exception.Message}";
            Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(_logger, exception, "Show reflog setting persistence failed");
        }
    }
}
