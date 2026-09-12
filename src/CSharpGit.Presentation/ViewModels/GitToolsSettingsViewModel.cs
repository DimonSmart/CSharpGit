using System.ComponentModel;
using System.Runtime.CompilerServices;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

public sealed record GitToolScopeOption(GitToolWriteScope Scope, string Label);

public sealed class GitToolsSettingsViewModel
{
    private readonly IGitToolsService _service;

    public GitToolsSettingsViewModel(IGitToolsService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        Editor = new GitToolSectionViewModel(GitToolKind.Editor, "Editor");
        Diff = new GitToolSectionViewModel(GitToolKind.Diff, "Diff Tool");
        Merge = new GitToolSectionViewModel(GitToolKind.Merge, "Merge Tool");
    }

    public GitToolSectionViewModel Editor { get; }
    public GitToolSectionViewModel Diff { get; }
    public GitToolSectionViewModel Merge { get; }

    public async Task RefreshAsync(Repository? repository, CancellationToken cancellationToken = default)
    {
        var editor = await _service.ReadAsync(repository, GitToolKind.Editor, cancellationToken);
        var diff = await _service.ReadAsync(repository, GitToolKind.Diff, cancellationToken);
        var merge = await _service.ReadAsync(repository, GitToolKind.Merge, cancellationToken);
        Editor.Load(editor, repository is not null);
        Diff.Load(diff, repository is not null);
        Merge.Load(merge, repository is not null);
    }

    public async Task SaveAsync(Repository? repository, GitToolSectionViewModel section, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(section);
        if (section.SelectedScope is null) throw new InvalidOperationException("Select a Git configuration scope.");
        var edit = section.CreateEdit();
        await _service.SaveAsync(repository, edit, cancellationToken);
        var refreshed = await _service.ReadAsync(repository, section.Kind, cancellationToken);
        section.Load(refreshed, repository is not null, edit.Scope);
    }

    public async Task RemoveOverrideAsync(Repository? repository, GitToolSectionViewModel section, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(section);
        if (section.SelectedScope is null) throw new InvalidOperationException("Select a Git configuration scope.");
        var scope = section.SelectedScope.Scope;
        await _service.RemoveOverrideAsync(repository, section.Kind, scope, cancellationToken);
        var refreshed = await _service.ReadAsync(repository, section.Kind, cancellationToken);
        section.Load(refreshed, repository is not null, scope);
    }

    public async Task TestAsync(Repository? repository, GitToolSectionViewModel section, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(section);
        if (section.IsDirty) throw new InvalidOperationException("Save Git Tools changes before running the test.");
        await _service.TestAsync(repository, section.Kind, cancellationToken);
    }
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
            if (!_loading && value is { IsCustom: false }) Value = value.Value;
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
            Notify(nameof(IsDirty));
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
            Notify(nameof(IsDirty));
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
            Notify(nameof(IsDirty));
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
            Notify(nameof(IsDirty));
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
            Notify(nameof(IsDirty));
        }
    }

    public bool IsDirty =>
        !string.Equals(_initialValue, Value, StringComparison.Ordinal)
        || _pathDirty || _commandDirty || _trustDirty || _keepBackupDirty;

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
    public string ValidationDisplay => _snapshot is null || _snapshot.Validation.Count == 0
        ? "Configuration looks usable."
        : string.Join(Environment.NewLine, _snapshot.Validation.Select(message => $"{message.Severity}: {message.Message}"));

    public string OverrideNotice
    {
        get
        {
            if (_snapshot is null || SelectedScope is null) return string.Empty;
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

    public bool CanRemoveOverride => SelectedEditableConfiguration()?.SelectionKey is not null;

    public void Load(GitToolConfigurationSnapshot snapshot, bool repositoryAvailable, GitToolWriteScope? preferredScope = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshot = snapshot;
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
        _selectedPreset = FindPreset(_value, _command);

        Notify(nameof(Value));
        Notify(nameof(Path));
        Notify(nameof(Command));
        Notify(nameof(TrustExitCode));
        Notify(nameof(KeepBackup));
        Notify(nameof(SelectedPreset));
        Notify(nameof(IsDirty));
        Notify(nameof(CanRemoveOverride));
        Notify(nameof(OverrideNotice));
    }

    private GitToolScopeConfiguration? SelectedEditableConfiguration()
    {
        if (_snapshot is null || SelectedScope is null) return null;
        return SelectedScope.Scope == GitToolWriteScope.Global ? _snapshot.Global : _snapshot.Repository;
    }

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
        return scope.SelectionValue ?? $"keepBackup={scope.KeepBackup?.ToString().ToLowerInvariant()}";
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

    private void NotifySnapshotProperties()
    {
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
