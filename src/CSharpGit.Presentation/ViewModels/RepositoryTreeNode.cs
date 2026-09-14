using System.Collections.ObjectModel;
using System.ComponentModel;
using CSharpGit.Domain;
using Microsoft.UI.Xaml;
using Windows.UI.Text;

namespace CSharpGit.Presentation.ViewModels;

public sealed class RepositoryTreeNode : INotifyPropertyChanged
{
    private static readonly Dictionary<string, bool> ExpansionState = new(StringComparer.Ordinal);

    private readonly string? _expansionKey;
    private string _baseName;
    private string _name;
    private string? _referenceName;
    private object? _value;
    private bool _isCurrent;
    private bool _isExpanded;
    private string? _associatedWorktreePath;

    internal RepositoryTreeNode(RepositoryTreeDescriptor descriptor)
    {
        Key = descriptor.Key;
        Kind = descriptor.Kind;
        _baseName = descriptor.Name;
        _name = descriptor.Name;
        _referenceName = descriptor.ReferenceName;
        _value = descriptor.Value;
        _isCurrent = descriptor.IsCurrent;
        _associatedWorktreePath = descriptor.AssociatedWorktreePath;
        _expansionKey = SupportsExpansionState(Kind) ? Key : null;
        _isExpanded = _expansionKey is not null && ExpansionState.TryGetValue(_expansionKey, out var savedExpansion)
            ? savedExpansion
            : descriptor.DefaultExpanded;
        Children.CollectionChanged += (_, _) => Notify(nameof(HasChildren));
        RefreshName();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; }
    public RepositoryTreeNodeKind Kind { get; }
    public string Name => _name;
    public string? ReferenceName => _referenceName;
    public object? Value => _value;
    public bool IsCurrent => _isCurrent;
    public string? AssociatedWorktreePath => _associatedWorktreePath;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            if (_expansionKey is not null) ExpansionState[_expansionKey] = value;
            Notify(nameof(IsExpanded));
        }
    }

    public ObservableCollection<RepositoryTreeNode> Children { get; } = [];
    public bool HasChildren => Children.Count > 0;
    public IReadOnlyList<RepositoryTreeGuideSegmentKind> HierarchyGuideSegments { get; private set; } =
        Array.Empty<RepositoryTreeGuideSegmentKind>();
    public string DisplayName => Name;
    public FontWeight NameFontWeight => IsCurrent ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal;
    public Visibility CurrentBranchAccentVisibility => IsCurrent ? Visibility.Visible : Visibility.Collapsed;
    public string? IconGlyph => Kind switch
    {
        RepositoryTreeNodeKind.Group when Name == "Worktrees" => "\uE8B7",
        RepositoryTreeNodeKind.Group when Name == "Branches" => "\uE8F0",
        RepositoryTreeNodeKind.Group when Name == "Remotes" => "\uE8AF",
        RepositoryTreeNodeKind.Group when Name == "Tags" => "\uE8EC",
        RepositoryTreeNodeKind.Group when Name == "Stashes" => "\uE8F1",
        RepositoryTreeNodeKind.Worktree => "\uE8B7",
        _ => null
    };
    public Visibility IconVisibility => IconGlyph is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility CurrentLocalBranchIconVisibility =>
        Kind == RepositoryTreeNodeKind.LocalBranch && IsCurrent
            ? Visibility.Visible
            : Visibility.Collapsed;
    public Visibility LocalDefaultBranchIconVisibility =>
        Kind == RepositoryTreeNodeKind.LocalBranch && !IsCurrent && Value is GitBranch { IsDefault: true }
            ? Visibility.Visible
            : Visibility.Collapsed;
    public Visibility RemoteDefaultBranchIconVisibility =>
        Kind == RepositoryTreeNodeKind.RemoteBranch && Value is GitBranch { IsDefault: true }
            ? Visibility.Visible
            : Visibility.Collapsed;

    internal void UpdateFrom(RepositoryTreeDescriptor descriptor)
    {
        if (!string.Equals(Key, descriptor.Key, StringComparison.Ordinal) || Kind != descriptor.Kind)
            throw new InvalidOperationException("Repository tree node identity changed during reconciliation.");

        var oldName = Name;
        var oldReferenceName = ReferenceName;
        var oldValue = Value;
        var oldIsCurrent = IsCurrent;
        var oldAssociatedWorktreePath = AssociatedWorktreePath;
        var oldLocalDefault = IsLocalDefaultBranchVisible();
        var oldRemoteDefault = IsRemoteDefaultBranchVisible();

        _baseName = descriptor.Name;
        _referenceName = descriptor.ReferenceName;
        _value = descriptor.Value;
        _isCurrent = descriptor.IsCurrent;
        _associatedWorktreePath = descriptor.AssociatedWorktreePath;
        RefreshName();

        if (!string.Equals(oldName, Name, StringComparison.Ordinal))
        {
            Notify(nameof(Name));
            Notify(nameof(DisplayName));
        }
        if (!string.Equals(oldReferenceName, ReferenceName, StringComparison.Ordinal)) Notify(nameof(ReferenceName));
        if (!Equals(oldValue, Value)) Notify(nameof(Value));
        if (oldIsCurrent != IsCurrent)
        {
            Notify(nameof(IsCurrent));
            Notify(nameof(NameFontWeight));
            Notify(nameof(CurrentBranchAccentVisibility));
            Notify(nameof(CurrentLocalBranchIconVisibility));
        }
        if (!string.Equals(oldAssociatedWorktreePath, AssociatedWorktreePath, StringComparison.Ordinal))
            Notify(nameof(AssociatedWorktreePath));
        if (oldLocalDefault != IsLocalDefaultBranchVisible()) Notify(nameof(LocalDefaultBranchIconVisibility));
        if (oldRemoteDefault != IsRemoteDefaultBranchVisible()) Notify(nameof(RemoteDefaultBranchIconVisibility));
    }

    internal void SetAssociatedWorktreePath(string? path)
    {
        if (string.Equals(_associatedWorktreePath, path, StringComparison.Ordinal)) return;
        var oldName = Name;
        _associatedWorktreePath = path;
        RefreshName();
        Notify(nameof(AssociatedWorktreePath));
        if (string.Equals(oldName, Name, StringComparison.Ordinal)) return;
        Notify(nameof(Name));
        Notify(nameof(DisplayName));
    }

    internal static void ResetExpansionState() => ExpansionState.Clear();

    internal void SetHierarchyGuideSegments(IReadOnlyList<RepositoryTreeGuideSegmentKind> segments)
    {
        IReadOnlyList<RepositoryTreeGuideSegmentKind> effectiveSegments = segments;
        if (IsCurrent && Kind is RepositoryTreeNodeKind.LocalBranch or RepositoryTreeNodeKind.Worktree && segments.Count > 0)
        {
            var adjustedSegments = segments.ToArray();
            adjustedSegments[^1] = RepositoryTreeGuideSegmentKind.Empty;
            effectiveSegments = adjustedSegments;
        }

        if (HierarchyGuideSegments.SequenceEqual(effectiveSegments)) return;
        HierarchyGuideSegments = effectiveSegments;
        Notify(nameof(HierarchyGuideSegments));
    }

    private static bool SupportsExpansionState(RepositoryTreeNodeKind kind) =>
        kind is RepositoryTreeNodeKind.Group or RepositoryTreeNodeKind.Remote or RepositoryTreeNodeKind.BranchFolder;

    private void RefreshName() =>
        _name = Kind == RepositoryTreeNodeKind.LocalBranch && !IsCurrent && !string.IsNullOrWhiteSpace(_associatedWorktreePath)
            ? $"{_baseName}  [worktree]"
            : _baseName;

    private bool IsLocalDefaultBranchVisible() =>
        Kind == RepositoryTreeNodeKind.LocalBranch && !IsCurrent && Value is GitBranch { IsDefault: true };

    private bool IsRemoteDefaultBranchVisible() =>
        Kind == RepositoryTreeNodeKind.RemoteBranch && Value is GitBranch { IsDefault: true };

    private void Notify(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record CommitFileRow(string Status, ChangedFile File)
{
    public string Path => File.Path;
    public string AddedDisplay => File.AddedLines is { } value ? $"+{value}" : string.Empty;
    public string RemovedDisplay => File.RemovedLines is { } value ? $"-{value}" : string.Empty;
}
