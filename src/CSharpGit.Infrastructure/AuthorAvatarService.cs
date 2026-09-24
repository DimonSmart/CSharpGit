using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using CSharpGit.Application.Abstractions;

namespace CSharpGit.Infrastructure;

public sealed class AuthorAvatarService : IAuthorAvatarService
{
    private const int CanonicalSize = 64;
    private const long MaxImageBytes = 1024L * 1024;
    private const long MaxCacheBytes = 32L * 1024 * 1024;
    private const int MaxMemoryEntries = 512;
    private static readonly TimeSpan RemoteTtl = TimeSpan.FromDays(30);
    private static readonly TimeSpan NegativeTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan RemoteTimeout = TimeSpan.FromSeconds(5);

    private readonly string _cacheDirectory;
    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _remoteRequestGate = new(4, 4);
    private readonly ConcurrentDictionary<string, Lazy<Task<AuthorAvatarResult>>> _inFlight = new(StringComparer.Ordinal);
    private readonly object _memoryGate = new();
    private readonly Dictionary<string, MemoryCacheEntry> _memoryCache = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _memoryLru = [];

    public AuthorAvatarService()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CSharpGit",
                "cache",
                "author-avatars"),
            CreateHttpClient(),
            TimeProvider.System)
    {
    }

    public AuthorAvatarService(
        string cacheDirectory,
        HttpClient httpClient,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        ArgumentNullException.ThrowIfNull(httpClient);

        _cacheDirectory = Path.GetFullPath(cacheDirectory);
        _httpClient = httpClient;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<AuthorAvatarResult> ResolveAsync(
        string authorName,
        string authorEmail,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var route = CreateRoute(authorEmail);
        if (route is null)
            return Task.FromResult(new AuthorAvatarResult(null, AuthorAvatarSource.None));

        if (TryGetMemory(route.CacheIdentity, out var cached))
            return Task.FromResult(cached);

        if (TryGetDisk(route, out cached))
        {
            StoreMemory(route.CacheIdentity, cached, RemoteTtl);
            return Task.FromResult(cached);
        }

        var lazy = _inFlight.GetOrAdd(route.CacheIdentity, _ => CreateSharedResolve(route));
        return lazy.Value.WaitAsync(cancellationToken);
    }

    public Task InvalidateAsync(
        string authorName,
        string authorEmail,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var route = CreateRoute(authorEmail);
        if (route is null) return Task.CompletedTask;

        RemoveMemory(route.CacheIdentity);
        TryDelete(CachePath(route));
        return Task.CompletedTask;
    }

    internal static bool TryGetGitHubUserName(string? email, out string userName)
    {
        userName = string.Empty;
        if (string.IsNullOrWhiteSpace(email)) return false;

        var normalized = email.Trim();
        var at = normalized.LastIndexOf('@');
        if (at <= 0 || at == normalized.Length - 1) return false;
        if (!normalized[(at + 1)..].Equals("users.noreply.github.com", StringComparison.OrdinalIgnoreCase))
            return false;

        var local = normalized[..at];
        var plus = local.IndexOf('+');
        if (plus < 0)
        {
            userName = local;
            return userName.Length > 0;
        }

        if (plus == 0 || plus == local.Length - 1) return false;
        var prefix = local[..plus];
        if (!prefix.All(char.IsAsciiDigit)) return false;

        userName = local[(plus + 1)..];
        return userName.Length > 0;
    }

    internal static string NormalizeGravatarEmail(string email) =>
        email.Trim().ToLowerInvariant();

    internal static string ComputeGravatarHash(string email) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeGravatarEmail(email))))
            .ToLowerInvariant();

    private Lazy<Task<AuthorAvatarResult>> CreateSharedResolve(AvatarRoute route)
    {
        Lazy<Task<AuthorAvatarResult>>? lazy = null;
        lazy = new Lazy<Task<AuthorAvatarResult>>(
            () => ResolveAndReleaseAsync(route, lazy!),
            LazyThreadSafetyMode.ExecutionAndPublication);
        return lazy;
    }

    private async Task<AuthorAvatarResult> ResolveAndReleaseAsync(
        AvatarRoute route,
        Lazy<Task<AuthorAvatarResult>> lazy)
    {
        try
        {
            return await ResolveRemoteAsync(route).ConfigureAwait(false);
        }
        finally
        {
            if (_inFlight.TryGetValue(route.CacheIdentity, out var current)
                && ReferenceEquals(current, lazy))
                _inFlight.TryRemove(route.CacheIdentity, out _);
        }
    }

    private async Task<AuthorAvatarResult> ResolveRemoteAsync(AvatarRoute route)
    {
        var fallback = new AuthorAvatarResult(null, route.Source);
        using var timeout = new CancellationTokenSource(RemoteTimeout);
        var enteredGate = false;
        try
        {
            await _remoteRequestGate.WaitAsync(timeout.Token).ConfigureAwait(false);
            enteredGate = true;

            using var request = new HttpRequestMessage(HttpMethod.Get, route.RequestUri);
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                StoreMemory(route.CacheIdentity, fallback, NegativeTtl);
                return fallback;
            }

            if (!response.IsSuccessStatusCode)
                return fallback;

            var bytes = await ReadLimitedAsync(response.Content, timeout.Token).ConfigureAwait(false);
            if (bytes.Length == 0)
                return fallback;

            string path;
            try
            {
                path = StoreDisk(route, bytes);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return fallback;
            }

            var result = new AuthorAvatarResult(path, route.Source);
            StoreMemory(route.CacheIdentity, result, RemoteTtl);
            return result;
        }
        catch (Exception exception) when (
            exception is OperationCanceledException
            or HttpRequestException
            or IOException
            or InvalidDataException)
        {
            return fallback;
        }
        finally
        {
            if (enteredGate) _remoteRequestGate.Release();
        }
    }

    private bool TryGetMemory(string key, out AuthorAvatarResult result)
    {
        lock (_memoryGate)
        {
            if (!_memoryCache.TryGetValue(key, out var entry))
            {
                result = default!;
                return false;
            }

            if (entry.ExpiresAt <= _timeProvider.GetUtcNow()
                || entry.Result.ImagePath is { } imagePath && !File.Exists(imagePath))
            {
                RemoveMemoryLocked(key, entry);
                result = default!;
                return false;
            }

            _memoryLru.Remove(entry.Node);
            _memoryLru.AddLast(entry.Node);
            result = entry.Result;
            return true;
        }
    }

    private void StoreMemory(string key, AuthorAvatarResult result, TimeSpan ttl)
    {
        lock (_memoryGate)
        {
            if (_memoryCache.TryGetValue(key, out var previous))
                RemoveMemoryLocked(key, previous);

            var node = _memoryLru.AddLast(key);
            _memoryCache[key] = new MemoryCacheEntry(
                result,
                _timeProvider.GetUtcNow() + ttl,
                node);

            while (_memoryCache.Count > MaxMemoryEntries && _memoryLru.First is { } first)
            {
                if (_memoryCache.TryGetValue(first.Value, out var oldest))
                    RemoveMemoryLocked(first.Value, oldest);
                else
                    _memoryLru.RemoveFirst();
            }
        }
    }

    private void RemoveMemory(string key)
    {
        lock (_memoryGate)
        {
            if (_memoryCache.TryGetValue(key, out var entry))
                RemoveMemoryLocked(key, entry);
        }
    }

    private void RemoveMemoryLocked(string key, MemoryCacheEntry entry)
    {
        _memoryCache.Remove(key);
        _memoryLru.Remove(entry.Node);
    }

    private bool TryGetDisk(AvatarRoute route, out AuthorAvatarResult result)
    {
        result = default!;
        var path = CachePath(route);
        try
        {
            if (!File.Exists(path)) return false;
            var info = new FileInfo(path);
            var lastWrite = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
            if (_timeProvider.GetUtcNow() - lastWrite >= RemoteTtl)
            {
                TryDelete(path);
                return false;
            }

            Touch(path);
            result = new AuthorAvatarResult(path, route.Source);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or ArgumentException)
        {
            return false;
        }
    }

    private string StoreDisk(AvatarRoute route, byte[] bytes)
    {
        Directory.CreateDirectory(_cacheDirectory);
        var path = CachePath(route);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, true);
        }
        finally
        {
            TryDelete(temporary);
        }

        try
        {
            File.SetLastWriteTimeUtc(path, _timeProvider.GetUtcNow().UtcDateTime);
        }
        catch
        {
        }

        Touch(path);
        CleanupDiskCache(path);
        return path;
    }

    private void CleanupDiskCache(string keepPath)
    {
        try
        {
            var files = Directory.EnumerateFiles(_cacheDirectory, "*.img")
                .Select(path => new FileInfo(path))
                .ToList();
            var total = files.Sum(file => file.Length);
            if (total <= MaxCacheBytes) return;

            foreach (var file in files
                         .Where(file => !string.Equals(file.FullName, keepPath, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(file => file.LastAccessTimeUtc))
            {
                if (total <= MaxCacheBytes) break;
                total -= file.Length;
                TryDelete(file.FullName);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string CachePath(AvatarRoute route) =>
        Path.Combine(_cacheDirectory, Hash(route.CacheIdentity) + ".img");

    private static AvatarRoute? CreateRoute(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var trimmed = email.Trim();

        if (TryGetGitHubUserName(trimmed, out var userName))
        {
            var normalizedUserName = userName.ToLowerInvariant();
            var identity = $"v1|{CanonicalSize}|github|{normalizedUserName}";
            var encoded = Uri.EscapeDataString(userName);
            return new AvatarRoute(
                identity,
                AuthorAvatarSource.GitHub,
                new Uri($"https://github.com/{encoded}.png?size={CanonicalSize}", UriKind.Absolute));
        }

        if (!LooksLikeEmail(trimmed)) return null;
        var normalized = NormalizeGravatarEmail(trimmed);
        var hash = ComputeGravatarHash(normalized);
        return new AvatarRoute(
            $"v1|{CanonicalSize}|gravatar|{normalized}",
            AuthorAvatarSource.Gravatar,
            new Uri($"https://www.gravatar.com/avatar/{hash}?s={CanonicalSize}&d=404", UriKind.Absolute));
    }

    private static bool LooksLikeEmail(string value)
    {
        if (value.Any(char.IsWhiteSpace)) return false;
        var at = value.IndexOf('@');
        return at > 0 && at == value.LastIndexOf('@') && at < value.Length - 1;
    }

    private static async Task<byte[]> ReadLimitedAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > MaxImageBytes)
            throw new InvalidDataException("Avatar response is too large.");

        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (buffer.Length + read > MaxImageBytes)
                throw new InvalidDataException("Avatar response is too large.");
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private void Touch(string path)
    {
        try
        {
            File.SetLastAccessTimeUtc(path, _timeProvider.GetUtcNow().UtcDateTime);
        }
        catch
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CSharpGit", "0.1"));
        return client;
    }

    private sealed record AvatarRoute(
        string CacheIdentity,
        AuthorAvatarSource Source,
        Uri RequestUri);

    private sealed record MemoryCacheEntry(
        AuthorAvatarResult Result,
        DateTimeOffset ExpiresAt,
        LinkedListNode<string> Node);
}
