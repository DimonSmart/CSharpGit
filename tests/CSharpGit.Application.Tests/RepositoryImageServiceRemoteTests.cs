using System.Net;
using CSharpGit.Infrastructure;

namespace CSharpGit.Application.Tests;

public sealed partial class RepositoryImageServiceTests
{
    [Fact]
    public async Task ValidRemoteCacheAvoidsNetwork()
    {
        using var fixture = new Fixture();
        var repository = fixture.CreateRepository("https://github.com/Owner/Repository.git");
        var firstHandler = CreateSuccessfulGitHubHandler();
        var firstService = fixture.CreateService(firstHandler);

        var first = await firstService.ResolveAsync(repository);

        Assert.NotNull(first);
        Assert.Equal(2, firstHandler.RequestCount);

        var secondHandler = new StubHandler(_ => throw new InvalidOperationException("Fresh cache must avoid network."));
        var secondService = fixture.CreateService(secondHandler);
        var cached = secondService.GetCachedState(repository);
        var second = await secondService.ResolveAsync(repository);

        Assert.Equal(first, cached.ImagePath);
        Assert.False(cached.ShouldRefresh);
        Assert.Equal(first, second);
        Assert.Equal(0, secondHandler.RequestCount);
    }

    [Fact]
    public async Task ExpiredRemoteCacheIsReturnedImmediatelyAndMarkedForRefresh()
    {
        using var fixture = new Fixture();
        var repository = fixture.CreateRepository("git@github.com:Owner/Repository.git");
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero));
        var service = fixture.CreateService(CreateSuccessfulGitHubHandler(), clock);

        var imagePath = await service.ResolveAsync(repository);
        clock.Advance(TimeSpan.FromDays(8));

        var cached = service.GetCachedState(repository);

        Assert.NotNull(imagePath);
        Assert.Equal(imagePath, cached.ImagePath);
        Assert.True(cached.ShouldRefresh);
    }

    [Fact]
    public async Task FailedRemoteRefreshKeepsStaleImage()
    {
        using var fixture = new Fixture();
        var repository = fixture.CreateRepository("https://github.com/owner/repository");
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero));
        var fail = false;
        var handler = new StubHandler(request =>
        {
            if (fail)
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

            return GitHubResponse(request);
        });
        var service = fixture.CreateService(handler, clock);

        var first = await service.ResolveAsync(repository);
        clock.Advance(TimeSpan.FromDays(8));
        fail = true;

        var second = await service.ResolveAsync(repository);

        Assert.NotNull(first);
        Assert.Equal(first, second);
        Assert.True(File.Exists(second));
    }

    [Fact]
    public async Task NegativeCachePreventsRepeatedGitHubRequest()
    {
        using var fixture = new Fixture();
        var repository = fixture.CreateRepository("ssh://git@github.com/owner/repository.git");
        var handler = new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html><head></head><body></body></html>")
            });
        var service = fixture.CreateService(handler);

        var first = await service.ResolveAsync(repository);
        var cached = service.GetCachedState(repository);
        var second = await service.ResolveAsync(repository);

        Assert.Null(first);
        Assert.Null(cached.ImagePath);
        Assert.False(cached.ShouldRefresh);
        Assert.Null(second);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task NewLocalCandidateInvalidatesNegativeCacheImmediately()
    {
        using var fixture = new Fixture();
        var repository = fixture.CreateRepository();
        var service = fixture.CreateService(new StubHandler(_ => throw new InvalidOperationException()));

        Assert.Null(await service.ResolveAsync(repository));
        Assert.False(service.GetCachedState(repository).ShouldRefresh);

        fixture.WritePng(Path.Combine(repository, "icon.png"), 3);

        Assert.True(service.GetCachedState(repository).ShouldRefresh);
        Assert.NotNull(await service.ResolveAsync(repository));
    }

    [Fact]
    public async Task DeletedRepositoryCanKeepUsingCachedImage()
    {
        using var fixture = new Fixture();
        var repository = fixture.CreateRepository("https://github.com/owner/repository.git");
        fixture.WritePng(Path.Combine(repository, "docs", "logo.png"), 1);
        var service = fixture.CreateService(new StubHandler(_ => throw new InvalidOperationException()));

        var imagePath = await service.ResolveAsync(repository);
        Directory.Delete(repository, true);

        var cached = service.GetCachedState(repository);

        Assert.NotNull(imagePath);
        Assert.Equal(imagePath, cached.ImagePath);
        Assert.False(cached.ShouldRefresh);
    }

    [Theory]
    [InlineData("https://github.com/Owner/Repository.git")]
    [InlineData("https://github.com/Owner/Repository")]
    [InlineData("git@github.com:Owner/Repository.git")]
    [InlineData("ssh://git@github.com/Owner/Repository.git")]
    public async Task SupportedGitHubRemoteFormatsUseCanonicalRepositoryPage(string remote)
    {
        using var fixture = new Fixture();
        var repository = fixture.CreateRepository(remote);
        var handler = new StubHandler(request =>
        {
            Assert.Equal("https://github.com/owner/repository", request.RequestUri?.AbsoluteUri);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html></html>")
            };
        });
        var service = fixture.CreateService(handler);

        Assert.Null(await service.ResolveAsync(repository));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task InFlightRemoteResolutionIsDeduplicated()
    {
        using var fixture = new Fixture();
        var repository = fixture.CreateRepository("https://github.com/owner/repository.git");
        var firstRequestStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri?.Host == "github.com")
            {
                firstRequestStarted.TrySetResult(true);
                await release.Task.WaitAsync(cancellationToken);
            }

            return GitHubResponse(request);
        });
        var service = fixture.CreateService(handler);

        var first = service.ResolveAsync(repository);
        await firstRequestStarted.Task;
        var second = service.ResolveAsync(repository);
        release.TrySetResult(true);

        var results = await Task.WhenAll(first, second);

        Assert.Equal(2, handler.RequestCount);
        Assert.Equal(results[0], results[1]);
    }

}
