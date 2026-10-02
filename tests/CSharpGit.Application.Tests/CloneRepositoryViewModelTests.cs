using CSharpGit.Application.Abstractions;
using CSharpGit.Infrastructure;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Application.Tests;

public sealed class CloneRepositoryViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"csharpgit-clone-vm-{Guid.NewGuid():N}");

    public CloneRepositoryViewModelTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task AutomaticTargetTracksRepositoryUrlUntilUserEditsIt()
    {
        var settings = await CreateSettingsAsync();
        var viewModel = new CloneRepositoryViewModel(
            new RecordingCloneService(),
            settings,
            new FixedFolderPicker(null));

        viewModel.Reset();
        viewModel.RepositoryUrl = "https://github.com/owner/RepoA.git";

        Assert.Equal(Path.Combine(_root, "RepoA"), viewModel.LocalDirectory);
        Assert.False(viewModel.TargetIsUserOwned);

        viewModel.RepositoryUrl = "git@github.com:owner/RepoB.git";
        Assert.Equal(Path.Combine(_root, "RepoB"), viewModel.LocalDirectory);
        Assert.False(viewModel.TargetIsUserOwned);

        var custom = Path.Combine(_root, "custom");
        viewModel.LocalDirectory = custom;
        Assert.True(viewModel.TargetIsUserOwned);

        viewModel.RepositoryUrl = "https://github.com/owner/RepoC.git";
        Assert.Equal(custom, viewModel.LocalDirectory);
    }

    [Fact]
    public async Task BrowseMakesTargetUserOwned()
    {
        var settings = await CreateSettingsAsync();
        var selected = Path.Combine(_root, "selected");
        var viewModel = new CloneRepositoryViewModel(
            new RecordingCloneService(),
            settings,
            new FixedFolderPicker(selected));

        viewModel.Reset();
        viewModel.RepositoryUrl = "https://github.com/owner/RepoA.git";
        await viewModel.BrowseAsync();

        Assert.Equal(selected, viewModel.LocalDirectory);
        Assert.True(viewModel.TargetIsUserOwned);

        viewModel.RepositoryUrl = "https://github.com/owner/RepoB.git";
        Assert.Equal(selected, viewModel.LocalDirectory);
    }

    [Fact]
    public async Task UnresolvedRepositoryNameLeavesAutomaticTargetEmpty()
    {
        var settings = await CreateSettingsAsync();
        var viewModel = new CloneRepositoryViewModel(
            new RecordingCloneService(),
            settings,
            new FixedFolderPicker(null));

        viewModel.Reset();
        viewModel.RepositoryUrl = "https://github.com/owner/.git";

        Assert.Equal(string.Empty, viewModel.LocalDirectory);
        Assert.False(viewModel.CanClone);
        Assert.False(viewModel.TargetIsUserOwned);
    }

    [Fact]
    public async Task CloneTrimsSourceAndPublishesNormalizedClonedPath()
    {
        var settings = await CreateSettingsAsync();
        var clone = new RecordingCloneService();
        var viewModel = new CloneRepositoryViewModel(
            clone,
            settings,
            new FixedFolderPicker(null));
        var target = Path.Combine(_root, "clone");

        viewModel.Reset();
        viewModel.RepositoryUrl = "  git@github.com:owner/repo.git  ";
        viewModel.LocalDirectory = target;

        var result = await viewModel.CloneAsync();

        Assert.True(result);
        Assert.Equal("git@github.com:owner/repo.git", clone.RepositoryUrl);
        Assert.Equal(target, clone.TargetPath);
        Assert.Equal(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(target)),
            viewModel.ClonedPath);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task CancelPropagatesToActiveClone()
    {
        var settings = await CreateSettingsAsync();
        var clone = new BlockingCloneService();
        var viewModel = new CloneRepositoryViewModel(
            clone,
            settings,
            new FixedFolderPicker(null));

        viewModel.Reset();
        viewModel.RepositoryUrl = "https://github.com/owner/repo.git";
        var cloneTask = viewModel.CloneAsync();

        await clone.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(viewModel.IsBusy);

        viewModel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await cloneTask);
        Assert.False(viewModel.IsBusy);
        Assert.Null(viewModel.ClonedPath);
    }

    private async Task<JsonAppSettingsService> CreateSettingsAsync()
    {
        var service = new JsonAppSettingsService(Path.Combine(_root, "settings.json"));
        await service.SetDefaultRepositoriesDirectoryAsync(_root);
        return service;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class FixedFolderPicker(string? path) : IFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(path);
        }
    }

    private sealed class RecordingCloneService : IRepositoryCloneService
    {
        public string? RepositoryUrl { get; private set; }
        public string? TargetPath { get; private set; }

        public Task CloneAsync(
            string repositoryUrl,
            string targetPath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RepositoryUrl = repositoryUrl;
            TargetPath = targetPath;
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingCloneService : IRepositoryCloneService
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task CloneAsync(
            string repositoryUrl,
            string targetPath,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}
