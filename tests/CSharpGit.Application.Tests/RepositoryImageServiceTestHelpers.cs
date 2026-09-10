using System.Net;
using CSharpGit.Infrastructure;

namespace CSharpGit.Application.Tests;

public sealed partial class RepositoryImageServiceTests
{
    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private static StubHandler CreateSuccessfulGitHubHandler() => new(GitHubResponse);

    private static HttpResponseMessage GitHubResponse(HttpRequestMessage request)
    {
        if (request.RequestUri?.Host == "github.com")
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "<html><head><meta property=\"og:image\" content=\"https://opengraph.githubassets.com/test/owner/repository\" /></head></html>")
            };
        }

        Assert.Equal("opengraph.githubassets.com", request.RequestUri?.Host);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(PngBytes)
        };
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "CSharpGit.RepositoryImages.Tests", Guid.NewGuid().ToString("N"));

        public Fixture()
        {
            Directory.CreateDirectory(_root);
        }

        public string CacheDirectory => Path.Combine(_root, "cache");

        public string CreateRepository(string? remote = null)
        {
            var repository = Path.Combine(_root, "repository-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(repository);
            if (remote is not null)
            {
                var gitDirectory = Path.Combine(repository, ".git");
                Directory.CreateDirectory(gitDirectory);
                File.WriteAllText(
                    Path.Combine(gitDirectory, "config"),
                    $"[remote \"origin\"]{Environment.NewLine}\turl = {remote}{Environment.NewLine}");
            }

            return repository;
        }

        public string WritePng(string path, byte discriminator)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, [.. PngBytes, discriminator]);
            return path;
        }

        public RepositoryImageService CreateService(
            StubHandler handler,
            TimeProvider? timeProvider = null)
        {
            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(5)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CSharpGit.Tests/1.0");
            return new RepositoryImageService(CacheDirectory, client, timeProvider);
        }

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
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow += duration;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;
        private int _requestCount;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            : this((request, _) => Task.FromResult(handler(request)))
        {
        }

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

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
