using System.Text.Json;

namespace CSharpGit.Infrastructure;

public sealed partial class RepositoryImageService
{
    private string ResolveCacheKey(RepositoryImageContext context)
    {
        if (context.GitHubRepositoryUrl is not null)
            return Hash(context.GitHubRepositoryUrl.AbsoluteUri);

        if (!context.RepositoryExists)
        {
            var aliasPath = AliasPath(context.RepositoryPath);
            try
            {
                if (File.Exists(aliasPath))
                {
                    var alias = File.ReadAllText(aliasPath).Trim();
                    if (IsHash(alias)) return alias;
                }
            }
            catch
            {
            }
        }

        return Hash(NormalizePathIdentity(context.RepositoryPath));
    }

    private string StoreImage(RepositoryImageContext context, RepositoryImageSource source)
    {
        Directory.CreateDirectory(_cacheDirectory);
        var key = ActiveKey(context);
        var contentHash = HashBytes(source.Bytes);
        var imageName = $"{key}-{contentHash[..12]}.{source.Extension}";
        var imagePath = Path.Combine(_cacheDirectory, imageName);
        var previous = ReadMetadata(key);

        if (!File.Exists(imagePath))
            AtomicWriteBytes(imagePath, source.Bytes);

        WriteMetadata(
            key,
            new RepositoryImageCacheMetadata
            {
                Kind = source.Kind,
                ImageFileName = imageName,
                SourcePath = source.SourcePath,
                SourceLength = source.SourceLength,
                SourceLastWriteUtc = source.SourceLastWriteUtc,
                LastValidatedUtc = _timeProvider.GetUtcNow()
            });
        WriteAlias(context.RepositoryPath, key);
        Touch(imagePath, _timeProvider.GetUtcNow());

        if (previous?.ImageFileName is { } oldName
            && !oldName.Equals(imageName, StringComparison.Ordinal))
            TryDelete(Path.Combine(_cacheDirectory, oldName));

        CleanupCache(imagePath);
        return imagePath;
    }

    private void StoreNegative(RepositoryImageContext context)
    {
        Directory.CreateDirectory(_cacheDirectory);
        var key = ActiveKey(context);
        var previous = ReadMetadata(key);
        var candidate = context.RepositoryExists
            ? RepositoryImageHelpers.FindLocalCandidate(context.RepositoryPath)
            : null;

        WriteMetadata(
            key,
            new RepositoryImageCacheMetadata
            {
                Kind = RepositoryImageCacheKind.Negative,
                SourcePath = candidate?.FullName,
                SourceLength = candidate?.Length,
                SourceLastWriteUtc = candidate is null
                    ? null
                    : new DateTimeOffset(candidate.LastWriteTimeUtc, TimeSpan.Zero),
                LastValidatedUtc = _timeProvider.GetUtcNow()
            });
        WriteAlias(context.RepositoryPath, key);

        if (previous?.ImageFileName is { } oldName)
            TryDelete(Path.Combine(_cacheDirectory, oldName));
    }

    private bool IsRemoteCache(RepositoryImageContext context)
    {
        var metadata = ReadMetadata(ResolveCacheKey(context));
        return metadata?.Kind == RepositoryImageCacheKind.Remote;
    }

    private void MarkRemoteCacheValidated(RepositoryImageContext context)
    {
        var key = ResolveCacheKey(context);
        var metadata = ReadMetadata(key);
        if (metadata?.Kind != RepositoryImageCacheKind.Remote) return;
        metadata.LastValidatedUtc = _timeProvider.GetUtcNow();
        WriteMetadata(key, metadata);
    }

    private string ActiveKey(RepositoryImageContext context) =>
        context.GitHubRepositoryUrl is not null
            ? Hash(context.GitHubRepositoryUrl.AbsoluteUri)
            : Hash(NormalizePathIdentity(context.RepositoryPath));

    private RepositoryImageCacheMetadata? ReadMetadata(string key)
    {
        var path = Path.Combine(_cacheDirectory, key + ".json");
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<RepositoryImageCacheMetadata>(
                File.ReadAllText(path),
                JsonOptions);
        }
        catch (Exception exception)
        {
            Log($"Could not read metadata '{path}'.", exception);
            return null;
        }
    }

    private void WriteMetadata(string key, RepositoryImageCacheMetadata metadata) =>
        AtomicWriteText(
            Path.Combine(_cacheDirectory, key + ".json"),
            JsonSerializer.Serialize(metadata, JsonOptions));

    private void WriteAlias(string repositoryPath, string key) =>
        AtomicWriteText(AliasPath(repositoryPath), key);

    private string AliasPath(string repositoryPath) =>
        Path.Combine(_cacheDirectory, Hash("local:" + NormalizePathIdentity(repositoryPath)) + ".alias");

    private string? GetImagePath(RepositoryImageCacheMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata.ImageFileName)) return null;
        var path = Path.Combine(_cacheDirectory, metadata.ImageFileName);
        return File.Exists(path) ? path : null;
    }

    private void CleanupCache(string keepImagePath)
    {
        try
        {
            var images = Directory.EnumerateFiles(_cacheDirectory)
                .Where(IsCachedImage)
                .Select(path => new FileInfo(path))
                .ToList();
            var total = images.Sum(file => file.Length);
            if (total <= MaxCacheBytes) return;

            foreach (var file in images
                         .Where(file => !PathsEqual(file.FullName, keepImagePath))
                         .OrderBy(file => file.LastAccessTimeUtc))
            {
                if (total <= MaxCacheBytes) break;
                total -= file.Length;
                var key = ExtractKey(file.Name);
                TryDelete(file.FullName);
                if (key is not null)
                    TryDelete(Path.Combine(_cacheDirectory, key + ".json"));
            }
        }
        catch (Exception exception)
        {
            Log("Could not clean repository image cache.", exception);
        }
    }

}
