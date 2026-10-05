using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Application.Tests;

public sealed class RepositoryIdentitySettingsViewModelTests
{
    [Fact]
    public async Task CleanRepositoryChangeReloadsCurrentRepository()
    {
        var repositoryA = RepositoryAt("repo-a");
        var repositoryB = RepositoryAt("repo-b");
        Repository? current = repositoryA;
        var service = new FakeIdentityService
        {
            ReadHandler = (repository, _) => Task.FromResult(
                Snapshot(Path.GetFileName(repository.WorkingDirectory)))
        };
        var viewModel = new RepositoryIdentitySettingsViewModel(service, () => current);

        await viewModel.RefreshAsync(force: true);
        current = repositoryB;
        await viewModel.RefreshAsync();

        Assert.Equal(repositoryB.WorkingDirectory, viewModel.RepositoryPath);
        Assert.Equal("repo-b", viewModel.NameEffectiveDisplay);
        Assert.True(viewModel.HasRepositoryReloadNotice);
        Assert.False(viewModel.IsStale);
    }

    [Fact]
    public async Task DirtyRepositoryChangePreservesOldBufferAndDisablesMutation()
    {
        var repositoryA = RepositoryAt("repo-a");
        var repositoryB = RepositoryAt("repo-b");
        Repository? current = repositoryA;
        var service = new FakeIdentityService
        {
            ReadHandler = (_, _) => Task.FromResult(Snapshot("Inherited"))
        };
        var viewModel = new RepositoryIdentitySettingsViewModel(service, () => current);

        await viewModel.RefreshAsync(force: true);
        viewModel.NameText = "Unsaved A";
        current = repositoryB;
        await viewModel.RefreshAsync();

        Assert.True(viewModel.IsStale);
        Assert.Equal("Unsaved A", viewModel.NameText);
        Assert.False(viewModel.CanSave);
        Assert.False(viewModel.CanRemoveNameOverride);
        Assert.Empty(service.Saves);
    }

    [Fact]
    public async Task SaveRechecksRepositoryImmediatelyBeforeMutation()
    {
        var repositoryA = RepositoryAt("repo-a");
        var repositoryB = RepositoryAt("repo-b");
        Repository? current = repositoryA;
        var service = new FakeIdentityService
        {
            ReadHandler = (_, _) => Task.FromResult(Snapshot("Inherited"))
        };
        var viewModel = new RepositoryIdentitySettingsViewModel(service, () => current);

        await viewModel.RefreshAsync(force: true);
        viewModel.NameText = "Local A";
        Assert.True(viewModel.CanSave);
        current = repositoryB;

        await Assert.ThrowsAsync<InvalidOperationException>(() => viewModel.SaveAsync());

        Assert.Empty(service.Saves);
        Assert.True(viewModel.IsStale);
    }

    [Fact]
    public async Task StaleAsyncReadFromPreviousRepositoryIsNotPublished()
    {
        var repositoryA = RepositoryAt("repo-a");
        var repositoryB = RepositoryAt("repo-b");
        Repository? current = repositoryA;
        var aCompletion = new TaskCompletionSource<RepositoryIdentitySnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeIdentityService
        {
            ReadHandler = (repository, _) =>
                repository.WorkingDirectory == repositoryA.WorkingDirectory
                    ? aCompletion.Task
                    : Task.FromResult(Snapshot("B"))
        };
        var viewModel = new RepositoryIdentitySettingsViewModel(service, () => current);

        var staleRead = viewModel.RefreshAsync(force: true);
        current = repositoryB;
        await viewModel.RefreshAsync(force: true);
        aCompletion.SetResult(Snapshot("A"));
        await staleRead;

        Assert.Equal(repositoryB.WorkingDirectory, viewModel.RepositoryPath);
        Assert.Equal("B", viewModel.NameEffectiveDisplay);
    }

    [Fact]
    public async Task ReturningToLoadedRepositoryMakesDirtyBufferValidAgain()
    {
        var repositoryA = RepositoryAt("repo-a");
        var repositoryB = RepositoryAt("repo-b");
        Repository? current = repositoryA;
        var service = new FakeIdentityService
        {
            ReadHandler = (_, _) => Task.FromResult(Snapshot("Inherited"))
        };
        var viewModel = new RepositoryIdentitySettingsViewModel(service, () => current);

        await viewModel.RefreshAsync(force: true);
        viewModel.NameText = "Unsaved A";
        current = repositoryB;
        await viewModel.RefreshAsync();
        Assert.True(viewModel.IsStale);

        current = repositoryA;
        await viewModel.RefreshAsync();

        Assert.False(viewModel.IsStale);
        Assert.True(viewModel.CanSave);
        Assert.Equal("Unsaved A", viewModel.NameText);
    }

    [Fact]
    public async Task PartialSaveFailureKeepsOnlyUnappliedEditDirty()
    {
        var repository = RepositoryAt("repo");
        RepositoryIdentitySnapshot authoritative = Snapshot("Global");
        var service = new FakeIdentityService
        {
            ReadHandler = (_, _) => Task.FromResult(authoritative)
        };
        service.SaveHandler = (_, _, _) =>
        {
            authoritative = new RepositoryIdentitySnapshot(
                new GitIdentityValue("Local Name", GitConfigSource.Repository, ".git/config", "Local Name", true),
                authoritative.Email);
            throw new InvalidOperationException("email write failed");
        };
        var viewModel = new RepositoryIdentitySettingsViewModel(service, () => repository);

        await viewModel.RefreshAsync(force: true);
        viewModel.NameText = "Local Name";
        viewModel.EmailText = "local@example.com";

        await Assert.ThrowsAsync<InvalidOperationException>(() => viewModel.SaveAsync());

        Assert.False(viewModel.IsNameDirty);
        Assert.True(viewModel.IsEmailDirty);
        Assert.Equal("Local Name", viewModel.NameText);
        Assert.Equal("local@example.com", viewModel.EmailText);
    }

    [Fact]
    public async Task ExplicitInheritedValueIsDirtyBecauseDirectOverrideIsAbsent()
    {
        var repository = RepositoryAt("repo");
        var service = new FakeIdentityService
        {
            ReadHandler = (_, _) => Task.FromResult(Snapshot("Global User"))
        };
        var viewModel = new RepositoryIdentitySettingsViewModel(service, () => repository);

        await viewModel.RefreshAsync(force: true);
        Assert.Equal("Global User", viewModel.NamePlaceholder);
        Assert.Equal(string.Empty, viewModel.NameText);

        viewModel.NameText = "Global User";

        Assert.True(viewModel.IsNameDirty);
        Assert.True(viewModel.CanSave);
    }

    [Theory]
    [InlineData("john@", false)]
    [InlineData("@company.com", false)]
    [InlineData("john company.com", false)]
    [InlineData("john@localhost", true)]
    [InlineData("john+work@example.com", true)]
    public async Task EmailValidationIsIntentionallySoft(string value, bool valid)
    {
        var repository = RepositoryAt("repo");
        var service = new FakeIdentityService
        {
            ReadHandler = (_, _) => Task.FromResult(Snapshot("Global"))
        };
        var viewModel = new RepositoryIdentitySettingsViewModel(service, () => repository);
        await viewModel.RefreshAsync(force: true);

        viewModel.EmailText = value;

        Assert.Equal(valid, !viewModel.HasEmailValidationError);
        Assert.Equal(valid, viewModel.CanSave);
    }

    private static Repository RepositoryAt(string name)
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "csharpgit-identity-vm", name));
        return new Repository(path, path, Path.Combine(path, ".git"), false);
    }

    private static RepositoryIdentitySnapshot Snapshot(string name) =>
        new(
            new GitIdentityValue(name, GitConfigSource.Global, "global.gitconfig", null, false),
            new GitIdentityValue("global@example.com", GitConfigSource.Global, "global.gitconfig", null, false));

    private sealed class FakeIdentityService : IRepositoryIdentityService
    {
        internal Func<Repository, CancellationToken, Task<RepositoryIdentitySnapshot>> ReadHandler { get; init; } =
            (_, _) => Task.FromResult(Snapshot("Global"));

        internal Func<Repository, RepositoryIdentityEdit, CancellationToken, Task>? SaveHandler { get; set; }

        internal List<(Repository Repository, RepositoryIdentityEdit Edit)> Saves { get; } = [];

        public Task<RepositoryIdentitySnapshot> ReadAsync(
            Repository repository,
            CancellationToken cancellationToken = default) =>
            ReadHandler(repository, cancellationToken);

        public Task SaveAsync(
            Repository repository,
            RepositoryIdentityEdit edit,
            CancellationToken cancellationToken = default)
        {
            Saves.Add((repository, edit));
            return SaveHandler?.Invoke(repository, edit, cancellationToken) ?? Task.CompletedTask;
        }

        public Task RemoveOverrideAsync(
            Repository repository,
            RepositoryIdentityField field,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
