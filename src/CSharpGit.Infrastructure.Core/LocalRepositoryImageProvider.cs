namespace CSharpGit.Infrastructure;

internal sealed class LocalRepositoryImageProvider(long maxImageBytes) : IRepositoryImageProvider
{
    public Task<RepositoryImageProviderResult> ResolveAsync(
        RepositoryImageContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.RepositoryExists)
            return Task.FromResult(RepositoryImageProviderResult.NotApplicable());

        var candidate = RepositoryImageHelpers.FindLocalCandidate(context.RepositoryPath);
        if (candidate is null)
            return Task.FromResult(RepositoryImageProviderResult.NotFound());

        try
        {
            if (candidate.Length <= 0 || candidate.Length > maxImageBytes)
                return Task.FromResult(RepositoryImageProviderResult.NotFound());

            var bytes = File.ReadAllBytes(candidate.FullName);
            if (!RepositoryImageHelpers.TryDetectImageExtension(bytes, out var extension))
                return Task.FromResult(RepositoryImageProviderResult.NotFound());

            return Task.FromResult(RepositoryImageProviderResult.Found(
                new RepositoryImageSource(
                    RepositoryImageCacheKind.Local,
                    bytes,
                    extension,
                    candidate.FullName,
                    candidate.Length,
                    new DateTimeOffset(candidate.LastWriteTimeUtc, TimeSpan.Zero))));
        }
        catch (IOException)
        {
            return Task.FromResult(RepositoryImageProviderResult.Transient());
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(RepositoryImageProviderResult.Transient());
        }
    }
}
