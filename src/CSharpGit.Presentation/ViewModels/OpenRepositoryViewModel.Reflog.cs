namespace CSharpGit.Presentation.ViewModels;

public sealed partial class OpenRepositoryViewModel
{
    private bool _showReflog = CSharpGit.Presentation.AppSettingsContext.Current.ShowReflog;

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
            await CSharpGit.Presentation.AppSettingsContext.Current.SetShowReflogAsync(value);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ErrorMessage = $"Could not save Show reflog setting: {exception.Message}";
            Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(_logger, exception, "Show reflog setting persistence failed");
        }
    }
}
