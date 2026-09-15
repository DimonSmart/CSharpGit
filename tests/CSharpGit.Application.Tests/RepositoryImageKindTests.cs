using CSharpGit.Application.Abstractions;

namespace CSharpGit.Application.Tests;

public sealed partial class RepositoryImageServiceTests
{
    [Fact]
    public async Task LocalRepositoryImageIsClassifiedAsIcon()
    {
        using var fixture = new Fixture();
        var repository = fixture.CreateRepository();
        fixture.WritePng(Path.Combine(repository, ".github", "repository-icon.png"), 1);
        var service = fixture.CreateService(new StubHandler(_ => throw new InvalidOperationException()));

        await service.ResolveAsync(repository);
        var cached = service.GetCachedState(repository);

        Assert.NotNull(cached.ImagePath);
        Assert.Equal(RepositoryImageKind.Icon, cached.Kind);
    }

    [Fact]
    public async Task GitHubOpenGraphImageIsClassifiedAsPreview()
    {
        using var fixture = new Fixture();
        var repository = fixture.CreateRepository("https://github.com/owner/repository.git");
        var service = fixture.CreateService(CreateSuccessfulGitHubHandler());

        await service.ResolveAsync(repository);
        var cached = service.GetCachedState(repository);

        Assert.NotNull(cached.ImagePath);
        Assert.Equal(RepositoryImageKind.Preview, cached.Kind);
    }
}
