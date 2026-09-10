using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CSharpGit.Application.Abstractions;

namespace CSharpGit.Infrastructure;

public sealed partial class RepositoryImageService : IRepositoryImageService
{
    private const long MaxImageBytes = 5L * 1024 * 1024;
    private const long MaxHtmlBytes = 2L * 1024 * 1024;
    private const long MaxCacheBytes = 50L * 1024 * 1024;
    private static readonly TimeSpan RemoteTtl = TimeSpan.FromDays(7);
    private static readonly TimeSpan NegativeTtl = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _cacheDirectory;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _remoteRequestGate = new(4, 4);
    private readonly IReadOnlyList<IRepositoryImageProvider> _providers;
    private readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _inFlight = new(
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);

    public RepositoryImageService()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CSharpGit",
                "cache",
                "repository-images"),
            CreateHttpClient(),
            TimeProvider.System)
    {
    }

    public RepositoryImageService(
        string cacheDirectory,
        HttpClient httpClient,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        ArgumentNullException.ThrowIfNull(httpClient);

        _cacheDirectory = Path.GetFullPath(cacheDirectory);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _providers =
        [
            new LocalRepositoryImageProvider(MaxImageBytes),
            new GitHubRepositoryImageProvider(httpClient, _remoteRequestGate, MaxHtmlBytes, MaxImageBytes)
        ];
    }

    public RepositoryImageCacheState GetCachedState(string repositoryPath)
    {
        try
        {
            var context = CreateContext(repositoryPath);
            var metadata = ReadMetadata(ResolveCacheKey(context));
            if (metadata is null) return new(null, true);

            var now = _timeProvider.GetUtcNow();
            if (metadata.Kind == RepositoryImageCacheKind.Negative)
            {
                if (context.RepositoryExists
                    && !CandidateMatchesMetadata(
                        RepositoryImageHelpers.FindLocalCandidate(context.RepositoryPath),
                        metadata))
                    return new(null, true);

                return new(null, now - metadata.LastValidatedUtc >= NegativeTtl);
            }

            var imagePath = GetImagePath(metadata);
            if (imagePath is null) return new(null, true);
            Touch(imagePath, now);
            if (!context.RepositoryExists) return new(imagePath, false);

            var localCandidate = RepositoryImageHelpers.FindLocalCandidate(context.RepositoryPath);
            if (metadata.Kind == RepositoryImageCacheKind.Local)
                return new(imagePath, !CandidateMatchesMetadata(localCandidate, metadata));

            // A local logo always outranks a previously cached remote preview.
            if (localCandidate is not null) return new(imagePath, true);
            return new(imagePath, now - metadata.LastValidatedUtc >= RemoteTtl);
        }
        catch (Exception exception)
        {
            Log($"Could not read cache for '{repositoryPath}'.", exception);
            return new(null, true);
        }
    }

    public Task<string?> ResolveAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = NormalizeRepositoryPath(repositoryPath);
        var lazy = _inFlight.GetOrAdd(
            path,
            _ => new Lazy<Task<string?>>(
                () => ResolveCoreAsync(path, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));
        return AwaitAndReleaseAsync(path, lazy);
    }

    private async Task<string?> AwaitAndReleaseAsync(string path, Lazy<Task<string?>> lazy)
    {
        try
        {
            return await lazy.Value.ConfigureAwait(false);
        }
        finally
        {
            if (_inFlight.TryGetValue(path, out var current) && ReferenceEquals(current, lazy))
                _inFlight.TryRemove(path, out _);
        }
    }

    private async Task<string?> ResolveCoreAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        var cached = GetCachedState(repositoryPath);
        if (!cached.ShouldRefresh) return cached.ImagePath;

        var context = CreateContext(repositoryPath);
        RepositoryImageProviderResult? lastResult = null;
        foreach (var provider in _providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RepositoryImageProviderResult result;
            try
            {
                result = await provider.ResolveAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Log($"Provider {provider.GetType().Name} failed for '{repositoryPath}'.", exception);
                result = RepositoryImageProviderResult.Transient();
            }

            if (!result.Applies) continue;
            lastResult = result;
            if (result.Image is not null)
            {
                try
                {
                    return StoreImage(context, result.Image);
                }
                catch (Exception exception)
                {
                    Log($"Could not cache image for '{repositoryPath}'.", exception);
                    return cached.ImagePath;
                }
            }

            if (result.TransientFailure) return cached.ImagePath;
        }

        if (cached.ImagePath is not null && IsRemoteCache(context))
        {
            if (lastResult is { TransientFailure: false })
                MarkRemoteCacheValidated(context);
            return cached.ImagePath;
        }

        try
        {
            StoreNegative(context);
        }
        catch (Exception exception)
        {
            Log($"Could not store negative cache for '{repositoryPath}'.", exception);
        }

        return null;
    }

    private RepositoryImageContext CreateContext(string repositoryPath)
    {
        var path = NormalizeRepositoryPath(repositoryPath);
        var exists = Directory.Exists(path);
        return new(
            path,
            exists,
            exists ? RepositoryImageHelpers.ReadGitHubOrigin(path) : null);
    }

}
