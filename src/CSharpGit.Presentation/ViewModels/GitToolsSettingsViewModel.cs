using System.ComponentModel;
using System.Runtime.CompilerServices;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

public sealed record GitToolScopeOption(GitToolWriteScope Scope, string Label);

public sealed class GitToolsSettingsViewModel
{
    private readonly IGitToolConfigurationService _configurationService;
    private readonly IExternalGitToolService _externalToolService;
    private readonly Func<Repository?, IDisposable?> _admission;
    private readonly Func<Repository?> _repositoryAccessor;
    private string? _loadedRepositoryKey;
    private bool _hasLoaded;
    private long _refreshGeneration;

    public GitToolsSettingsViewModel(
        IGitToolConfigurationService configurationService,
        IExternalGitToolService externalToolService,
        Func<Repository?, IDisposable?> admission,
        Func<Repository?> repositoryAccessor)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _externalToolService = externalToolService ?? throw new ArgumentNullException(nameof(externalToolService));
        _admission = admission ?? throw new ArgumentNullException(nameof(admission));
        _repositoryAccessor = repositoryAccessor ?? throw new ArgumentNullException(nameof(repositoryAccessor));
        Editor = new GitToolSectionViewModel(GitToolKind.Editor, "Editor");
        Diff = new GitToolSectionViewModel(GitToolKind.Diff, "Diff Tool");
        Merge = new GitToolSectionViewModel(GitToolKind.Merge, "Merge Tool");
    }

    public GitToolSectionViewModel Editor { get; }
    public GitToolSectionViewModel Diff { get; }
    public GitToolSectionViewModel Merge { get; }

    private bool HasPendingChanges => Editor.HasUnsavedChanges || Diff.HasUnsavedChanges || Merge.HasUnsavedChanges;

    public void RepositoryChanged()
    {
        Interlocked.Increment(ref _refreshGeneration);
        if (!_hasLoaded || RepositoryKeysEqual(_loadedRepositoryKey, RepositoryKey(_repositoryAccessor()))) return;
        if (HasPendingChanges) MarkStale();
    }

    public async Task<bool> RefreshAsync(
        Repository? repository,
        bool force = false,
        bool discard = false,
        CancellationToken cancellationToken = default)
    {
        var generation = Interlocked.Increment(ref _refreshGeneration);
        if (!CurrentRepositoryMatches(repository)) return false;
        var currentKey = RepositoryKey(repository);
        if (!discard && HasPendingChanges)
        {
            if (!_hasLoaded || !RepositoryKeysEqual(_loadedRepositoryKey, currentKey))
                MarkStale();
            // Same repository: keep edits across section navigation and reopening Settings.
            return false;
        }

        using var lease = _admission(repository);
        if (lease is null) return false;
        var editor = await _configurationService.ReadAsync(repository, GitToolKind.Editor, cancellationToken);
        var diff = await _configurationService.ReadAsync(repository, GitToolKind.Diff, cancellationToken);
        var merge = await _configurationService.ReadAsync(repository, GitToolKind.Merge, cancellationToken);
        if (generation != Volatile.Read(ref _refreshGeneration) || !CurrentRepositoryMatches(repository))
            return false;
        // The user may have started editing during the awaited Git reads.
        // Never publish a refresh over those freshly typed changes.
        if (!discard && HasPendingChanges)
            return false;

        if (discard)
        {
            Editor.ClearDrafts();
            Diff.ClearDrafts();
            Merge.ClearDrafts();
        }
        Editor.Load(editor, repository is not null);
        Diff.Load(diff, repository is not null);
        Merge.Load(merge, repository is not null);
        _loadedRepositoryKey = currentKey;
        _hasLoaded = true;
        return true;
    }

    public async Task<bool> SaveAsync(Repository? repository, GitToolSectionViewModel section, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(section);
        if (section.SelectedScope is null) throw new InvalidOperationException("Select a Git configuration scope.");
        if (!section.IsDirty) return false;
        EnsureCurrentRepository(repository);
        if (!section.CanSave) throw new InvalidOperationException(section.EditValidationMessage ?? "Reload Git Tools settings before saving.");
        var edit = section.CreateEdit();
        using var lease = _admission(repository);
        if (lease is null) return false;
        EnsureCurrentRepository(repository); // Recheck immediately before the first write.
        try
        {
            await _configurationService.SaveAsync(repository, edit, cancellationToken);
            var refreshed = await _configurationService.ReadAsync(repository, section.Kind, cancellationToken);
            if (!CurrentRepositoryMatches(repository)) { MarkStale(); return false; }
            section.ForgetCurrentDraft();
            section.Load(refreshed, repository is not null, edit.Scope);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            try
            {
                var refreshed = await _configurationService.ReadAsync(repository, section.Kind, cancellationToken);
                if (CurrentRepositoryMatches(repository))
                {
                    section.Load(refreshed, repository is not null, edit.Scope);
                    section.RestorePendingEdit(edit);
                }
                else MarkStale();
            }
            catch { /* The original failure is more actionable; never discard the edit buffer. */ }
            throw new InvalidOperationException(
                "Git Tools save failed. Some values may already have been written. Current Git configuration was re-read where possible; unapplied edits remain available. " + exception.Message,
                exception);
        }
    }

    public async Task<bool> RemoveOverrideAsync(Repository? repository, GitToolSectionViewModel section, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(section);
        if (section.SelectedScope is null) throw new InvalidOperationException("Select a Git configuration scope.");
        EnsureCurrentRepository(repository);
        if (section.IsDirty) throw new InvalidOperationException("Save or discard changes in this scope before removing its override.");
        if (!section.CanRemoveOverride) return false;
        var scope = section.SelectedScope.Scope;
        using var lease = _admission(repository);
        if (lease is null) return false;
        EnsureCurrentRepository(repository);
        await _configurationService.RemoveOverrideAsync(repository, section.Kind, scope, cancellationToken);
        var refreshed = await _configurationService.ReadAsync(repository, section.Kind, cancellationToken);
        if (!CurrentRepositoryMatches(repository)) { MarkStale(); return false; }
        section.Load(refreshed, repository is not null, scope);
        return true;
    }

    public async Task<bool> TestAsync(Repository? repository, GitToolSectionViewModel section, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(section);
        EnsureCurrentRepository(repository);
        if (section.IsDirty) throw new InvalidOperationException("Save Git Tools changes before running the test.");
        using var lease = _admission(repository);
        if (lease is null) return false;
        EnsureCurrentRepository(repository);
        await _externalToolService.TestAsync(repository, section.Kind, cancellationToken);
        return CurrentRepositoryMatches(repository);
    }

    private void EnsureCurrentRepository(Repository? repository)
    {
        if (!_hasLoaded || !CurrentRepositoryMatches(repository)
            || !RepositoryKeysEqual(_loadedRepositoryKey, RepositoryKey(repository))
            || Editor.IsStale || Diff.IsStale || Merge.IsStale)
        {
            MarkStale();
            throw new InvalidOperationException("Repository changed. Discard and reload Git Tools settings before saving or testing.");
        }
    }

    private bool CurrentRepositoryMatches(Repository? repository) =>
        RepositoryKeysEqual(RepositoryKey(repository), RepositoryKey(_repositoryAccessor()));

    private void MarkStale()
    {
        Editor.SetStale(true);
        Diff.SetStale(true);
        Merge.SetStale(true);
    }

    private static string? RepositoryKey(Repository? repository) =>
        repository is null ? null : Path.GetFullPath(repository.WorkingDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool RepositoryKeysEqual(string? left, string? right) =>
        string.Equals(left, right,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}

public sealed class GitToolSectionViewModel : INotifyPropertyChanged
{
    private GitToolConfigurationSnapshot? _snapshot;
    private GitToolScopeOption? _selectedScope;
    private GitToolPreset? _selectedPreset;
    private string _value = string.Empty;
    private string _path = string.Empty;
    private string _command = string.Empty;
    private bool? _trustExitCode;
    private bool? _keepBackup;
    private bool _loading;
    private bool _pathDirty;
    private bool _commandDirty;
    private bool _trustDirty;
    private bool _keepBackupDirty;
    private string _initialValue = string.Empty;
    private bool _isStale;
    private readonly Dictionary<GitToolWriteScope, GitToolDraft> _drafts = new();

    private sealed record GitToolDraft(
        string Value, string Path, string Command, bool? TrustExitCode, bool? KeepBackup,
        string InitialValue, bool PathDirty, bool CommandDirty, bool TrustDirty, bool KeepBackupDirty)
    {
        public bool IsDirty => !string.Equals(InitialValue, Value, StringComparison.Ordinal)
                               || PathDirty || CommandDirty || TrustDirty || KeepBackupDirty;
    }

    public GitToolSectionViewModel(GitToolKind kind, string title)
    {
        Kind = kind;
        Title = title;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public GitToolKind Kind { get; }
    public string Title { get; }
    public IReadOnlyList<GitToolScopeOption> Scopes { get; private set; } = [];
    public IReadOnlyList<GitToolPreset> Presets { get; private set; } = [];

    public GitToolScopeOption? SelectedScope
    {
        get => _selectedScope;
        set
        {
            if (Equals(_selectedScope, value)) return;
            if (!_loading) StoreCurrentDraft();
            _selectedScope = value;
            Notify();
            if (!_loading) LoadEditableScope();
        }
    }

    public GitToolPreset? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (Equals(_selectedPreset, value)) return;
            _selectedPreset = value;
            Notify();
            if (!_loading && value is { IsCustom: false })
            {
                var changedTool = !string.Equals(Value, value.Value, StringComparison.OrdinalIgnoreCase);
                Value = value.Value;
                if (changedTool && !IsEditor)
                {
                    Path = string.Empty;
                    Command = string.Empty;
                }
            }
        }
    }

    public string Value
    {
        get => _value;
        set
        {
            value ??= string.Empty;
            if (string.Equals(_value, value, StringComparison.Ordinal)) return;
            _value = value;
            Notify();
            NotifyEditState();
            if (!_loading) RefreshPresetSelection();
        }
    }

    public string Path
    {
        get => _path;
        set
        {
            value ??= string.Empty;
            if (string.Equals(_path, value, StringComparison.Ordinal)) return;
            _path = value;
            if (!_loading) _pathDirty = true;
            Notify();
            NotifyEditState();
        }
    }

    public string Command
    {
        get => _command;
        set
        {
            value ??= string.Empty;
            if (string.Equals(_command, value, StringComparison.Ordinal)) return;
            _command = value;
            if (!_loading) _commandDirty = true;
            Notify();
            NotifyEditState();
            if (!_loading && Kind is GitToolKind.Diff or GitToolKind.Merge && !string.IsNullOrWhiteSpace(value)) SelectCustomPreset();
        }
    }

    public bool? TrustExitCode
    {
        get => _trustExitCode;
        set
        {
            if (_trustExitCode == value) return;
            _trustExitCode = value;
            if (!_loading) _trustDirty = true;
            Notify();
            NotifyEditState();
        }
    }

    public bool? KeepBackup
    {
        get => _keepBackup;
        set
        {
            if (_keepBackup == value) return;
            _keepBackup = value;
            if (!_loading) _keepBackupDirty = true;
            Notify();
            NotifyEditState();
        }
    }

    public bool IsDirty =>
        !string.Equals(_initialValue, Value, StringComparison.Ordinal)
        || _pathDirty || _commandDirty || _trustDirty || _keepBackupDirty;

    public bool HasUnsavedChanges => IsDirty || _drafts.Values.Any(draft => draft.IsDirty);
    public bool IsStale => _isStale;
    public string StaleNotice => "Repository changed. These unsaved Git Tools edits belong to another repository. Discard and reload to edit the current repository.";
    public bool CanSave => IsDirty && !_isStale && EditValidationMessage is null;
    public string? EditValidationMessage
    {
        get
        {
            if (!IsDirty) return null;
            if (string.IsNullOrWhiteSpace(Value)) return IsEditor ? "Enter a Git editor command." : "Select a Git tool.";
            if (Value.IndexOfAny(['\0', '\r', '\n']) >= 0) return "Value cannot contain line breaks or NUL.";
            if (!IsEditor && Value.Any(char.IsWhiteSpace)) return "Tool name cannot contain whitespace.";
            if (!IsEditor && !string.IsNullOrWhiteSpace(Path) && !File.Exists(Path) && !Directory.Exists(Path))
                return "Executable not found. Browse to an installed tool.";
            if (!IsEditor && !string.IsNullOrWhiteSpace(Command))
            {
                var required = Kind == GitToolKind.Diff ? new[] { "$LOCAL", "$REMOTE" }
                    : new[] { "$LOCAL", "$REMOTE", "$MERGED" };
                foreach (var variable in required)
                    if (!Command.Contains(variable, StringComparison.Ordinal))
                        return $"Custom command must contain {variable}.";
            }
            return null;
        }
    }
    public bool IsEditor => Kind == GitToolKind.Editor;
    public bool IsMerge => Kind == GitToolKind.Merge;

    public string EffectiveDisplay => string.IsNullOrWhiteSpace(_snapshot?.EffectiveValue) ? "Not configured" : DisplayPreset(_snapshot.EffectiveValue!, _snapshot.EffectiveCommand);
    public string SourceDisplay => _snapshot is null ? "—" : SourceLabel(_snapshot.EffectiveSource);
    public string OriginDisplay => string.IsNullOrWhiteSpace(_snapshot?.EffectiveOrigin) ? "—" : _snapshot.EffectiveOrigin!;
    public string GlobalDisplay => ScopeDisplay(_snapshot?.Global);
    public string RepositoryDisplay => ScopeDisplay(_snapshot?.Repository);
    public string WorktreeDisplay => ScopeDisplay(_snapshot?.Worktree);
    public string SystemDisplay => ScopeDisplay(_snapshot?.System);
    public string ResolvedExecutableDisplay => string.IsNullOrWhiteSpace(_snapshot?.ResolvedExecutable) ? "Not resolved" : _snapshot.ResolvedExecutable!;
    public string EffectivePathDisplay => string.IsNullOrWhiteSpace(_snapshot?.EffectivePath) ? "Git built-in / PATH" : _snapshot.EffectivePath!;
    public string EffectiveCommandDisplay => string.IsNullOrWhiteSpace(_snapshot?.EffectiveCommand) ? "Git built-in" : _snapshot.EffectiveCommand!;
    public string ValidationDisplay => EditValidationMessage
        ?? (_snapshot is null || _snapshot.Validation.Count == 0
            ? "Configuration looks usable."
            : string.Join(Environment.NewLine, _snapshot.Validation.Select(message => $"{message.Severity}: {message.Message}")));

    public string OverrideNotice
    {
        get
        {
            if (_snapshot is null || SelectedScope is null) return string.Empty;
            if (_snapshot.EffectiveSource == GitToolConfigurationSource.Environment)
                return "An environment or command override controls the effective value. Saving this scope will not replace it.";
            if (SelectedScope.Scope == GitToolWriteScope.Global)
            {
                if (_snapshot.Worktree.IsConfigured) return "A worktree value has higher priority here. Saving Global will not change the effective value for this worktree.";
                if (_snapshot.Repository.IsConfigured) return "This repository overrides Global. Saving Global will not change the effective value for this repository.";
            }
            if (SelectedScope.Scope == GitToolWriteScope.Repository && _snapshot.Worktree.IsConfigured)
                return "This worktree overrides repository-local configuration. Saving This repository may not change the effective value for this worktree.";
            return string.Empty;
        }
    }

    public bool CanRemoveOverride =>
        !_isStale && SelectedEditableConfiguration()?.SelectionKey is { } key && IsOwnSelectionKey(Kind, key);

    public void Load(GitToolConfigurationSnapshot snapshot, bool repositoryAvailable, GitToolWriteScope? preferredScope = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshot = snapshot;
        _isStale = false;
        _loading = true;
        try
        {
            Presets = snapshot.Presets;
            Scopes = repositoryAvailable
                ? [new(GitToolWriteScope.Global, "Global"), new(GitToolWriteScope.Repository, "This repository")]
                : [new(GitToolWriteScope.Global, "Global")];
            Notify(nameof(Presets));
            Notify(nameof(Scopes));

            var scope = preferredScope
                ?? _selectedScope?.Scope
                ?? (repositoryAvailable && snapshot.Repository.IsConfigured ? GitToolWriteScope.Repository : GitToolWriteScope.Global);
            if (!repositoryAvailable && scope == GitToolWriteScope.Repository) scope = GitToolWriteScope.Global;
            _selectedScope = Scopes.First(option => option.Scope == scope);
            Notify(nameof(SelectedScope));
            LoadEditableScopeCore();
        }
        finally
        {
            _loading = false;
        }

        NotifySnapshotProperties();
    }

    public void SetStale(bool stale)
    {
        if (_isStale == stale) return;
        _isStale = stale;
        Notify(nameof(IsStale));
        Notify(nameof(CanSave));
        Notify(nameof(CanRemoveOverride));
    }

    public void ClearDrafts() => _drafts.Clear();

    public void ForgetCurrentDraft()
    {
        if (SelectedScope is not null) _drafts.Remove(SelectedScope.Scope);
    }

    public void RestorePendingEdit(GitToolEdit edit)
    {
        Value = edit.Value;
        if (edit.UpdatePath) Path = edit.Path ?? string.Empty;
        if (edit.UpdateCommand) Command = edit.Command ?? string.Empty;
        if (edit.UpdateTrustExitCode) TrustExitCode = edit.TrustExitCode;
        if (edit.UpdateKeepBackup) KeepBackup = edit.KeepBackup;
    }

    private void StoreCurrentDraft()
    {
        if (SelectedScope is null) return;
        if (IsDirty)
            _drafts[SelectedScope.Scope] = new GitToolDraft(
                _value, _path, _command, _trustExitCode, _keepBackup, _initialValue,
                _pathDirty, _commandDirty, _trustDirty, _keepBackupDirty);
        else _drafts.Remove(SelectedScope.Scope);
    }

    public GitToolEdit CreateEdit()
    {
        if (SelectedScope is null) throw new InvalidOperationException("Select a Git configuration scope.");
        if (string.IsNullOrWhiteSpace(Value))
            throw new InvalidOperationException(IsEditor ? "Enter a Git editor command." : "Enter a Git tool name.");
        return new GitToolEdit(
            Kind,
            SelectedScope.Scope,
            Value,
            Path: string.IsNullOrWhiteSpace(Path) ? null : Path,
            Command: string.IsNullOrWhiteSpace(Command) ? null : Command,
            TrustExitCode: TrustExitCode,
            KeepBackup: KeepBackup,
            UpdatePath: _pathDirty,
            UpdateCommand: _commandDirty,
            UpdateTrustExitCode: _trustDirty,
            UpdateKeepBackup: _keepBackupDirty);
    }

    private void LoadEditableScope()
    {
        _loading = true;
        try { LoadEditableScopeCore(); }
        finally { _loading = false; }
        NotifySnapshotProperties();
    }

    private void LoadEditableScopeCore()
    {
        var scope = SelectedEditableConfiguration();
        _value = scope?.SelectionValue ?? string.Empty;
        _path = scope?.Path ?? string.Empty;
        _command = Kind == GitToolKind.Editor ? string.Empty : scope?.Command ?? string.Empty;
        _trustExitCode = scope?.TrustExitCode;
        _keepBackup = scope?.KeepBackup;
        _initialValue = _value;
        _pathDirty = false;
        _commandDirty = false;
        _trustDirty = false;
        _keepBackupDirty = false;
        if (SelectedScope is { } selected && _drafts.TryGetValue(selected.Scope, out var draft))
        {
            _value = draft.Value;
            _path = draft.Path;
            _command = draft.Command;
            _trustExitCode = draft.TrustExitCode;
            _keepBackup = draft.KeepBackup;
            _initialValue = draft.InitialValue;
            _pathDirty = draft.PathDirty;
            _commandDirty = draft.CommandDirty;
            _trustDirty = draft.TrustDirty;
            _keepBackupDirty = draft.KeepBackupDirty;
        }
        _selectedPreset = FindPreset(_value, _command);

        Notify(nameof(Value));
        Notify(nameof(Path));
        Notify(nameof(Command));
        Notify(nameof(TrustExitCode));
        Notify(nameof(KeepBackup));
        Notify(nameof(SelectedPreset));
        NotifyEditState();
        Notify(nameof(CanRemoveOverride));
        Notify(nameof(OverrideNotice));
        Notify(nameof(HasUnsavedChanges));
        Notify(nameof(CanSave));
        Notify(nameof(ValidationDisplay));
    }

    private GitToolScopeConfiguration? SelectedEditableConfiguration()
    {
        if (_snapshot is null || SelectedScope is null) return null;
        return SelectedScope.Scope == GitToolWriteScope.Global ? _snapshot.Global : _snapshot.Repository;
    }

    private static bool IsOwnSelectionKey(GitToolKind kind, string key) => kind switch
    {
        GitToolKind.Editor => string.Equals(key, "core.editor", StringComparison.OrdinalIgnoreCase),
        GitToolKind.Diff => string.Equals(key, "diff.guitool", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(key, "diff.tool", StringComparison.OrdinalIgnoreCase),
        GitToolKind.Merge => string.Equals(key, "merge.guitool", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(key, "merge.tool", StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    private void RefreshPresetSelection()
    {
        var preset = FindPreset(Value, Command);
        if (Equals(_selectedPreset, preset)) return;
        _loading = true;
        try
        {
            _selectedPreset = preset;
            Notify(nameof(SelectedPreset));
        }
        finally
        {
            _loading = false;
        }
    }

    private void SelectCustomPreset()
    {
        var custom = Presets.FirstOrDefault(preset => preset.IsCustom);
        if (custom is null || Equals(_selectedPreset, custom)) return;
        _loading = true;
        try
        {
            _selectedPreset = custom;
            Notify(nameof(SelectedPreset));
        }
        finally
        {
            _loading = false;
        }
    }

    private GitToolPreset? FindPreset(string value, string command)
    {
        if (Kind is GitToolKind.Diff or GitToolKind.Merge && !string.IsNullOrWhiteSpace(command))
            return Presets.FirstOrDefault(preset => preset.IsCustom);
        var match = Presets.FirstOrDefault(preset => !preset.IsCustom && string.Equals(preset.Value, value, StringComparison.OrdinalIgnoreCase));
        return match ?? Presets.FirstOrDefault(preset => preset.IsCustom);
    }

    private string DisplayPreset(string value, string? command)
    {
        if (Kind is GitToolKind.Diff or GitToolKind.Merge && !string.IsNullOrWhiteSpace(command)) return value;
        var preset = Presets.FirstOrDefault(candidate => !candidate.IsCustom && string.Equals(candidate.Value, value, StringComparison.OrdinalIgnoreCase));
        return preset?.DisplayName ?? value;
    }

    private static string ScopeDisplay(GitToolScopeConfiguration? scope)
    {
        if (scope is null || !scope.IsConfigured) return "Not configured";
        if (!string.IsNullOrWhiteSpace(scope.SelectionValue)) return scope.SelectionValue;
        if (scope.TrustExitCode is not null) return $"trustExitCode={scope.TrustExitCode.Value.ToString().ToLowerInvariant()}";
        if (scope.KeepBackup is not null) return $"keepBackup={scope.KeepBackup.Value.ToString().ToLowerInvariant()}";
        return "Configured";
    }

    private static string SourceLabel(GitToolConfigurationSource source) => source switch
    {
        GitToolConfigurationSource.Environment => "Environment / command override",
        GitToolConfigurationSource.Worktree => "This worktree",
        GitToolConfigurationSource.Repository => "This repository",
        GitToolConfigurationSource.Global => "Global",
        GitToolConfigurationSource.System => "System",
        GitToolConfigurationSource.GitDefault => "Git default",
        _ => "Not configured"
    };

    private void NotifyEditState()
    {
        Notify(nameof(IsDirty));
        Notify(nameof(HasUnsavedChanges));
        Notify(nameof(CanSave));
        Notify(nameof(ValidationDisplay));
    }

    private void NotifySnapshotProperties()
    {
        Notify(nameof(IsStale));
        Notify(nameof(CanSave));
        Notify(nameof(HasUnsavedChanges));
        Notify(nameof(EffectiveDisplay));
        Notify(nameof(SourceDisplay));
        Notify(nameof(OriginDisplay));
        Notify(nameof(GlobalDisplay));
        Notify(nameof(RepositoryDisplay));
        Notify(nameof(WorktreeDisplay));
        Notify(nameof(SystemDisplay));
        Notify(nameof(ResolvedExecutableDisplay));
        Notify(nameof(EffectivePathDisplay));
        Notify(nameof(EffectiveCommandDisplay));
        Notify(nameof(ValidationDisplay));
        Notify(nameof(OverrideNotice));
        Notify(nameof(CanRemoveOverride));
    }

    private void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
