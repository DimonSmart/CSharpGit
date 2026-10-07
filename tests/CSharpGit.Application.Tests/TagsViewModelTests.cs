using System.ComponentModel;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Application.Tests;

public sealed class TagsViewModelTests
{
    [Fact]
    public async Task CreateTagUsesExpectedCurrentRepositoryAndRejectsStaleRepository()
    {
        var service = new FakeTagService();
        var firstRepository = Repository("first");
        var secondRepository = Repository("second");
        var context = new FakeTagsRepositoryContext { Repository = firstRepository };
        var viewModel = CreateViewModel(service, context);
        var request = new CreateTagRequest("v1.0", "abc", GitTagKind.Annotated, "release");

        var firstSucceeded = await viewModel.CreateTagAsync(firstRepository, request);

        Assert.True(firstSucceeded);
        Assert.Same(firstRepository, service.LastRepository);
        Assert.Equal(request, service.LastCreateRequest);
        Assert.Equal(1, context.MutationLifecycleCalls);
        Assert.Equal("Could not create tag", context.LastErrorContext);
        Assert.True(context.LastIncludeHistory);

        context.Repository = secondRepository;

        var staleSucceeded = await viewModel.CreateTagAsync(firstRepository, request);

        Assert.False(staleSucceeded);
        Assert.Equal(1, context.MutationLifecycleCalls);
        Assert.Same(firstRepository, service.LastRepository);

        var secondSucceeded = await viewModel.CreateTagAsync(secondRepository, request);

        Assert.True(secondSucceeded);
        Assert.Equal(2, context.MutationLifecycleCalls);
        Assert.Same(secondRepository, service.LastRepository);
    }

    [Fact]
    public async Task CreateTagFailureReturnsFalseWithoutStartingAnotherLifecycle()
    {
        var service = new FakeTagService { CreateException = new InvalidOperationException("failed") };
        var context = new FakeTagsRepositoryContext { Repository = Repository("repo") };
        var viewModel = CreateViewModel(service, context);

        var succeeded = await viewModel.CreateTagAsync(
            context.Repository!,
            new CreateTagRequest("v1", "abc", GitTagKind.Lightweight));

        Assert.False(succeeded);
        Assert.Equal(1, context.MutationLifecycleCalls);
        Assert.IsType<InvalidOperationException>(context.LastFailure);
    }

    [Fact]
    public async Task CreateTagCancellationPropagates()
    {
        var service = new FakeTagService { CreateException = new OperationCanceledException() };
        var context = new FakeTagsRepositoryContext { Repository = Repository("repo") };
        var viewModel = CreateViewModel(service, context);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            viewModel.CreateTagAsync(
                context.Repository!,
                new CreateTagRequest("v1", "abc", GitTagKind.Lightweight)));
        Assert.Equal(1, context.MutationLifecycleCalls);
    }

    [Fact]
    public async Task DeleteTagWithRemoteValidatesObjectAndDeletesRemoteBeforeLocal()
    {
        var service = new FakeTagService
        {
            RemoteTag = new RemoteTagInfo("origin", "v1", "tag-object", "abc", true)
        };
        var context = new FakeTagsRepositoryContext { Repository = Repository("repo") };
        var viewModel = CreateViewModel(service, context);
        var tag = new GitTag("v1", "abc", GitTagKind.Annotated, "tag-object");
        var remote = Remote("origin");

        var result = await viewModel.DeleteTagAsync(context.Repository!, tag, remote);

        Assert.True(result.Succeeded);
        Assert.False(result.RemoteWasMissing);
        Assert.Equal(
            ["read-remote:origin:v1", "delete-remote:origin:v1", "delete-local:v1"],
            service.Calls);
        Assert.True(context.LastIncludeHistory);
    }

    [Fact]
    public async Task DeleteTagRejectsRemoteThatChangedWithoutDeletingAnything()
    {
        var service = new FakeTagService
        {
            RemoteTag = new RemoteTagInfo("origin", "v1", "different-object", "def", true)
        };
        var context = new FakeTagsRepositoryContext { Repository = Repository("repo") };
        var viewModel = CreateViewModel(service, context);
        var tag = new GitTag("v1", "abc", GitTagKind.Annotated, "local-object");

        var result = await viewModel.DeleteTagAsync(context.Repository!, tag, Remote("origin"));

        Assert.False(result.Succeeded);
        Assert.Equal(["read-remote:origin:v1"], service.Calls);
        Assert.IsType<InvalidOperationException>(context.LastFailure);
    }

    [Fact]
    public async Task DeleteTagWhenRemoteIsMissingDeletesLocalAndReportsOutcome()
    {
        var service = new FakeTagService { RemoteTag = null };
        var context = new FakeTagsRepositoryContext { Repository = Repository("repo") };
        var viewModel = CreateViewModel(service, context);

        var result = await viewModel.DeleteTagAsync(
            context.Repository!,
            new GitTag("v1", "abc"),
            Remote("origin"));

        Assert.True(result.Succeeded);
        Assert.True(result.RemoteWasMissing);
        Assert.Equal("origin", result.RemoteName);
        Assert.Equal(["read-remote:origin:v1", "delete-local:v1"], service.Calls);
    }

    [Fact]
    public async Task PushTagUsesMutationLifecycleWithoutHistoryRefresh()
    {
        var expected = new PushTagResult(PushTagResultKind.Pushed, "pushed");
        var service = new FakeTagService { PushResult = expected };
        var context = new FakeTagsRepositoryContext { Repository = Repository("repo") };
        var viewModel = CreateViewModel(service, context);

        var result = await viewModel.PushTagAsync(
            context.Repository!,
            new GitTag("v1", "abc"),
            Remote("origin"));

        Assert.Equal(expected, result);
        Assert.Equal("Could not push tag", context.LastErrorContext);
        Assert.False(context.LastIncludeHistory);
        Assert.Equal(["push:origin:v1"], service.Calls);
    }

    [Fact]
    public void RepositoryStatePreservesSelectionByNameAndClearsRemovedSelection()
    {
        var service = new FakeTagService();
        var context = new FakeTagsRepositoryContext { Repository = Repository("repo") };
        var viewModel = CreateViewModel(service, context);
        var selected = new GitTag("v1", "abc", GitTagKind.Annotated, "tag-object");
        var other = new GitTag("v2", "def");

        viewModel.ApplyRepositoryState([selected, other]);
        viewModel.SelectedTag = selected;

        var refreshedSelected = selected with { Message = "refreshed" };
        var refreshedOther = other with { TargetCommit = "def2" };
        viewModel.ApplyRepositoryState([refreshedSelected, refreshedOther]);

        Assert.Equal(["v1", "v2"], viewModel.Items.Select(tag => tag.Name));
        Assert.Same(refreshedSelected, viewModel.SelectedTag);

        viewModel.ApplyRepositoryState([refreshedOther]);

        Assert.Single(viewModel.Items);
        Assert.Null(viewModel.SelectedTag);

        viewModel.ClearRepositoryState();

        Assert.Empty(viewModel.Items);
        Assert.Null(viewModel.SelectedTag);
    }

    [Fact]
    public void RepositorySwitchClearsTagState()
    {
        var service = new FakeTagService();
        var context = new FakeTagsRepositoryContext { Repository = Repository("first") };
        var viewModel = CreateViewModel(service, context);
        var tag = new GitTag("v1", "abc");

        viewModel.ApplyRepositoryState([tag]);
        viewModel.SelectedTag = tag;

        context.Repository = Repository("second");

        Assert.Empty(viewModel.Items);
        Assert.Null(viewModel.SelectedTag);
    }

    [Fact]
    public async Task CheckoutCommandUsesReferenceServiceAndMutationLifecycle()
    {
        var service = new FakeTagService();
        var referenceService = new FakeReferenceService();
        var repository = Repository("repo");
        var context = new FakeTagsRepositoryContext { Repository = repository };
        var viewModel = CreateViewModel(service, context, referenceService);
        var tag = new GitTag("v1", "abc");

        viewModel.ApplyRepositoryState([tag]);
        viewModel.SelectedTag = tag;

        Assert.True(viewModel.CheckoutTagCommand.CanExecute(null));

        await ((AsyncCommand)viewModel.CheckoutTagCommand).ExecuteAsync();

        Assert.Equal(1, context.MutationLifecycleCalls);
        Assert.Null(context.LastErrorContext);
        Assert.True(context.LastIncludeHistory);
        Assert.Equal(1, referenceService.CheckoutCalls);
        Assert.Same(repository, referenceService.LastCheckoutRepository);
        Assert.Equal("v1", referenceService.LastCheckoutReference);
    }

    [Fact]
    public void CheckoutAvailabilityFollowsRepositorySelectionBusyAndOperation()
    {
        var service = new FakeTagService();
        var context = new FakeTagsRepositoryContext { Repository = Repository("repo") };
        var viewModel = CreateViewModel(service, context);
        var tag = new GitTag("v1", "abc");

        viewModel.ApplyRepositoryState([tag]);
        Assert.False(viewModel.CheckoutTagCommand.CanExecute(null));

        viewModel.SelectedTag = tag;
        Assert.True(viewModel.CanCheckoutSelectedTag);
        Assert.True(viewModel.CheckoutTagCommand.CanExecute(null));

        context.IsBusy = true;
        Assert.False(viewModel.CheckoutTagCommand.CanExecute(null));

        context.IsBusy = false;
        context.CurrentOperation = RepositoryOperation.Rebase;
        Assert.False(viewModel.CheckoutTagCommand.CanExecute(null));

        context.CurrentOperation = RepositoryOperation.None;
        Assert.True(viewModel.CheckoutTagCommand.CanExecute(null));

        context.Repository = null;
        Assert.False(viewModel.CheckoutTagCommand.CanExecute(null));
        Assert.Null(viewModel.SelectedTag);
        Assert.Empty(viewModel.Items);
    }

    [Fact]
    public async Task CheckoutRejectsStaleRepository()
    {
        var service = new FakeTagService();
        var referenceService = new FakeReferenceService();
        var currentRepository = Repository("current");
        var staleRepository = Repository("stale");
        var context = new FakeTagsRepositoryContext { Repository = currentRepository };
        var viewModel = CreateViewModel(service, context, referenceService);

        var succeeded = await viewModel.CheckoutTagAsync(
            staleRepository,
            new GitTag("v1", "abc"));

        Assert.False(succeeded);
        Assert.Equal(0, referenceService.CheckoutCalls);
        Assert.Equal(0, context.MutationLifecycleCalls);
    }

    [Fact]
    public void AvailabilityAndPreferredRemoteFollowLiveRepositoryContext()
    {
        var service = new FakeTagService();
        var context = new FakeTagsRepositoryContext
        {
            Repository = Repository("first"),
            Remotes = [Remote("origin"), Remote("backup")],
            LocalBranches = [new GitBranch("main", "abc", true, "backup/main")]
        };
        var viewModel = CreateViewModel(service, context);

        Assert.True(viewModel.CanMutate);
        Assert.Equal("backup", viewModel.PreferredRemote?.Name);

        context.IsBusy = true;
        Assert.False(viewModel.CanMutate);
        context.IsBusy = false;
        context.CurrentOperation = RepositoryOperation.Rebase;
        Assert.False(viewModel.CanMutate);
        context.CurrentOperation = RepositoryOperation.None;
        context.Repository = null;
        Assert.False(viewModel.CanMutate);

        context.Repository = Repository("second");
        context.Remotes = [Remote("single")];
        context.LocalBranches = [];
        Assert.True(viewModel.CanMutate);
        Assert.Equal("single", viewModel.PreferredRemote?.Name);
    }

    [Fact]
    public async Task RemoteTagReadFailureIsMappedToPresentationResult()
    {
        var service = new FakeTagService
        {
            ReadRemoteTagsException = new InvalidOperationException("network failed")
        };
        var context = new FakeTagsRepositoryContext { Repository = Repository("repo") };
        var viewModel = CreateViewModel(service, context);

        var result = await viewModel.ReadRemoteTagsAsync(context.Repository!, Remote("origin"));

        Assert.False(result.Succeeded);
        Assert.Empty(result.Value);
        Assert.Equal("network failed", result.ErrorMessage);
    }

    private static TagsViewModel CreateViewModel(
        FakeTagService service,
        FakeTagsRepositoryContext context,
        FakeReferenceService? referenceService = null)
    {
        var viewModel = new TagsViewModel(
            service,
            referenceService ?? new FakeReferenceService());
        viewModel.Attach(context);
        return viewModel;
    }

    private static Repository Repository(string name) =>
        new($"/work/{name}", $"/work/{name}", $"/work/{name}/.git", false);

    private static GitRemote Remote(string name) =>
        new(name, $"https://example.test/{name}", $"https://example.test/{name}");

    private sealed class FakeTagsRepositoryContext : ITagsRepositoryContext
    {
        private Repository? _repository;
        private bool _isBusy;
        private RepositoryOperation _currentOperation;

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

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (_isBusy == value) return;
                _isBusy = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBusy)));
            }
        }

        public RepositoryOperation CurrentOperation
        {
            get => _currentOperation;
            set
            {
                if (_currentOperation == value) return;
                _currentOperation = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentOperation)));
            }
        }

        public IReadOnlyList<GitRemote> Remotes { get; set; } = [];
        public IReadOnlyList<GitBranch> LocalBranches { get; set; } = [];
        public int MutationLifecycleCalls { get; private set; }
        public string? LastErrorContext { get; private set; }
        public bool LastIncludeHistory { get; private set; }
        public Exception? LastFailure { get; private set; }

        public async Task<bool> RunTagMutationAsync(
            Repository expectedRepository,
            Func<Task> mutation,
            string? errorContext,
            bool includeHistory)
        {
            if (!ReferenceEquals(Repository, expectedRepository)) return false;

            MutationLifecycleCalls++;
            LastErrorContext = errorContext;
            LastIncludeHistory = includeHistory;
            LastFailure = null;
            try
            {
                await mutation();
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                LastFailure = exception;
                return false;
            }
        }
    }

    private sealed class FakeReferenceService : IReferenceService
    {
        public Repository? LastCheckoutRepository { get; private set; }
        public string? LastCheckoutReference { get; private set; }
        public int CheckoutCalls { get; private set; }

        public Task SwitchBranchAsync(
            Repository repository,
            string branch,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task CheckoutAsync(
            Repository repository,
            string reference,
            CancellationToken cancellationToken = default)
        {
            CheckoutCalls++;
            LastCheckoutRepository = repository;
            LastCheckoutReference = reference;
            return Task.CompletedTask;
        }

        public Task CreateBranchAsync(
            Repository repository,
            string branch,
            string? startPoint = null,
            bool switchToBranch = true,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RenameBranchAsync(
            Repository repository,
            string oldName,
            string newName,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteBranchAsync(
            Repository repository,
            string branch,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteBranchAsync(
            Repository repository,
            string branch,
            BranchDeletionMode mode,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task CheckoutRemoteBranchAsync(
            Repository repository,
            string remoteBranch,
            string localBranch,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeTagService : ITagService
    {
        public Repository? LastRepository { get; private set; }
        public CreateTagRequest? LastCreateRequest { get; private set; }
        public Exception? CreateException { get; set; }
        public Exception? ReadRemoteTagsException { get; set; }
        public RemoteTagInfo? RemoteTag { get; set; }
        public IReadOnlyList<RemoteTagInfo> RemoteTags { get; set; } = [];
        public PushTagResult PushResult { get; set; } =
            new(PushTagResultKind.AlreadyUpToDate, "up to date");
        public List<string> Calls { get; } = [];

        public Task<IReadOnlyList<GitTag>> ReadTagsAsync(
            Repository repository,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GitTag>>([]);

        public Task CreateTagAsync(
            Repository repository,
            CreateTagRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRepository = repository;
            LastCreateRequest = request;
            Calls.Add($"create:{request.Name}");
            if (CreateException is not null) return Task.FromException(CreateException);
            return Task.CompletedTask;
        }

        public Task DeleteTagAsync(
            Repository repository,
            string tagName,
            CancellationToken cancellationToken = default)
        {
            LastRepository = repository;
            Calls.Add($"delete-local:{tagName}");
            return Task.CompletedTask;
        }

        public Task FetchTagsAsync(
            Repository repository,
            string remote,
            CancellationToken cancellationToken = default)
        {
            LastRepository = repository;
            Calls.Add($"fetch:{remote}");
            return Task.CompletedTask;
        }

        public Task<PushTagResult> PushTagAsync(
            Repository repository,
            string remote,
            string tagName,
            CancellationToken cancellationToken = default)
        {
            LastRepository = repository;
            Calls.Add($"push:{remote}:{tagName}");
            return Task.FromResult(PushResult);
        }

        public Task PushAllTagsAsync(
            Repository repository,
            string remote,
            CancellationToken cancellationToken = default)
        {
            LastRepository = repository;
            Calls.Add($"push-all:{remote}");
            return Task.CompletedTask;
        }

        public Task DeleteRemoteTagAsync(
            Repository repository,
            RemoteTagInfo expectedTag,
            CancellationToken cancellationToken = default)
        {
            LastRepository = repository;
            Calls.Add($"delete-remote:{expectedTag.Remote}:{expectedTag.Name}");
            return Task.CompletedTask;
        }

        public Task<RemoteTagInfo?> ReadRemoteTagAsync(
            Repository repository,
            string remote,
            string tagName,
            CancellationToken cancellationToken = default)
        {
            LastRepository = repository;
            Calls.Add($"read-remote:{remote}:{tagName}");
            return Task.FromResult(RemoteTag);
        }

        public Task<IReadOnlyList<RemoteTagInfo>> ReadRemoteTagsAsync(
            Repository repository,
            string remote,
            CancellationToken cancellationToken = default)
        {
            LastRepository = repository;
            Calls.Add($"read-remotes:{remote}");
            if (ReadRemoteTagsException is not null)
                return Task.FromException<IReadOnlyList<RemoteTagInfo>>(ReadRemoteTagsException);
            return Task.FromResult(RemoteTags);
        }

        public Task ForceUpdateRemoteTagAsync(
            Repository repository,
            RemoteTagConflictSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            LastRepository = repository;
            Calls.Add($"force-update:{snapshot.Remote}:{snapshot.TagName}");
            return Task.CompletedTask;
        }
    }
}
