namespace CSharpGit.Presentation.ViewModels;

public sealed record PathTreeBuildOptions(bool CollapseSingleChildFolderChains = false);

public sealed class PathTreeNode<TItem> where TItem : class
{
    internal PathTreeNode(
        string displayName,
        string path,
        TItem? item,
        bool isFolder,
        IReadOnlyList<PathTreeNode<TItem>> children)
    {
        DisplayName = displayName;
        Path = path;
        Item = item;
        IsFolder = isFolder;
        Children = children;
    }

    public string DisplayName { get; }
    public string Path { get; }
    public TItem? Item { get; }
    public bool IsFolder { get; }
    public IReadOnlyList<PathTreeNode<TItem>> Children { get; }
}

public static class PathTreeBuilder
{
    public static IReadOnlyList<PathTreeNode<TItem>> Build<TItem>(
        IEnumerable<TItem> items,
        Func<TItem, string> getPath,
        PathTreeBuildOptions? options = null)
        where TItem : class
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(getPath);

        options ??= new PathTreeBuildOptions();
        var roots = new List<BuilderNode<TItem>>();
        var seenPaths = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            var path = getPath(item);
            if (string.IsNullOrEmpty(path) || !seenPaths.Add(path)) continue;

            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            var current = roots;
            var pathParts = new List<string>(parts.Length);
            for (var index = 0; index < parts.Length - 1; index++)
            {
                pathParts.Add(parts[index]);
                var folderPath = string.Join('/', pathParts);
                var folder = current.FirstOrDefault(node =>
                    node.IsFolder && string.Equals(node.Name, parts[index], StringComparison.Ordinal));
                if (folder is null)
                {
                    folder = new BuilderNode<TItem>(parts[index], folderPath, null, true);
                    current.Add(folder);
                }

                current = folder.Children;
            }

            current.Add(new BuilderNode<TItem>(parts[^1], path, item, false));
        }

        return Sort(roots)
            .Select(node => ToResult(node, options.CollapseSingleChildFolderChains))
            .ToList();
    }

    private static PathTreeNode<TItem> ToResult<TItem>(BuilderNode<TItem> source, bool collapseFolderChains)
        where TItem : class
    {
        var displayName = source.Name;
        var path = source.Path;
        var children = Sort(source.Children)
            .Select(child => ToResult(child, collapseFolderChains))
            .ToList();

        if (collapseFolderChains)
        {
            while (source.IsFolder && children.Count == 1 && children[0].IsFolder)
            {
                var onlyChild = children[0];
                displayName = $"{displayName}/{onlyChild.DisplayName}";
                path = onlyChild.Path;
                children = onlyChild.Children.ToList();
            }
        }

        return new PathTreeNode<TItem>(displayName, path, source.Item, source.IsFolder, children);
    }

    private static IOrderedEnumerable<BuilderNode<TItem>> Sort<TItem>(IEnumerable<BuilderNode<TItem>> nodes)
        where TItem : class =>
        nodes.OrderBy(node => node.IsFolder ? 0 : 1)
            .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(node => node.Name, StringComparer.Ordinal);

    private sealed class BuilderNode<TItem>(string name, string path, TItem? item, bool isFolder)
        where TItem : class
    {
        public string Name { get; } = name;
        public string Path { get; } = path;
        public TItem? Item { get; } = item;
        public bool IsFolder { get; } = isFolder;
        public List<BuilderNode<TItem>> Children { get; } = [];
    }
}
