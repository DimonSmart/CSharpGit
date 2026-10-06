using System.ComponentModel;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Application.Tests;

public sealed class RepositoryFilesViewModelTests
{
    [Fact]
    public async Task LoadPublishesSnapshotForCurrentCommit()
    {
        var repository = Repository("repo");
        var service = new FakeRepositorySnapshotService
        {
            Tree =
            [
                File("README.md"),
                File("src/App.cs")
            ]
        };
        var context = new FakeRepositoryFilesContext(repository, "commit-a");
        using var viewModel = CreateViewModel(service, context);

        await viewModel.SetActiveAsync(true);

        Assert.Equal(2, viewModel.Snapshot.Count);
        Assert.Equal(repository, viewModel.CurrentRepository);
        Assert.Equal("commit-a", viewModel.CurrentCommit);
        Assert.True(viewModel.SnapshotMatchesSelection);
        Assert.False(viewModel.IsLoading);
        Assert.Null(viewModel.ErrorMessage);
        Assert.NotEmpty(viewModel.TreeRoots);
    }

    [Fact]
    public async Task CommitSwitchRejectsStaleSnapshotResult()
    {
        var repository = Repository("repo");
        var firstResult = new TaskCompletionSource<IReadOnlyList<RepositorySnapshotEntry>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeRepositorySnapshotService
        {
            ReadTreeHandler = (_, commit, _) => commit == "commit-a"
                ? firstResult.Task
                : Task.FromResult<IReadOnlyList<RepositorySnapshotEntry>>([File("b.txt")])
        };
        var context = new FakeRepositoryFilesContext(repository, "commit-a");
        using var viewModel = CreateViewModel(service, context);

        var firstLoad = viewModel.SetActiveAsync(true);
        context.SelectedObjectCommit = "commit-b";
        await WaitForAsync(() => viewModel.CurrentCommit == "commit-b");

        firstResult.SetResult([File("a.txt")]);
        await firstLoad;

        Assert.Equal("commit-b", viewModel.CurrentCommit);
        Assert.Single(viewModel.Snapshot);
        Assert.Equal("b.txt", viewModel.Snapshot[0].Path);
    }

    [Fact]
    public async Task RepositorySwitchRejectsOldRepositoryCompletion()
    {
        var first = Repository("first");
        var second = Repository("second");
        var firstResult = new TaskCompletionSource<IReadOnlyList<RepositorySnapshotEntry>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeRepositorySnapshotService
        {
            ReadTreeHandler = (repository, _, _) => ReferenceEquals(repository, first)
                ? firstResult.Task
                : Task.FromResult<IReadOnlyList<RepositorySnapshotEntry>>([File("second.txt")])
        };
        var context = new FakeRepositoryFilesContext(first, "commit");
        using var viewModel = CreateViewModel(service, context);

        var firstLoad = viewModel.SetActiveAsync(true);
        context.Repository = second;
        await WaitForAsync(() => ReferenceEquals(viewModel.CurrentRepository, second));

        firstResult.SetResult([File("first.txt")]);
        await firstLoad;

        Assert.Same(second, viewModel.CurrentRepository);
        Assert.Single(viewModel.Snapshot);
        Assert.Equal("second.txt", viewModel.Snapshot[0].Path);
    }

    [Fact]
    public async Task DeactivationCancelsLoadWithoutPublishingError()
    {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeRepositorySnapshotService
        {
            ReadTreeHandler = async (_, _, cancellationToken) =>
            {
                entered.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return [];
            }
        };
        var context = new FakeRepositoryFilesContext(Repository("repo"), "commit");
        using var viewModel = CreateViewModel(service, context);

        var load = viewModel.SetActiveAsync(true);
        await entered.Task;
        await viewModel.SetActiveAsync(false);
        await load;

        Assert.False(viewModel.IsActive);
        Assert.False(viewModel.IsLoading);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task NameFilterIsLocalAndDoesNotReloadGitSnapshot()
    {
        var service = new FakeRepositorySnapshotService
        {
            Tree =
            [
                File("README.md"),
                File("src/App.cs"),
                File("src/Feature.cs")
            ]
        };
        var context = new FakeRepositoryFilesContext(Repository("repo"), "commit");
        using var viewModel = CreateViewModel(service, context);
        await viewModel.SetActiveAsync(true);

        viewModel.SetNameQuery("src/");

        Assert.Equal(1, service.ReadTreeCalls);
        Assert.Single(viewModel.TreeRoots);
        Assert.Equal("src", viewModel.TreeRoots[0].DisplayName);
        Assert.Equal(2, viewModel.TreeRoots[0].Children.Count);
    }

    [Fact]
    public async Task ContentSearchIsExplicitAndFiltersNonRegularEntries()
    {
        var service = new FakeRepositorySnapshotService
        {
            Tree =
            [
                File("src/App.cs"),
                new RepositorySnapshotEntry(
                    "link",
                    RepositorySnapshotEntryKind.Symlink,
                    "def",
                    "120000",
                    "blob")
            ],
            SearchResults =
            [
                new RepositoryContentSearchMatch("src/App.cs", 7, "needle"),
                new RepositoryContentSearchMatch("link", 1, "needle")
            ]
        };
        var context = new FakeRepositoryFilesContext(Repository("repo"), "commit");
        using var viewModel = CreateViewModel(service, context);
        await viewModel.SetActiveAsync(true);

        viewModel.SetSearchMode(RepositoryFilesViewModel.ContentSearchMode, string.Empty);
        Assert.Equal(0, service.SearchCalls);

        await viewModel.SearchContentAsync("needle");

        Assert.Equal(1, service.SearchCalls);
        Assert.Single(viewModel.ContentResults);
        Assert.Equal("src/App.cs", viewModel.ContentResults[0].Match.Path);
    }

    [Fact]
    public async Task SelectionBelongsToFeatureAndIsClearedForNewCommit()
    {
        var service = new FakeRepositorySnapshotService
        {
            ReadTreeHandler = (_, commit, _) => Task.FromResult<IReadOnlyList<RepositorySnapshotEntry>>(
                commit == "commit-a"
                    ? [File("src/App.cs")]
                    : [File("other.txt")])
        };
        var context = new FakeRepositoryFilesContext(Repository("repo"), "commit-a");
        using var viewModel = CreateViewModel(service, context);
        await viewModel.SetActiveAsync(true);

        var selected = FindNode(viewModel.TreeRoots, "src/App.cs");
        Assert.NotNull(selected);
        viewModel.SelectTreeNode(selected);
        Assert.Equal("src/App.cs", viewModel.SelectedPath);
        Assert.Equal("src/App.cs", viewModel.SelectedEntry?.Path);

        context.SelectedObjectCommit = "commit-b";
        await WaitForAsync(() => viewModel.CurrentCommit == "commit-b");

        Assert.Null(viewModel.SelectedEntry);
        Assert.Null(viewModel.SelectedPath);
    }

    [Fact]
    public async Task ResolveFileVersionUsesCurrentCommitAndRejectsStaleContext()
    {
        var expected = new DiffFileVersion(
            DiffFileVersionLocation.GitSnapshot,
            "src/App.cs",
            RevisionIdentity: "commit-a",
            BlobId: "abc");
        var service = new FakeRepositorySnapshotService { Tree = [File("src/App.cs")], Version = expected };
        var context = new FakeRepositoryFilesContext(Repository("repo"), "commit-a");
        using var viewModel = CreateViewModel(service, context);
        await viewModel.SetActiveAsync(true);
        var entry = Assert.Single(viewModel.Snapshot);

        var version = await viewModel.ResolveFileVersionAsync(entry);

        Assert.Equal(expected, version);
        Assert.Equal("commit-a", service.LastResolveCommit);
        Assert.Equal("src/App.cs", service.LastResolvePath);
    }

    [Fact]
    public async Task LoadFailureIsFeatureStateAndCancellationIsNot()
    {
        var service = new FakeRepositorySnapshotService
        {
            ReadTreeHandler = (_, _, _) =>
                Task.FromException<IReadOnlyList<RepositorySnapshotEntry>>(
                    new InvalidOperationException("broken tree"))
        };
        var context = new FakeRepositoryFilesContext(Repository("repo"), "commit");
        using var viewModel = CreateViewModel(service, context);

        await viewModel.SetActiveAsync(true);

        Assert.Equal("broken tree", viewModel.ErrorMessage);
        Assert.Contains("Could not load repository files", viewModel.StatusText, StringComparison.Ordinal);
        Assert.False(viewModel.IsLoading);
        Assert.Empty(viewModel.Snapshot);
    }

    private static RepositoryFilesViewModel CreateViewModel(
        FakeRepositorySnapshotService service,
        FakeRepositoryFilesContext context)
    {
        var viewModel = new RepositoryFilesViewModel(service);
        viewModel.Attach(context);
        return viewModel;
    }

    private static RepositorySnapshotEntry File(string path) =>
        new(path, RepositorySnapshotEntryKind.File, "abc", "100644", "blob");

    private static Repository Repository(string name) =>
        new($"/work/{name}", $"/work/{name}", $"/work/{name}/.git", false);

    private static RepositorySnapshotTreeNode? FindNode(
        IEnumerable<RepositorySnapshotTreeNode> nodes,
        string path)
    {
        foreach (var node in nodes)
        {
            if (string.Equals(node.Path, path, StringComparison.Ordinal))
                return node;
            if (FindNode(node.Children, path) is { } match)
                return match;
        }

        return null;
    }

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        for (var attempt = 0; attempt < 100 && !predicate(); attempt++)
            await Task.Yield();
        Assert.True(predicate());
    }

    private sealed class FakeRepositoryFilesContext : IRepositoryFilesContext
    {
        private Repository? _repository;
        private string? _selectedObjectCommit;
        private bool _hasSelectedStash;

        public FakeRepositoryFilesContext(Repository? repository, string? selectedObjectCommit)
        {
            _repository = repository;
            _selectedObjectCommit = selectedObjectCommit;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public Repository? Repository
        {
            get => _repository;
            set
            {
                if (ReferenceEquals(_repository, value)) return;
                _repository = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Repository)));
            }
        }

        public string? SelectedObjectCommit
        {
            get => _selectedObjectCommit;
            set
            {
                if (string.Equals(_selectedObjectCommit, value, StringComparison.Ordinal)) return;
                _selectedObjectCommit = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedObjectCommit)));
            }
        }

        public bool HasSelectedStash
        {
            get => _hasSelectedStash;
            set
            {
                if (_hasSelectedStash == value) return;
                _hasSelectedStash = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSelectedStash)));
            }
        }
    }

    private sealed class FakeRepositorySnapshotService : IRepositorySnapshotService
    {
        public IReadOnlyList<RepositorySnapshotEntry> Tree { get; set; } = [];
        public IReadOnlyList<RepositoryContentSearchMatch> SearchResults { get; set; } = [];
        public DiffFileVersion Version { get; set; } =
            new(DiffFileVersionLocation.GitSnapshot, "file.txt", "commit", "abc");
        public Func<Repository, string, CancellationToken, Task<IReadOnlyList<RepositorySnapshotEntry>>>? ReadTreeHandler { get; set; }
        public int ReadTreeCalls { get; private set; }
        public int SearchCalls { get; private set; }
        public string? LastResolveCommit { get; private set; }
        public string? LastResolvePath { get; private set; }

        public Task<IReadOnlyList<RepositorySnapshotEntry>> ReadTreeAsync(
            Repository repository,
            string commitHash,
            CancellationToken cancellationToken = default)
        {
            ReadTreeCalls++;
            return ReadTreeHandler?.Invoke(repository, commitHash, cancellationToken)
                   ?? Task.FromResult(Tree);
        }

        public Task<IReadOnlyList<RepositoryContentSearchMatch>> SearchContentAsync(
            Repository repository,
            string commitHash,
            string query,
            CancellationToken cancellationToken = default)
        {
            SearchCalls++;
            return Task.FromResult(SearchResults);
        }

        public Task<DiffFileVersion> ResolveFileVersionAsync(
            Repository repository,
            string commitHash,
            string path,
            CancellationToken cancellationToken = default)
        {
            LastResolveCommit = commitHash;
            LastResolvePath = path;
            return Task.FromResult(Version);
        }
    }
}
