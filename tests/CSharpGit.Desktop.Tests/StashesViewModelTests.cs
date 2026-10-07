using System.ComponentModel;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Desktop.Tests;

public sealed class StashesViewModelTests
{
    [Fact]
    public async Task RefreshPreservesSelectionByCommitAndFallsBackToPreviousIndex()
    {
        var context = new FakeContext(Repository());
        using var viewModel = new StashesViewModel(new FakeStashMutationService());
        viewModel.Attach(context);

        var first = Stash("stash@{0}", "a");
        var selected = Stash("stash@{1}", "b");
        var third = Stash("stash@{2}", "c");
        viewModel.ApplyRepositoryState([first, selected, third]);
        await viewModel.SelectStashAsync(selected);

        var replacement = Stash("stash@{1}", "b");
        viewModel.ApplyRepositoryState([first, replacement, third]);

        Assert.Same(replacement, viewModel.SelectedStash);

        viewModel.ApplyRepositoryState([first, third]);

        Assert.Same(third, viewModel.SelectedStash);
    }

    [Fact]
    public async Task SelectedPathStashUsesParentMutationLifecycleAndSemanticPaths()
    {
        var mutation = new FakeStashMutationService();
        var context = new FakeContext(Repository())
        {
            WorkingTreeChanges =
            [
                new WorkingTreeChange("renamed.txt", 'M', ' ', "old.txt"),
                new WorkingTreeChange("new.txt", '?', '?')
            ]
        };
        using var viewModel = new StashesViewModel(mutation);
        viewModel.Attach(context);

        await viewModel.CreateSelectedStashAsync(context.WorkingTreeChanges, "picked");

        Assert.Equal(1, context.MutationRuns);
        var request = Assert.IsType<CreateStashRequest>(mutation.LastCreateRequest);
        Assert.Equal(StashScope.SelectedPaths, request.Scope);
        Assert.Equal("picked", request.Message);
        Assert.Collection(
            request.Paths!,
            path =>
            {
                Assert.Equal("renamed.txt", path.Path);
                Assert.Equal("old.txt", path.OriginalPath);
                Assert.False(path.IsUntracked);
            },
            path =>
            {
                Assert.Equal("new.txt", path.Path);
                Assert.True(path.IsUntracked);
            });
    }

    private static Repository Repository() =>
        new("/repo", "/repo", "/repo/.git", false);

    private static GitStash Stash(string name, string commit) =>
        new(name, commit, name);

    private sealed class FakeContext(Repository repository) : IStashesRepositoryContext
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public Repository? Repository { get; set; } = repository;
        public bool CanCreateStash { get; set; } = true;
        public bool CanMutateStash { get; set; } = true;
        public IReadOnlyList<WorkingTreeChange> WorkingTreeChanges { get; set; } = [];
        public int MutationRuns { get; private set; }

        public async Task<bool> RunStashMutationAsync(
            Repository expectedRepository,
            Func<Task> mutation,
            string errorContext)
        {
            Assert.Same(Repository, expectedRepository);
            MutationRuns++;
            await mutation();
            return true;
        }

        public void Raise(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class FakeStashMutationService : IStashMutationService
    {
        public CreateStashRequest? LastCreateRequest { get; private set; }

        public Task CreateStashAsync(
            Repository repository,
            string? message = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task CreateStashAsync(
            Repository repository,
            CreateStashRequest request,
            CancellationToken cancellationToken = default)
        {
            LastCreateRequest = request;
            return Task.CompletedTask;
        }

        public Task ApplyStashAsync(
            Repository repository,
            string stashName,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ApplyStashAsync(
            Repository repository,
            GitStash stash,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task PopStashAsync(
            Repository repository,
            string stashName,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task PopStashAsync(
            Repository repository,
            GitStash stash,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DropStashAsync(
            Repository repository,
            GitStash stash,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
