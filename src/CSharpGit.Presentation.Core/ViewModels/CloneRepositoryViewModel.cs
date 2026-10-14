using System.ComponentModel;
using System.Runtime.CompilerServices;
using CSharpGit.Application;
using CSharpGit.Application.Abstractions;
using CSharpGit.Application.Exceptions;

namespace CSharpGit.Presentation.ViewModels;

public sealed class CloneRepositoryViewModel : INotifyPropertyChanged
{
    private readonly IRepositoryCloneService _cloneService;
    private readonly IAppSettingsService _settings;
    private readonly IFolderPicker _folderPicker;
    private string _repositoryUrl = string.Empty;
    private string _localDirectory = string.Empty;
    private bool _targetIsUserOwned;
    private bool _isBusy;
    private string? _errorMessage;
    private string? _clonedPath;
    private CancellationTokenSource? _activeCloneCancellation;

    public CloneRepositoryViewModel(
        IRepositoryCloneService cloneService,
        IAppSettingsService settings,
        IFolderPicker folderPicker)
    {
        _cloneService = cloneService ?? throw new ArgumentNullException(nameof(cloneService));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _folderPicker = folderPicker ?? throw new ArgumentNullException(nameof(folderPicker));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string RepositoryUrl
    {
        get => _repositoryUrl;
        set
        {
            var next = value ?? string.Empty;
            if (string.Equals(_repositoryUrl, next, StringComparison.Ordinal))
                return;
            _repositoryUrl = next;
            Notify();
            Notify(nameof(RepositoryDisplayName));
            if (!_targetIsUserOwned)
                RefreshAutomaticTarget();
            Notify(nameof(CanClone));
        }
    }

    public string LocalDirectory
    {
        get => _localDirectory;
        set => SetLocalDirectory(value ?? string.Empty, userOwned: true);
    }

    public string RepositoryDisplayName =>
        RepositoryNameResolver.Resolve(RepositoryUrl) ?? "repository";

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            Notify();
            Notify(nameof(CanClone));
        }
    }

    public bool CanClone =>
        !IsBusy
        && !string.IsNullOrWhiteSpace(RepositoryUrl)
        && !string.IsNullOrWhiteSpace(LocalDirectory);

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (string.Equals(_errorMessage, value, StringComparison.Ordinal))
                return;
            _errorMessage = value;
            Notify();
        }
    }

    public string? ClonedPath
    {
        get => _clonedPath;
        private set
        {
            if (string.Equals(_clonedPath, value, StringComparison.Ordinal))
                return;
            _clonedPath = value;
            Notify();
        }
    }

    internal bool TargetIsUserOwned => _targetIsUserOwned;

    internal void Reset()
    {
        Cancel();
        _targetIsUserOwned = false;
        _repositoryUrl = string.Empty;
        Notify(nameof(RepositoryUrl));
        Notify(nameof(RepositoryDisplayName));
        SetLocalDirectory(string.Empty, userOwned: false);
        ErrorMessage = null;
        ClonedPath = null;
        Notify(nameof(CanClone));
    }

    internal async Task BrowseAsync()
    {
        if (IsBusy) return;

        ErrorMessage = null;
        try
        {
            var selected = await _folderPicker.PickFolderAsync();
            if (selected is not null)
                LocalDirectory = selected;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            ErrorMessage =
                $"The system folder picker could not be opened: {exception.Message}";
        }
    }

    internal async Task<bool> CloneAsync(
        CancellationToken cancellationToken = default)
    {
        if (!CanClone) return false;

        IsBusy = true;
        ErrorMessage = null;
        ClonedPath = null;
        using var linkedCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _activeCloneCancellation = linkedCancellation;
        try
        {
            await _cloneService.CloneAsync(
                RepositoryUrl.Trim(),
                LocalDirectory,
                linkedCancellation.Token);
            ClonedPath = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(LocalDirectory.Trim()));
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (RepositoryCloneException exception)
        {
            ErrorMessage = FormatError(exception);
            return false;
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Could not clone repository.\n{exception.Message}";
            return false;
        }
        finally
        {
            if (ReferenceEquals(_activeCloneCancellation, linkedCancellation))
                _activeCloneCancellation = null;
            IsBusy = false;
        }
    }

    internal void Cancel() => _activeCloneCancellation?.Cancel();

    private void RefreshAutomaticTarget()
    {
        var repositoryName = RepositoryNameResolver.Resolve(RepositoryUrl);
        var target = repositoryName is null
            ? string.Empty
            : Path.Combine(_settings.DefaultRepositoriesDirectory, repositoryName);
        SetLocalDirectory(target, userOwned: false);
    }

    private void SetLocalDirectory(string value, bool userOwned)
    {
        if (string.Equals(_localDirectory, value, StringComparison.Ordinal))
        {
            if (userOwned)
                _targetIsUserOwned = true;
            return;
        }

        _localDirectory = value;
        if (userOwned)
            _targetIsUserOwned = true;
        Notify(nameof(LocalDirectory));
        Notify(nameof(CanClone));
    }

    private static string FormatError(RepositoryCloneException exception)
    {
        var message = exception.Kind switch
        {
            RepositoryCloneFailureKind.InvalidTargetPath =>
                "Enter a valid absolute local directory path.",
            RepositoryCloneFailureKind.TargetIsFile =>
                "A file already exists at the selected path.",
            RepositoryCloneFailureKind.TargetNotEmpty =>
                "The selected directory is not empty.",
            RepositoryCloneFailureKind.TargetCannotBeCreated =>
                "The selected repository directory cannot be created.",
            RepositoryCloneFailureKind.GitUnavailable =>
                "Git could not be started. Make sure Git is installed and available through PATH.",
            RepositoryCloneFailureKind.AccessDenied =>
                "Access to the selected directory was denied.",
            RepositoryCloneFailureKind.GitFailed =>
                "Could not clone repository.",
            _ => "Could not clone repository."
        };

        return string.IsNullOrWhiteSpace(exception.GitDiagnostic)
            ? message
            : $"{message}\n\n{exception.GitDiagnostic}";
    }

    private void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
