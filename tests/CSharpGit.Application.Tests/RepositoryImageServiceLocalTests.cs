using CSharpGit.Infrastructure;

namespace CSharpGit.Application.Tests;

public sealed partial class RepositoryImageServiceTests
{
    [Fact]
    public async Task LocalLogoIsResolvedWithoutNetwork()
    {
        using var fixture = new Fixture();
        var repository = fixture.CreateRepository();
        fixture.WritePng(Path.Combine(repository, "logo.png"), 1);
        var handler = new StubHandler(_ => throw new InvalidOperationException("Network must not be used."));
        var service = fixture.CreateService(handler);

        var imagePath = await service.ResolveAsync(repository);

        Assert.NotNull(imagePath);
        Assert.True(File.Exists(imagePath));
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task HigherPriorityLocalCandidateWins()
    {
        using var fixture = new Fixture();
        var repository = fixture.CreateRepository();
        var lowPriority = fixture.WritePng(Path.Combine(repository, "logo.png"), 1);
        var highPriority = fixture.WritePng(Path.Combine(repository, ".github", "repository-icon.png"), 2);
        var service = fixture.CreateService(new StubHandler(_ => throw new InvalidOperationException()));

        var imagePath = await service.ResolveAsync(repository);

        Assert.NotNull(imagePath);
        Assert.False(File.ReadAllBytes(lowPriority).SequenceEqual(File.ReadAllBytes(imagePath!)));
        Assert.True(File.ReadAllBytes(highPriority).SequenceEqual(File.ReadAllBytes(imagePath!)));
    }

    [Fact]
    public async Task RepositoryWithoutImageUsesFallbackAndDoesNotUseNetworkWithoutRemote()
    {
        using var fixture = new Fixture();
        var repository = fixture.CreateRepository();
        var handler = new StubHandler(_ => throw new InvalidOperationException("Network must not be used."));
        var service = fixture.CreateService(handler);

        var imagePath = await service.ResolveAsync(repository);
        var cached = service.GetCachedState(repository);

        Assert.Null(imagePath);
        Assert.Null(cached.ImagePath);
        Assert.False(cached.ShouldRefresh);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task CorruptLocalImageDoesNotEscapeAsRepositoryImage()
    {
        using var fixture = new Fixture();
        var repository = fixture.CreateRepository();
        var path = Path.Combine(repository, "logo.png");
        File.WriteAllText(path, "not an image");
        var service = fixture.CreateService(new StubHandler(_ => throw new InvalidOperationException()));

        var imagePath = await service.ResolveAsync(repository);

        Assert.Null(imagePath);
    }

    [Fact]
    public async Task ValidLocalCacheReturnsSameImageWithoutNetwork()
    {
        using var fixture = new Fixture();
        var repository = fixture.CreateRepository();
        fixture.WritePng(Path.Combine(repository, "logo.png"), 1);
        var handler = new StubHandler(_ => throw new InvalidOperationException("Network must not be used."));
        var service = fixture.CreateService(handler);

        var first = await service.ResolveAsync(repository);
        var cached = service.GetCachedState(repository);
        var second = await service.ResolveAsync(repository);

        Assert.NotNull(first);
        Assert.Equal(first, cached.ImagePath);
        Assert.False(cached.ShouldRefresh);
        Assert.Equal(first, second);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task ChangedLocalImageInvalidatesCache()
    {
        using var fixture = new Fixture();
        var repository = fixture.CreateRepository();
        var sourcePath = fixture.WritePng(Path.Combine(repository, "logo.png"), 1);
        var service = fixture.CreateService(new StubHandler(_ => throw new InvalidOperationException()));

        var first = await service.ResolveAsync(repository);
        fixture.WritePng(sourcePath, 2);
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(2));

        var stale = service.GetCachedState(repository);
        var second = await service.ResolveAsync(repository);

        Assert.NotNull(first);
        Assert.Equal(first, stale.ImagePath);
        Assert.True(stale.ShouldRefresh);
        Assert.NotNull(second);
        Assert.NotEqual(first, second);
        Assert.True(File.ReadAllBytes(sourcePath).SequenceEqual(File.ReadAllBytes(second!)));
    }

}
