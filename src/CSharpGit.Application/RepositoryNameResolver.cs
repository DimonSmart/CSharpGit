namespace CSharpGit.Application;

public static class RepositoryNameResolver
{
    public static string? Resolve(string? repositoryUrl)
    {
        if (string.IsNullOrWhiteSpace(repositoryUrl))
            return null;

        var source = repositoryUrl.Trim();
        string path;
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri)
            && !string.IsNullOrWhiteSpace(uri.Scheme))
        {
            path = Uri.UnescapeDataString(
                uri.GetComponents(UriComponents.Path, UriFormat.UriEscaped));
        }
        else
        {
            path = source;
            var colon = source.IndexOf(':');
            var looksLikeWindowsDrive = colon == 1 && char.IsLetter(source[0]);
            var firstSlash = source.IndexOfAny(['/', '\\']);
            if (colon >= 0
                && !looksLikeWindowsDrive
                && (firstSlash < 0 || colon < firstSlash))
            {
                path = source[(colon + 1)..];
            }
        }

        path = path.TrimEnd('/', '\\');
        if (path.Length == 0)
            return null;

        var separator = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        var name = separator >= 0 ? path[(separator + 1)..] : path;
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        name = name.Trim();
        if (name.Length == 0 || name is "." or "..")
            return null;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return null;

        return name;
    }
}
