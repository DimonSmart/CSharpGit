using System.Collections.ObjectModel;
using System.ComponentModel;
using CSharpGit.Domain;
using Microsoft.UI.Xaml;
using Windows.UI.Text;

namespace CSharpGit.Presentation.ViewModels;

public enum RepositoryTreeNodeKind
{
    WorkingTree,
    Group,
    BranchFolder,
    LocalBranch,
    Remote,
    RemoteBranch,
    Tag,
    Stash
}

public sealed class RepositoryTreeNode : INotifyPropertyChanged
{
    private static readonly Dictionary<string, bool> ExpansionState = new(StringComparer.Ordinal);
    private static bool _localBranchBuildInitialized;
    private static string? _lastCurrentLocalBranch;

    private readonly string? _expansionKey;
    private bool _isExpanded;

    public RepositoryTreeNode(
        RepositoryTreeNodeKind kind,
        string name,
        string? referenceName = null,
        object? value = null,
        bool isCurrent = false,
        bool isExpanded = false,
        IEnumerable<RepositoryTreeNode>? children = null,
        string? expansionKey = null)
    {
        Kind = kind;
        Name = name;
        ReferenceName = referenceName;
        Value = value;
        IsCurrent = isCurrent;
        _expansionKey = expansionKey ?? CreateExpansionKey(kind, name);
        _isExpanded = _expansionKey is not null && ExpansionState.TryGetValue(_expansionKey, out var savedExpansion)
            ? savedExpansion
            : ResolveDefaultExpansion(kind, name, isExpanded);

        if (children is null) return;

        var childNodes = children.ToList();
        if (childNodes.Count > 0 && childNodes.All(IsBranchNode))
        {
            var currentBranchChanged = AddGroupedBranches(childNodes);
            if (currentBranchChanged && Kind == RepositoryTreeNodeKind.Group && Name == "Branches")
                IsExpanded = true;
        }
        else
        {
            foreach (var child in childNodes) Children.Add(child);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RepositoryTreeNodeKind Kind { get; }
    public string Name { get; }
    public string? ReferenceName { get; }
    public object? Value { get; }
    public bool IsCurrent { get; }
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            if (_expansionKey is not null) ExpansionState[_expansionKey] = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }
    public ObservableCollection<RepositoryTreeNode> Children { get; } = [];
    public string DisplayName => Name;
    public FontWeight NameFontWeight => IsCurrent ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal;
    public string? IconGlyph => Kind switch
    {
        RepositoryTreeNodeKind.WorkingTree => "\uE8B7",
        RepositoryTreeNodeKind.Group when Name == "Branches" => "\uE8F0",
        RepositoryTreeNodeKind.Group when Name == "Remotes" => "\uE8AF",
        RepositoryTreeNodeKind.Group when Name == "Tags" => "\uE8EC",
        RepositoryTreeNodeKind.Group when Name == "Stashes" => "\uE8F1",
        _ => null
    };
    public Visibility IconVisibility => IconGlyph is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility LocalDefaultBranchIconVisibility =>
        Kind == RepositoryTreeNodeKind.LocalBranch && Value is GitBranch { IsDefault: true }
            ? Visibility.Visible
            : Visibility.Collapsed;
    public Visibility RemoteDefaultBranchIconVisibility =>
        Kind == RepositoryTreeNodeKind.RemoteBranch && Value is GitBranch { IsDefault: true }
            ? Visibility.Visible
            : Visibility.Collapsed;

    internal static void ResetExpansionState()
    {
        ExpansionState.Clear();
        _localBranchBuildInitialized = false;
        _lastCurrentLocalBranch = null;
    }

    private static string? CreateExpansionKey(RepositoryTreeNodeKind kind, string name) => kind switch
    {
        RepositoryTreeNodeKind.Group => $"group:{name}",
        RepositoryTreeNodeKind.Remote => $"remote:{name}",
        _ => null
    };

    private static bool ResolveDefaultExpansion(RepositoryTreeNodeKind kind, string name, bool requestedExpansion) =>
        kind switch
        {
            RepositoryTreeNodeKind.Group when name == "Remotes" => false,
            RepositoryTreeNodeKind.Remote => false,
            _ => requestedExpansion
        };

    private static bool IsBranchNode(RepositoryTreeNode node) =>
        node.Kind is RepositoryTreeNodeKind.LocalBranch or RepositoryTreeNodeKind.RemoteBranch;

    private bool AddGroupedBranches(IReadOnlyList<RepositoryTreeNode> branches)
    {
        var isLocalBranchGroup = branches.All(branch => branch.Kind == RepositoryTreeNodeKind.LocalBranch);
        var currentBranch = isLocalBranchGroup
            ? branches.FirstOrDefault(branch => branch.IsCurrent)?.ReferenceName
            : null;
        var currentBranchChanged = false;

        if (isLocalBranchGroup)
        {
            currentBranchChanged = !_localBranchBuildInitialized ||
                                   !string.Equals(_lastCurrentLocalBranch, currentBranch, StringComparison.Ordinal);
            _localBranchBuildInitialized = true;
            _lastCurrentLocalBranch = currentBranch;
        }

        foreach (var branch in branches)
        {
            var parts = branch.Name.Split('/');
            if (parts.Length == 1)
            {
                Children.Add(branch);
                continue;
            }

            AddBranch(Children, branch, parts, 0, currentBranchChanged);
        }

        SortBranchNodes(Children);
        return currentBranchChanged && currentBranch is not null;
    }

    private static void AddBranch(
        ObservableCollection<RepositoryTreeNode> nodes,
        RepositoryTreeNode branch,
        IReadOnlyList<string> parts,
        int index,
        bool forceCurrentPathExpansion)
    {
        if (index == parts.Count - 1)
        {
            nodes.Add(new RepositoryTreeNode(
                branch.Kind,
                parts[index],
                branch.ReferenceName,
                branch.Value,
                branch.IsCurrent,
                branch.IsExpanded));
            return;
        }

        var folderName = parts[index];
        var expansionKey = CreateBranchFolderExpansionKey(branch, parts, index);
        var folder = nodes.FirstOrDefault(node =>
            node.Kind == RepositoryTreeNodeKind.BranchFolder &&
            string.Equals(node.Name, folderName, StringComparison.Ordinal));

        if (folder is null)
        {
            folder = new RepositoryTreeNode(
                RepositoryTreeNodeKind.BranchFolder,
                folderName,
                expansionKey: expansionKey);
            nodes.Add(folder);
        }

        var hasSavedExpansion = ExpansionState.ContainsKey(expansionKey);
        AddBranch(folder.Children, branch, parts, index + 1, forceCurrentPathExpansion);
        if (branch.IsCurrent && (forceCurrentPathExpansion || !hasSavedExpansion))
            folder.IsExpanded = true;
    }

    private static string CreateBranchFolderExpansionKey(
        RepositoryTreeNode branch,
        IReadOnlyList<string> displayParts,
        int folderIndex)
    {
        var referenceParts = (branch.ReferenceName ?? branch.Name).Split('/');
        var referencePrefixCount = Math.Max(0, referenceParts.Length - displayParts.Count);
        var segmentCount = Math.Min(referenceParts.Length, referencePrefixCount + folderIndex + 1);
        var folderPath = string.Join('/', referenceParts.Take(segmentCount));
        return $"branch-folder:{branch.Kind}:{folderPath}";
    }

    private static void SortBranchNodes(ObservableCollection<RepositoryTreeNode> nodes)
    {
        foreach (var folder in nodes.Where(node => node.Kind == RepositoryTreeNodeKind.BranchFolder))
            SortBranchNodes(folder.Children);

        var ordered = nodes
            .OrderBy(node => node.Kind == RepositoryTreeNodeKind.BranchFolder ? 1 : 0)
            .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        nodes.Clear();
        foreach (var node in ordered) nodes.Add(node);
    }
}

public sealed record CommitFileRow(string Status, ChangedFile File)
{
    public string Path => File.Path;
    public string AddedDisplay => File.AddedLines is { } value ? $"+{value}" : string.Empty;
    public string RemovedDisplay => File.RemovedLines is { } value ? $"-{value}" : string.Empty;
}
