using System.ComponentModel;
using System.Runtime.CompilerServices;
using CSharpGit.Application;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

internal sealed class RepositoryIdentitySettingsViewModel : INotifyPropertyChanged
{
    private readonly IRepositoryIdentityService _service;
    private readonly Func<Repository?> _repositoryAccessor;
    private RepositoryIdentitySnapshot? _snapshot;
    private string? _loadedRepositoryKey;
    private string _repositoryName = string.Empty;
    private string _repositoryPath = string.Empty;
    private string _nameText = string.Empty;
    private string _emailText = string.Empty;
    private string? _repositoryReloadNotice;
    private bool _isBusy;
    private bool _isStale;
    private long _refreshGeneration;

    internal RepositoryIdentitySettingsViewModel(
        IRepositoryIdentityService service,
        Func<Repository?> repositoryAccessor)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _repositoryAccessor = repositoryAccessor ?? throw new ArgumentNullException(nameof(repositoryAccessor));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool HasRepository => _snapshot is not null;
    public bool HasNoRepository => !HasRepository && !_isStale;
    public bool IsBusy => _isBusy;
    public bool IsStale => _isStale;
    public bool CanEdit => HasRepository && !_isStale && !_isBusy;

    public string RepositoryName => _repositoryName;
    public string RepositoryPath => _repositoryPath;

    public string NameText
    {
        get => _nameText;
        set
        {
            if (string.Equals(_nameText, value, StringComparison.Ordinal)) return;
            _nameText = value ?? string.Empty;
            Notify(nameof(NameText));
            NotifyDerived();
        }
    }

    public string EmailText
    {
        get => _emailText;
        set
        {
            if (string.Equals(_emailText, value, StringComparison.Ordinal)) return;
            _emailText = value ?? string.Empty;
            Notify(nameof(EmailText));
            NotifyDerived();
        }
    }

    public string? NamePlaceholder =>
        _snapshot?.Name.HasRepositoryOverride == false ? _snapshot.Name.EffectiveValue : null;

    public string? EmailPlaceholder =>
        _snapshot?.Email.HasRepositoryOverride == false ? _snapshot.Email.EffectiveValue : null;

    public string NameEffectiveDisplay => EffectiveDisplay(_snapshot?.Name);
    public string EmailEffectiveDisplay => EffectiveDisplay(_snapshot?.Email);
    public string NameSourceDisplay => SourceDisplay(_snapshot?.Name.EffectiveSource ?? GitConfigSource.NotConfigured);
    public string EmailSourceDisplay => SourceDisplay(_snapshot?.Email.EffectiveSource ?? GitConfigSource.NotConfigured);
    public string NameOriginDisplay => OriginDisplay(_snapshot?.Name.EffectiveOrigin);
    public string EmailOriginDisplay => OriginDisplay(_snapshot?.Email.EffectiveOrigin);
    public string NameOverrideDisplay => OverrideDisplay(_snapshot?.Name);
    public string EmailOverrideDisplay => OverrideDisplay(_snapshot?.Email);

    public bool IsNameDirty => IsFieldDirty(_nameText, _snapshot?.Name);
    public bool IsEmailDirty => IsFieldDirty(_emailText, _snapshot?.Email);
    public bool IsDirty => IsNameDirty || IsEmailDirty;

    public string? NameValidationMessage => ValidateName(_nameText);
    public string? EmailValidationMessage => ValidateEmail(_emailText);
    public bool HasNameValidationError => NameValidationMessage is not null;
    public bool HasEmailValidationError => EmailValidationMessage is not null;

    public bool CanSave =>
        HasRepository
        && !_isStale
        && !_isBusy
        && IsDirty
        && !HasNameValidationError
        && !HasEmailValidationError;

    public bool CanRemoveNameOverride =>
        HasRepository && !_isStale && !_isBusy && _snapshot!.Name.HasRepositoryOverride;

    public bool CanRemoveEmailOverride =>
        HasRepository && !_isStale && !_isBusy && _snapshot!.Email.HasRepositoryOverride;

    public string StaleWarning =>
        "Repository changed. These unsaved identity edits belong to the previous repository. Reload to edit the current repository.";

    public bool HasHigherPriorityWarning =>
        IsHigherPriority(_snapshot?.Name.EffectiveSource)
        || IsHigherPriority(_snapshot?.Email.EffectiveSource);

    public string HigherPriorityWarning
    {
        get
        {
            var hasCommand = _snapshot?.Name.EffectiveSource == GitConfigSource.Command
                             || _snapshot?.Email.EffectiveSource == GitConfigSource.Command;
            var hasWorktree = _snapshot?.Name.EffectiveSource == GitConfigSource.Worktree
                              || _snapshot?.Email.EffectiveSource == GitConfigSource.Worktree;
            if (hasCommand && hasWorktree)
                return "Command/environment and worktree Git configuration currently override one or more repository values. Saving a repository override will not change those effective values.";
            if (hasCommand)
                return "A command/environment Git configuration value currently overrides this repository value. Saving a repository override will not change the effective value.";
            if (hasWorktree)
                return "A worktree Git configuration value currently overrides this repository value. Saving a repository override will not change the effective value for this worktree.";
            return string.Empty;
        }
    }

    public string? RepositoryReloadNotice => _repositoryReloadNotice;
    public bool HasRepositoryReloadNotice => !string.IsNullOrWhiteSpace(_repositoryReloadNotice);

    internal async Task RefreshAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        var generation = Interlocked.Increment(ref _refreshGeneration);
        var repository = _repositoryAccessor();

        if (repository is null)
        {
            if (_snapshot is not null && IsDirty)
            {
                MarkStale();
                return;
            }

            ClearForNoRepository();
            return;
        }

        var repositoryKey = RepositoryKey(repository);
        if (_loadedRepositoryKey is not null && !RepositoryKeysEqual(_loadedRepositoryKey, repositoryKey))
        {
            if (IsDirty)
            {
                MarkStale();
                return;
            }
        }
        else if (_isStale && _loadedRepositoryKey is not null && RepositoryKeysEqual(_loadedRepositoryKey, repositoryKey))
        {
            _isStale = false;
            Notify(nameof(IsStale));
            NotifyDerived();
            if (!force) return;
        }

        if (!force
            && _snapshot is not null
            && RepositoryKeysEqual(_loadedRepositoryKey, repositoryKey)
            && !_isStale)
            return;

        SetBusy(true);
        try
        {
            var snapshot = await _service.ReadAsync(repository, cancellationToken);
            if (generation != Volatile.Read(ref _refreshGeneration)) return;
            if (!CurrentRepositoryMatches(repository)) return;

            var changed = _loadedRepositoryKey is not null
                          && !RepositoryKeysEqual(_loadedRepositoryKey, repositoryKey);
            ApplySnapshot(repository, snapshot);
            _repositoryReloadNotice = changed ? "Repository changed. Identity settings were reloaded." : null;
            Notify(nameof(RepositoryReloadNotice));
            Notify(nameof(HasRepositoryReloadNotice));
        }
        finally
        {
            if (generation == Volatile.Read(ref _refreshGeneration))
                SetBusy(false);
        }
    }

    internal async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSave) return;
        var repository = RequireCurrentRepository();
        var desiredName = _nameText;
        var desiredEmail = _emailText;
        var updateName = IsNameDirty;
        var updateEmail = IsEmailDirty;
        var edit = new RepositoryIdentityEdit(updateName, desiredName, updateEmail, desiredEmail);

        SetBusy(true);
        try
        {
            await _service.SaveAsync(repository, edit, cancellationToken);
            var snapshot = await _service.ReadAsync(repository, cancellationToken);
            if (!CurrentRepositoryMatches(repository))
            {
                MarkStale();
                return;
            }
            ApplySnapshot(repository, snapshot);
        }
        catch
        {
            await RecoverAfterFailureAsync(repository, updateName, desiredName, updateEmail, desiredEmail, cancellationToken);
            throw;
        }
        finally
        {
            SetBusy(false);
        }
    }

    internal Task RemoveNameOverrideAsync(CancellationToken cancellationToken = default) =>
        RemoveOverrideAsync(RepositoryIdentityField.Name, cancellationToken);

    internal Task RemoveEmailOverrideAsync(CancellationToken cancellationToken = default) =>
        RemoveOverrideAsync(RepositoryIdentityField.Email, cancellationToken);

    internal async Task DiscardChangesAndReloadAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _refreshGeneration);
        _snapshot = null;
        _loadedRepositoryKey = null;
        _nameText = string.Empty;
        _emailText = string.Empty;
        _isStale = false;
        NotifyAll();
        await RefreshAsync(force: true, cancellationToken);
    }

    private async Task RemoveOverrideAsync(
        RepositoryIdentityField field,
        CancellationToken cancellationToken)
    {
        var repository = RequireCurrentRepository();
        var preserveName = field != RepositoryIdentityField.Name && IsNameDirty;
        var preserveEmail = field != RepositoryIdentityField.Email && IsEmailDirty;
        var desiredName = _nameText;
        var desiredEmail = _emailText;

        SetBusy(true);
        try
        {
            await _service.RemoveOverrideAsync(repository, field, cancellationToken);
            var snapshot = await _service.ReadAsync(repository, cancellationToken);
            if (!CurrentRepositoryMatches(repository))
            {
                MarkStale();
                return;
            }

            ApplySnapshot(repository, snapshot);
            if (preserveName) _nameText = desiredName;
            if (preserveEmail) _emailText = desiredEmail;
            NotifyAll();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RecoverAfterFailureAsync(
        Repository repository,
        bool updateName,
        string desiredName,
        bool updateEmail,
        string desiredEmail,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _service.ReadAsync(repository, cancellationToken);
            if (!CurrentRepositoryMatches(repository))
            {
                MarkStale();
                return;
            }

            ApplySnapshot(repository, snapshot);
            if (updateName && !TargetMatches(desiredName, snapshot.Name))
                _nameText = desiredName;
            if (updateEmail && !TargetMatches(desiredEmail, snapshot.Email))
                _emailText = desiredEmail;
            NotifyAll();
        }
        catch
        {
        }
    }

    private Repository RequireCurrentRepository()
    {
        var repository = _repositoryAccessor();
        if (repository is null
            || _loadedRepositoryKey is null
            || !RepositoryKeysEqual(_loadedRepositoryKey, RepositoryKey(repository)))
        {
            MarkStale();
            throw new InvalidOperationException("Repository changed. Reload Identity settings before saving.");
        }

        return repository;
    }

    private bool CurrentRepositoryMatches(Repository repository)
    {
        var current = _repositoryAccessor();
        return current is not null
               && RepositoryKeysEqual(RepositoryKey(repository), RepositoryKey(current));
    }

    private void ApplySnapshot(Repository repository, RepositoryIdentitySnapshot snapshot)
    {
        _snapshot = snapshot;
        _loadedRepositoryKey = RepositoryKey(repository);
        _repositoryPath = repository.WorkingDirectory;
        _repositoryName = RepositoryNameResolver.Resolve(repository.WorkingDirectory) ?? repository.WorkingDirectory;
        _nameText = snapshot.Name.RepositoryValue ?? string.Empty;
        _emailText = snapshot.Email.RepositoryValue ?? string.Empty;
        _isStale = false;
        _repositoryReloadNotice = null;
        NotifyAll();
    }

    private void ClearForNoRepository()
    {
        _snapshot = null;
        _loadedRepositoryKey = null;
        _repositoryName = string.Empty;
        _repositoryPath = string.Empty;
        _nameText = string.Empty;
        _emailText = string.Empty;
        _isStale = false;
        _repositoryReloadNotice = null;
        SetBusy(false);
        NotifyAll();
    }

    private void MarkStale()
    {
        Interlocked.Increment(ref _refreshGeneration);
        _isStale = true;
        SetBusy(false);
        Notify(nameof(IsStale));
        NotifyDerived();
    }

    private void SetBusy(bool value)
    {
        if (_isBusy == value) return;
        _isBusy = value;
        Notify(nameof(IsBusy));
        NotifyDerived();
    }

    private void NotifyAll()
    {
        Notify(nameof(HasRepository));
        Notify(nameof(HasNoRepository));
        Notify(nameof(IsBusy));
        Notify(nameof(IsStale));
        Notify(nameof(CanEdit));
        Notify(nameof(RepositoryName));
        Notify(nameof(RepositoryPath));
        Notify(nameof(NameText));
        Notify(nameof(EmailText));
        NotifyDerived();
    }

    private void NotifyDerived()
    {
        Notify(nameof(NamePlaceholder));
        Notify(nameof(EmailPlaceholder));
        Notify(nameof(NameEffectiveDisplay));
        Notify(nameof(EmailEffectiveDisplay));
        Notify(nameof(NameSourceDisplay));
        Notify(nameof(EmailSourceDisplay));
        Notify(nameof(NameOriginDisplay));
        Notify(nameof(EmailOriginDisplay));
        Notify(nameof(NameOverrideDisplay));
        Notify(nameof(EmailOverrideDisplay));
        Notify(nameof(IsNameDirty));
        Notify(nameof(IsEmailDirty));
        Notify(nameof(IsDirty));
        Notify(nameof(NameValidationMessage));
        Notify(nameof(EmailValidationMessage));
        Notify(nameof(HasNameValidationError));
        Notify(nameof(HasEmailValidationError));
        Notify(nameof(CanSave));
        Notify(nameof(CanRemoveNameOverride));
        Notify(nameof(CanRemoveEmailOverride));
        Notify(nameof(HasHigherPriorityWarning));
        Notify(nameof(HigherPriorityWarning));
        Notify(nameof(RepositoryReloadNotice));
        Notify(nameof(HasRepositoryReloadNotice));
        Notify(nameof(CanEdit));
    }

    private static bool IsFieldDirty(string text, GitIdentityValue? baseline)
    {
        if (baseline is null) return false;
        var target = NormalizeOptional(text);
        var targetPresent = target is not null;
        var baselineValue = NormalizeOptional(baseline.RepositoryValue);
        if (targetPresent != baseline.HasRepositoryOverride) return true;
        return targetPresent && !string.Equals(target, baselineValue, StringComparison.Ordinal);
    }

    private static bool TargetMatches(string text, GitIdentityValue current)
    {
        var target = NormalizeOptional(text);
        var targetPresent = target is not null;
        if (targetPresent != current.HasRepositoryOverride) return false;
        return !targetPresent
               || string.Equals(target, NormalizeOptional(current.RepositoryValue), StringComparison.Ordinal);
    }

    private static string? ValidateName(string text)
    {
        var value = NormalizeOptional(text);
        if (value is null) return null;
        return value.IndexOfAny(['\r', '\n', '\0']) >= 0
            ? "Name must be a single-line value."
            : null;
    }

    private static string? ValidateEmail(string text)
    {
        var value = NormalizeOptional(text);
        if (value is null) return null;
        if (value.IndexOfAny(['\r', '\n', '\0']) >= 0 || value.Any(char.IsWhiteSpace))
            return "Enter an email without whitespace or line breaks.";

        var at = value.IndexOf('@');
        return at <= 0 || at != value.LastIndexOf('@') || at == value.Length - 1
            ? "Enter an email with non-empty parts before and after @."
            : null;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static string EffectiveDisplay(GitIdentityValue? value) =>
        string.IsNullOrEmpty(value?.EffectiveValue) ? "Not configured" : value.EffectiveValue;

    private static string OriginDisplay(string? origin) =>
        string.IsNullOrWhiteSpace(origin) ? "—" : origin;

    private static string OverrideDisplay(GitIdentityValue? value) =>
        value?.HasRepositoryOverride == true ? "Repository override" : "No repository override";

    private static string SourceDisplay(GitConfigSource source) => source switch
    {
        GitConfigSource.Command => "Command / environment config override",
        GitConfigSource.Worktree => "This worktree",
        GitConfigSource.Repository => "This repository",
        GitConfigSource.Global => "Global",
        GitConfigSource.System => "System",
        _ => "Not configured"
    };

    private static bool IsHigherPriority(GitConfigSource? source) =>
        source is GitConfigSource.Command or GitConfigSource.Worktree;

    private static string RepositoryKey(Repository repository) =>
        Path.GetFullPath(repository.WorkingDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool RepositoryKeysEqual(string? left, string? right) =>
        string.Equals(
            left,
            right,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
