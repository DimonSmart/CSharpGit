using System.Net;
using CSharpGit.Application.Abstractions;
using CSharpGit.Infrastructure;

namespace CSharpGit.Application.Tests;

public sealed class AuthorAvatarServiceTests : IDisposable
{
    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "CSharpGit.AuthorAvatars.Tests",
        Guid.NewGuid().ToString("N"));

    public AuthorAvatarServiceTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("john@users.noreply.github.com", "john")]
    [InlineData("12345+john@users.noreply.github.com", "john")]
    [InlineData("JOHN@users.noreply.github.com", "JOHN")]
    [InlineData("17778523+DimonSmart@USERS.NOREPLY.GITHUB.COM", "DimonSmart")]
    public void GitHubNoreplyIdentityExtractsUserName(string email, string expected)
    {
        Assert.True(AuthorAvatarService.TryGetGitHubUserName(email, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("")]
    [InlineData("john@example.com")]
    [InlineData("abc+john@users.noreply.github.com")]
    [InlineData("+john@users.noreply.github.com")]
    [InlineData("12345+@users.noreply.github.com")]
    public void MalformedOrNonGitHubAddressIsNotGitHubIdentity(string email) =>
        Assert.False(AuthorAvatarService.TryGetGitHubUserName(email, out _));

    [Fact]
    public void GravatarUsesTrimLowerUtf8Sha256()
    {
        Assert.Equal(
            "973dfe463ec85785f5f95af5ba3906eedb2d931c24e69824a89ea65dba4e813b",
            AuthorAvatarService.ComputeGravatarHash("  TEST@example.com "));
    }

    [Fact]
    public async Task GravatarRequestUsesSha256AndExplicit404Fallback()
    {
        Uri? requested = null;
        var handler = new StubHandler(request =>
        {
            requested = request.RequestUri;
            return ImageResponse();
        });
        var service = CreateService(handler);

        var result = await service.ResolveAsync("Test User", "  TEST@example.com ");

        Assert.Equal(AuthorAvatarSource.Gravatar, result.Source);
        Assert.NotNull(result.ImagePath);
        Assert.Equal(
            "https://www.gravatar.com/avatar/973dfe463ec85785f5f95af5ba3906eedb2d931c24e69824a89ea65dba4e813b?s=64&d=404",
            requested?.AbsoluteUri);
    }

    [Fact]
    public async Task GitHubNoreplyDoesNotFallThroughToGravatar()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Equal("github.com", request.RequestUri?.Host);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var service = CreateService(handler);

        var result = await service.ResolveAsync("John", "12345+john@users.noreply.github.com");

        Assert.Null(result.ImagePath);
        Assert.Equal(AuthorAvatarSource.GitHub, result.Source);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GitHubCacheIdentityIsCaseInsensitive()
    {
        var handler = new StubHandler(_ => ImageResponse());
        var service = CreateService(handler);

        var upper = await service.ResolveAsync("John", "JOHN@users.noreply.github.com");
        var lower = await service.ResolveAsync("John", "john@users.noreply.github.com");

        Assert.NotNull(upper.ImagePath);
        Assert.Equal(upper.ImagePath, lower.ImagePath);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task EmptyOrUnusableEmailDoesNotUseNetwork()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("Network must not be used."));
        var service = CreateService(handler);

        var empty = await service.ResolveAsync("John Doe", "");
        var malformed = await service.ResolveAsync("John Doe", "not-an-email");

        Assert.Null(empty.ImagePath);
        Assert.Null(malformed.ImagePath);
        Assert.Equal(AuthorAvatarSource.None, empty.Source);
        Assert.Equal(AuthorAvatarSource.None, malformed.Source);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task SequentialLookupsUseBoundedMemoryCache()
    {
        var handler = new StubHandler(_ => ImageResponse());
        var service = CreateService(handler);

        var first = await service.ResolveAsync("John Doe", "john@example.com");
        var second = await service.ResolveAsync("John Doe", "john@example.com");

        Assert.NotNull(first.ImagePath);
        Assert.Equal(first, second);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task ConcurrentLookupsAreDeduplicated()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHandler(async (_, cancellationToken) =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return ImageResponse();
        });
        var service = CreateService(handler);

        var first = service.ResolveAsync("John Doe", "john@example.com");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = service.ResolveAsync("John Doe", "john@example.com");
        release.TrySetResult();

        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(results[0], results[1]);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task CancellingOneConsumerDoesNotCancelSharedRemoteRequest()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHandler(async (_, cancellationToken) =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return ImageResponse();
        });
        var service = CreateService(handler);
        using var cancellation = new CancellationTokenSource();

        var cancelled = service.ResolveAsync("John Doe", "john@example.com", cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var survivor = service.ResolveAsync("John Doe", "john@example.com");

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await cancelled);

        release.TrySetResult();
        var result = await survivor.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(result.ImagePath);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task FreshDiskCacheAvoidsNetworkAcrossServiceInstances()
    {
        var firstHandler = new StubHandler(_ => ImageResponse());
        var firstService = CreateService(firstHandler);
        var first = await firstService.ResolveAsync("John Doe", "john@example.com");

        var secondHandler = new StubHandler(_ => throw new InvalidOperationException("Fresh disk cache must avoid network."));
        var secondService = CreateService(secondHandler);
        var second = await secondService.ResolveAsync("John Doe", "john@example.com");

        Assert.NotNull(first.ImagePath);
        Assert.Equal(first.ImagePath, second.ImagePath);
        Assert.Equal(0, secondHandler.RequestCount);
    }

    [Fact]
    public async Task ExpiredDiskEntryTriggersNewRequest()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero));
        var handler = new StubHandler(_ => ImageResponse());
        var service = CreateService(handler, clock);

        Assert.NotNull((await service.ResolveAsync("John Doe", "john@example.com")).ImagePath);
        clock.Advance(TimeSpan.FromDays(31));
        Assert.NotNull((await service.ResolveAsync("John Doe", "john@example.com")).ImagePath);

        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task NotFoundUsesNegativeMemoryCache()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var service = CreateService(handler);

        var first = await service.ResolveAsync("John Doe", "john@example.com");
        var second = await service.ResolveAsync("John Doe", "john@example.com");

        Assert.Null(first.ImagePath);
        Assert.Null(second.ImagePath);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task NegativeCacheExpiresAfterOneDay()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var service = CreateService(handler, clock);

        Assert.Null((await service.ResolveAsync("John Doe", "john@example.com")).ImagePath);
        clock.Advance(TimeSpan.FromHours(25));
        Assert.Null((await service.ResolveAsync("John Doe", "john@example.com")).ImagePath);

        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task TransientFailureDoesNotBecomeNegativeCache()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var service = CreateService(handler);

        Assert.Null((await service.ResolveAsync("John Doe", "john@example.com")).ImagePath);
        Assert.Null((await service.ResolveAsync("John Doe", "john@example.com")).ImagePath);

        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task OversizedResponseIsRejectedAndRetriedLater()
    {
        var bytes = new byte[1024 * 1024 + 1];
        var handler = new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            });
        var service = CreateService(handler);

        Assert.Null((await service.ResolveAsync("John Doe", "john@example.com")).ImagePath);
        Assert.Null((await service.ResolveAsync("John Doe", "john@example.com")).ImagePath);

        Assert.Equal(2, handler.RequestCount);
        Assert.Empty(Directory.Exists(CacheDirectory)
            ? Directory.EnumerateFiles(CacheDirectory, "*.img")
            : []);
    }

    [Fact]
    public async Task DiskCacheFileNameDoesNotContainIdentity()
    {
        var service = CreateService(new StubHandler(_ => ImageResponse()));

        var result = await service.ResolveAsync("John Doe", "john@example.com");

        var fileName = Path.GetFileName(result.ImagePath);
        Assert.NotNull(fileName);
        Assert.DoesNotContain("john", fileName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("example", fileName, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".img", fileName, StringComparison.OrdinalIgnoreCase);
    }

    private string CacheDirectory => Path.Combine(_root, "cache");

    private AuthorAvatarService CreateService(
        StubHandler handler,
        TimeProvider? timeProvider = null)
    {
        var client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CSharpGit.Tests/1.0");
        return new AuthorAvatarService(CacheDirectory, client, timeProvider);
    }

    private static HttpResponseMessage ImageResponse() =>
        new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(PngBytes)
        };

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
        catch
        {
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;
        private int _requestCount;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            : this((request, _) => Task.FromResult(handler(request)))
        {
        }

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) =>
            _handler = handler;

        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            return await _handler(request, cancellationToken);
        }
    }
}
