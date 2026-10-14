using System.Text.RegularExpressions;

namespace CSharpGit.Infrastructure;

internal enum RepositoryImageCacheKind
{
    Local,
    Remote,
    Negative
}

internal sealed class RepositoryImageCacheMetadata
{
    public RepositoryImageCacheKind Kind { get; set; }
    public string? ImageFileName { get; set; }
    public string? SourcePath { get; set; }
    public long? SourceLength { get; set; }
    public DateTimeOffset? SourceLastWriteUtc { get; set; }
    public DateTimeOffset LastValidatedUtc { get; set; }
}

internal sealed record RepositoryImageContext(
    string RepositoryPath,
    bool RepositoryExists,
    Uri? GitHubRepositoryUrl);

internal sealed record RepositoryImageSource(
    RepositoryImageCacheKind Kind,
    byte[] Bytes,
    string Extension,
    string? SourcePath = null,
    long? SourceLength = null,
    DateTimeOffset? SourceLastWriteUtc = null);

internal sealed record RepositoryImageProviderResult(
    bool Applies,
    RepositoryImageSource? Image,
    bool TransientFailure)
{
    public static RepositoryImageProviderResult NotApplicable() => new(false, null, false);
    public static RepositoryImageProviderResult NotFound() => new(true, null, false);
    public static RepositoryImageProviderResult Found(RepositoryImageSource image) => new(true, image, false);
    public static RepositoryImageProviderResult Transient() => new(true, null, true);
}

internal interface IRepositoryImageProvider
{
    Task<RepositoryImageProviderResult> ResolveAsync(
        RepositoryImageContext context,
        CancellationToken cancellationToken);
}

internal static class RepositoryImageHelpers
{
    private static readonly (string Directory, string Stem)[] LocalCandidates =
    [
        (".github", "repository-icon"),
        (".github", "logo"),
        ("docs", "logo"),
        ("", "logo"),
        ("", "icon")
    ];

    internal static FileInfo? FindLocalCandidate(string repositoryPath)
    {
        foreach (var candidate in LocalCandidates)
        {
            var directory = string.IsNullOrEmpty(candidate.Directory)
                ? repositoryPath
                : Path.Combine(repositoryPath, candidate.Directory);
            if (!Directory.Exists(directory)) continue;

            try
            {
                var match = Directory.EnumerateFiles(
                        directory,
                        candidate.Stem + ".*",
                        SearchOption.TopDirectoryOnly)
                    .Select(path => new FileInfo(path))
                    .Where(file => IsSupportedExtension(file.Extension))
                    .OrderBy(file => ExtensionPriority(file.Extension))
                    .ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (match is not null) return match;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return null;
    }

    internal static Uri? ReadGitHubOrigin(string repositoryPath)
    {
        var configPath = FindGitConfig(repositoryPath);
        if (configPath is null) return null;

        try
        {
            var inOrigin = false;
            foreach (var rawLine in File.ReadLines(configPath))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("[", StringComparison.Ordinal))
                {
                    inOrigin = Regex.IsMatch(
                        line,
                        @"^\[\s*remote\s+""origin""\s*\]$",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    continue;
                }

                if (!inOrigin || line.StartsWith('#') || line.StartsWith(';')) continue;
                var equals = line.IndexOf('=');
                if (equals < 0) continue;
                if (!line[..equals].Trim().Equals("url", StringComparison.OrdinalIgnoreCase)) continue;

                var value = line[(equals + 1)..].Trim();
                if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                    value = value[1..^1];
                return TryCanonicalGitHubUrl(value);
            }
        }
        catch
        {
        }

        return null;
    }

    internal static Uri? TryCanonicalGitHubUrl(string? remoteUrl)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl)) return null;

        var value = remoteUrl.Trim();
        string path;
        if (value.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
        {
            path = value["git@github.com:".Length..];
        }
        else if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
                 && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            path = uri.AbsolutePath.TrimStart('/');
        }
        else
        {
            return null;
        }

        path = path.Trim('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            path = path[..^4];

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return null;

        var owner = Uri.UnescapeDataString(parts[0]).ToLowerInvariant();
        var repository = Uri.UnescapeDataString(parts[1]).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repository)) return null;

        return new Uri(
            $"https://github.com/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repository)}",
            UriKind.Absolute);
    }

    internal static bool TryDetectImageExtension(byte[] bytes, out string extension)
    {
        if (bytes.Length >= 8
            && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
            && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
        {
            extension = "png";
            return true;
        }

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            extension = "jpg";
            return true;
        }

        if (bytes.Length >= 12
            && bytes[0] == (byte)'R' && bytes[1] == (byte)'I'
            && bytes[2] == (byte)'F' && bytes[3] == (byte)'F'
            && bytes[8] == (byte)'W' && bytes[9] == (byte)'E'
            && bytes[10] == (byte)'B' && bytes[11] == (byte)'P')
        {
            extension = "webp";
            return true;
        }

        extension = string.Empty;
        return false;
    }

    private static string? FindGitConfig(string repositoryPath)
    {
        var dotGit = Path.Combine(repositoryPath, ".git");
        if (Directory.Exists(dotGit))
        {
            var direct = Path.Combine(dotGit, "config");
            return File.Exists(direct) ? direct : null;
        }

        if (!File.Exists(dotGit)) return null;
        try
        {
            var firstLine = File.ReadLines(dotGit).FirstOrDefault()?.Trim();
            if (firstLine is null || !firstLine.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase))
                return null;

            var gitDirValue = firstLine["gitdir:".Length..].Trim();
            var gitDir = Path.IsPathRooted(gitDirValue)
                ? Path.GetFullPath(gitDirValue)
                : Path.GetFullPath(Path.Combine(repositoryPath, gitDirValue));

            var direct = Path.Combine(gitDir, "config");
            if (File.Exists(direct)) return direct;

            var worktrees = Directory.GetParent(gitDir);
            if (worktrees?.Name.Equals("worktrees", StringComparison.OrdinalIgnoreCase) == true)
            {
                var common = Path.Combine(worktrees.Parent?.FullName ?? string.Empty, "config");
                if (File.Exists(common)) return common;
            }
        }
        catch
        {
        }

        return null;
    }

    private static bool IsSupportedExtension(string extension) =>
        extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);

    private static int ExtensionPriority(string extension)
    {
        if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase)) return 0;
        if (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)) return 1;
        if (extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)) return 2;
        return 3;
    }
}
