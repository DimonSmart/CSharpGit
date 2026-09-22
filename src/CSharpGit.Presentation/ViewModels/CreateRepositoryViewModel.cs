using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CSharpGit.Application.Abstractions;
using CSharpGit.Application.Exceptions;

namespace CSharpGit.Presentation.ViewModels;

public sealed class CreateRepositoryViewModel : INotifyPropertyChanged
{
    private readonly IRepositoryCreationService _creationService;
    private readonly IFolderPicker _folderPicker;
    private readonly AsyncCommand _createCommand;
    private readonly AsyncCommand _browseCommand;
    private string _directory = string.Empty;
    private RepositoryCreationKind _repositoryType = RepositoryCreationKind.WorkingTree;
    private bool _isBusy;
    private string? _errorMessage;
    private string? _createdPath;

    public CreateRepositoryViewModel(
        IRepositoryCreationService creationService,
        IFolderPicker folderPicker)
    {
        _creationService = creationService
            ?? throw new ArgumentNullException(nameof(creationService));
        _folderPicker = folderPicker
            ?? throw new ArgumentNullException(nameof(folderPicker));
        _createCommand = new AsyncCommand(
            () => CreateAsync(),
            () => CanCreate);
        _browseCommand = new AsyncCommand(
            BrowseAsync,
            () => !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Directory
    {
        get => _directory;
        set
        {
            if (string.Equals(_directory, value, StringComparison.Ordinal))
                return;
            _directory = value;
            Notify();
            Notify(nameof(CanCreate));
            RaiseCommands();
        }
    }

    public RepositoryCreationKind RepositoryType
    {
        get => _repositoryType;
        set
        {
            if (_repositoryType == value)
                return;
            _repositoryType = value;
            Notify();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value)
                return;
            _isBusy = value;
            Notify();
            Notify(nameof(CanCreate));
            RaiseCommands();
        }
    }

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

    public string? CreatedPath
    {
        get => _createdPath;
        private set
        {
            if (string.Equals(_createdPath, value, StringComparison.Ordinal))
                return;
            _createdPath = value;
            Notify();
        }
    }

    public bool CanCreate =>
        !IsBusy
        && !string.IsNullOrWhiteSpace(Directory);

    public ICommand CreateCommand => _createCommand;
    public ICommand BrowseCommand => _browseCommand;

    internal void Reset()
    {
        Directory = string.Empty;
        RepositoryType = RepositoryCreationKind.WorkingTree;
        ErrorMessage = null;
        CreatedPath = null;
    }

    internal async Task BrowseAsync()
    {
        if (IsBusy)
            return;

        ErrorMessage = null;
        try
        {
            var selected = await _folderPicker.PickFolderAsync();
            if (selected is not null)
                Directory = selected;
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

    internal async Task<bool> CreateAsync(
        CancellationToken cancellationToken = default)
    {
        if (!CanCreate)
            return false;

        IsBusy = true;
        ErrorMessage = null;
        CreatedPath = null;
        try
        {
            await _creationService.CreateAsync(
                Directory,
                RepositoryType,
                cancellationToken);
            CreatedPath = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(Directory.Trim()));
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (RepositoryCreationException exception)
        {
            ErrorMessage = FormatError(exception);
            return false;
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Could not create repository.\n{exception.Message}";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string FormatError(RepositoryCreationException exception)
    {
        var message = exception.Kind switch
        {
            RepositoryCreationFailureKind.InvalidPath =>
                "Enter a valid absolute directory path.",
            RepositoryCreationFailureKind.TargetIsFile =>
                "A file already exists at the selected path.",
            RepositoryCreationFailureKind.TargetCannotBeCreated =>
                "The selected repository directory cannot be created.",
            RepositoryCreationFailureKind.RepositoryAlreadyExists =>
                "A Git repository already exists in this directory.",
            RepositoryCreationFailureKind.BareTargetNotEmpty =>
                "A central repository must be created in an empty directory.",
            RepositoryCreationFailureKind.GitUnavailable =>
                "Git could not be started. Make sure Git is installed and available through PATH.",
            RepositoryCreationFailureKind.AccessDenied =>
                "Access to the selected directory was denied.",
            RepositoryCreationFailureKind.GitFailed =>
                "Could not create repository.",
            _ => "Could not create repository."
        };

        return string.IsNullOrWhiteSpace(exception.GitDiagnostic)
            ? message
            : $"{message}\n\n{exception.GitDiagnostic}";
    }

    private void RaiseCommands()
    {
        _createCommand.RaiseCanExecuteChanged();
        _browseCommand.RaiseCanExecuteChanged();
    }

    private void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
